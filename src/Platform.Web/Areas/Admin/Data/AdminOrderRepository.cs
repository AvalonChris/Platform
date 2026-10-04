using Dapper;
using Npgsql;
using Platform.Web.Areas.Admin.Models;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin.Data;

public class AdminOrderRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<Order>> ListAsync(string? status)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var orders = await connection.QueryAsync<Order>(
            """
            select id, order_number, email, status, total, created_at, placed_at, refunded_total, dispute_status
            from orders
            where @status::text is null or status = @status
            order by id desc
            limit 200
            """,
            new { status });
        return orders.AsList();
    }

    public async Task<OrderViewModel?> GetAsync(long id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var order = await connection.QuerySingleOrDefaultAsync<Order>(
            """
            select id, order_number, email, status, ship_full_name, ship_line1, ship_line2, ship_city,
                   ship_region, ship_postal_code, subtotal, shipping_total, tax_total, total,
                   payment_provider, payment_reference, created_at, placed_at,
                   fulfillment_status, fulfillment_reference, fulfillment_error,
                   public_token, shipped_at, tracking_carrier, tracking_number,
                   refunded_total, refunded_at, dispute_id, dispute_status, dispute_reason
            from orders
            where id = @id
            """,
            new { id });
        if (order is null)
            return null;

        var items = await connection.QueryAsync<OrderItem>(
            """
            select product_name, purchase_mode, frequency_days, quantity, unit_price, line_total
            from order_items
            where order_id = @id
            order by id
            """,
            new { id });

        return new OrderViewModel(order, items.AsList());
    }

    /// <summary>Marks a paid order as shipped, with optional tracking. Returns false if it wasn't paid.</summary>
    public async Task<bool> MarkShippedAsync(long id, string? carrier, string? trackingNumber)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteAsync(
            """
            update orders
            set status = 'fulfilled', shipped_at = now(), tracking_carrier = @carrier, tracking_number = @trackingNumber
            where id = @id and status = 'paid'
            """,
            new { id, carrier, trackingNumber }) > 0;
    }

    /// <summary>Cancels an unshipped order and any subscriptions it started. Does not refund.</summary>
    public async Task<bool> CancelAsync(long id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var cancelled = await connection.ExecuteAsync(
            "update orders set status = 'cancelled' where id = @id and status in ('pending', 'paid')",
            new { id }, transaction) > 0;

        if (cancelled)
        {
            await connection.ExecuteAsync(
                """
                update subscriptions set status = 'cancelled', cancelled_at = now()
                where origin_order_id = @id and status <> 'cancelled'
                """,
                new { id }, transaction);
        }

        await transaction.CommitAsync();
        return cancelled;
    }

    public async Task<IReadOnlyList<SubscriptionRow>> ListSubscriptionsAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var subscriptions = await connection.QueryAsync<SubscriptionRow>(
            """
            select s.id, c.email, s.status, s.frequency_days, s.next_charge_on, s.created_at,
                   s.payment_provider, s.payment_method_type, s.payment_method_reference is not null as has_saved_method,
                   s.failed_attempts, s.last_failure,
                   string_agg(si.quantity || ' × ' || p.name, ', ' order by p.name) as items,
                   sum(si.quantity * si.unit_price) as renewal_total
            from subscriptions s
            join customers c on c.id = s.customer_id
            join subscription_items si on si.subscription_id = s.id
            join products p on p.id = si.product_id
            group by s.id, c.email
            order by s.id desc
            limit 200
            """);
        return subscriptions.AsList();
    }
}
