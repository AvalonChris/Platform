using Dapper;
using Npgsql;
using Platform.Web.Models;
using Platform.Web.Payments;

namespace Platform.Web.Data;

/// <summary>An order between checkout and payment: what's needed to take payment and then mark it paid.</summary>
public class PendingOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public Guid PublicToken { get; set; }
    public long CustomerId { get; set; }
    public long? AddressId { get; set; }
    public string Email { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public bool HasSubscription { get; set; }
    public string ShipFullName { get; set; } = "";
    public string ShipLine1 { get; set; } = "";
    public string ShipLine2 { get; set; } = "";
    public string ShipCity { get; set; } = "";
    public string ShipRegion { get; set; } = "";
    public string ShipPostalCode { get; set; } = "";

    public bool IsPending => Status == "pending";
}

public class OrderRepository(NpgsqlDataSource dataSource)
{
    /// <summary>Saves the customer, address, order and its items. The order stays 'pending' until payment succeeds.</summary>
    public async Task<PendingOrder> CreatePendingAsync(CheckoutForm form, CartViewModel cart, decimal shipping)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var email = form.Email.Trim();

        // The no-op update makes the statement return the id of an existing customer too.
        var customerId = await connection.ExecuteScalarAsync<long>(
            """
            insert into customers (email, full_name) values (@email, @FullName)
            on conflict (lower(email)) do update set email = customers.email
            returning id
            """,
            new { email, form.FullName }, transaction);

        var address = new
        {
            customerId,
            form.FullName,
            form.Line1,
            Line2 = form.Line2 ?? "",
            form.City,
            form.Region,
            form.PostalCode,
            Phone = form.Phone ?? ""
        };

        var addressId = await connection.ExecuteScalarAsync<long>(
            """
            insert into addresses (customer_id, full_name, line1, line2, city, region, postal_code, country, phone)
            values (@customerId, @FullName, @Line1, @Line2, @City, @Region, @PostalCode, 'US', @Phone)
            returning id
            """,
            address, transaction);

        var (orderId, token) = await connection.QuerySingleAsync<(long, Guid)>(
            """
            insert into orders (customer_id, email, ship_full_name, ship_line1, ship_line2, ship_city,
                                ship_region, ship_postal_code, ship_country, shipping_address_id,
                                subtotal, shipping_total, total)
            values (@customerId, @email, @FullName, @Line1, @Line2, @City,
                    @Region, @PostalCode, 'US', @addressId,
                    @Subtotal, @shipping, @total)
            returning id, public_token
            """,
            new
            {
                customerId, email, address.FullName, address.Line1, address.Line2, address.City,
                address.Region, address.PostalCode, addressId, cart.Subtotal, shipping, total = cart.Subtotal + shipping
            },
            transaction);

        await connection.ExecuteAsync(
            """
            insert into order_items (order_id, product_id, product_name, purchase_mode, frequency_days,
                                     quantity, unit_price, line_total)
            values (@orderId, @ProductId, @ProductName, @PurchaseMode, @FrequencyDays,
                    @Quantity, @UnitPrice, @LineTotal)
            """,
            cart.Items.Select(item => new
            {
                orderId,
                item.Line.ProductId,
                ProductName = item.Line.Name,
                item.Line.PurchaseMode,
                item.Line.FrequencyDays,
                item.Line.Quantity,
                item.UnitPrice,
                item.LineTotal
            }),
            transaction);

        await transaction.CommitAsync();
        return await GetPendingAsync(token) ?? throw new InvalidOperationException("The new order could not be read back.");
    }

    public async Task<PendingOrder?> GetPendingAsync(Guid token)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<PendingOrder>(
            """
            select o.id, o.order_number, o.public_token, o.customer_id, o.shipping_address_id as address_id,
                   o.email, o.status, o.total, o.ship_full_name, o.ship_line1, o.ship_line2, o.ship_city,
                   o.ship_region, o.ship_postal_code,
                   exists (select 1 from order_items oi
                           where oi.order_id = o.id and oi.purchase_mode = 'subscription') as has_subscription
            from orders o
            where o.public_token = @token
            """,
            new { token });
    }

    /// <summary>Remembers the provider's checkout session (the PayPal order id) for reconciling payments.</summary>
    public async Task SetPaymentSessionAsync(long orderId, string sessionId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update orders set payment_session_id = @sessionId where id = @orderId", new { orderId, sessionId });
    }

    /// <summary>
    /// Marks the order paid, starts its subscriptions and empties the cart. Returns false, changing nothing,
    /// if the order was no longer pending (for example, already marked paid by a repeated request).
    /// </summary>
    public async Task<bool> MarkPaidAsync(PendingOrder order, PaymentResult payment, Guid? cartId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var parameters = new
        {
            orderId = order.Id,
            order.CustomerId,
            order.AddressId,
            payment.Provider,
            payment.Reference,
            payment.PaymentMethodReference,
            payment.PaymentMethodType,
            cartId
        };

        var updated = await connection.ExecuteAsync(
            """
            update orders
            set status = 'paid', placed_at = now(), payment_provider = @Provider, payment_reference = @Reference,
                fulfillment_status = 'pending'
            where id = @orderId and status = 'pending'
            """,
            parameters, transaction);
        if (updated == 0)
            return false;

        // One subscription per delivery frequency in the order.
        await connection.ExecuteAsync(
            """
            insert into subscriptions (customer_id, origin_order_id, shipping_address_id, frequency_days,
                                       next_charge_on, payment_provider, payment_method_reference, payment_method_type)
            select @CustomerId, @orderId, @AddressId, frequency_days,
                   current_date + frequency_days, @Provider, @PaymentMethodReference, @PaymentMethodType
            from order_items
            where order_id = @orderId and purchase_mode = 'subscription'
            group by frequency_days
            """,
            parameters, transaction);

        await connection.ExecuteAsync(
            """
            insert into subscription_items (subscription_id, product_id, quantity, unit_price)
            select s.id, oi.product_id, oi.quantity, oi.unit_price
            from subscriptions s
            join order_items oi on oi.order_id = s.origin_order_id and oi.frequency_days = s.frequency_days
            where s.origin_order_id = @orderId and oi.purchase_mode = 'subscription'
            """,
            parameters, transaction);

        if (cartId is not null)
            await connection.ExecuteAsync("delete from carts where id = @cartId", parameters, transaction);

        await transaction.CommitAsync();
        return true;
    }

    public async Task CancelAsync(long orderId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update orders set status = 'cancelled' where id = @orderId and status = 'pending'", new { orderId });
    }

    public async Task<OrderViewModel?> GetByTokenAsync(Guid token)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var order = await connection.QuerySingleOrDefaultAsync<Order>(
            """
            select id, order_number, public_token, email, status, ship_full_name, ship_line1, ship_line2, ship_city,
                   ship_region, ship_postal_code, subtotal, shipping_total, tax_total, total,
                   shipped_at, tracking_carrier, tracking_number
            from orders
            where public_token = @token
            """,
            new { token });
        if (order is null)
            return null;

        var items = await connection.QueryAsync<OrderItem>(
            """
            select product_name, purchase_mode, frequency_days, quantity, unit_price, line_total
            from order_items
            where order_id = @Id
            order by id
            """,
            new { order.Id });

        return new OrderViewModel(order, items.AsList());
    }
}
