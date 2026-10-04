using Dapper;
using Npgsql;

namespace Platform.Web.Subscriptions;

public class DueSubscription
{
    public long Id { get; init; }
    public string Email { get; init; } = "";
    public int FrequencyDays { get; init; }
    public DateTime NextChargeOn { get; init; }
    public string? PaymentProvider { get; init; }
    public string? PaymentMethodReference { get; init; }
    public string? PaymentMethodType { get; init; }
    public int FailedAttempts { get; init; }
}

public class RenewalOrder
{
    public long Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal Total { get; init; }
    public string Items { get; init; } = "";
    public string ShipFullName { get; init; } = "";
    public string ShipLine1 { get; init; } = "";
    public string ShipLine2 { get; init; } = "";
    public string ShipCity { get; init; } = "";
    public string ShipRegion { get; init; } = "";
    public string ShipPostalCode { get; init; } = "";
}

public class ReminderDue
{
    public long SubscriptionId { get; init; }
    public string Email { get; init; } = "";
    public DateTime NextChargeOn { get; init; }
    public string? PaymentMethodType { get; init; }
    public string Items { get; init; } = "";
    public decimal Subtotal { get; init; }
}

public class RenewalRepository(NpgsqlDataSource dataSource)
{
    /// <summary>Active subscriptions whose renewal date has arrived and which aren't waiting out a retry delay.</summary>
    public async Task<IReadOnlyList<DueSubscription>> GetDueAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var due = await connection.QueryAsync<DueSubscription>(
            """
            select s.id, c.email, s.frequency_days, s.next_charge_on, s.payment_provider,
                   s.payment_method_reference, s.payment_method_type, s.failed_attempts
            from subscriptions s
            join customers c on c.id = s.customer_id
            where s.status = 'active'
              and s.next_charge_on <= current_date
              and (s.retry_after is null or s.retry_after <= current_date)
            order by s.next_charge_on, s.id
            limit 50
            """);
        return due.AsList();
    }

    /// <summary>
    /// The renewal order for this subscription's current period, created on first call at the price the customer
    /// signed up for. Calling it again for the same period returns the same order.
    /// </summary>
    public async Task<RenewalOrder> GetOrCreateRenewalOrderAsync(DueSubscription subscription, Func<decimal, decimal> shippingFor)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var subtotal = await connection.ExecuteScalarAsync<decimal>(
            "select coalesce(sum(quantity * unit_price), 0) from subscription_items where subscription_id = @Id",
            subscription, transaction);
        var shipping = shippingFor(subtotal);

        var parameters = new
        {
            subscription.Id,
            period = subscription.NextChargeOn,
            subtotal,
            shipping,
            total = subtotal + shipping
        };

        // Inserting draws an order number even when the insert is skipped, so check first to avoid gaps.
        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from orders where subscription_id = @Id and renewal_period = @period)",
            parameters, transaction);

        var orderId = exists ? null : await connection.ExecuteScalarAsync<long?>(
            """
            insert into orders (customer_id, email, ship_full_name, ship_line1, ship_line2, ship_city, ship_region,
                                ship_postal_code, ship_country, shipping_address_id, subtotal, shipping_total, total,
                                subscription_id, renewal_period)
            select s.customer_id, c.email, a.full_name, a.line1, a.line2, a.city, a.region,
                   a.postal_code, a.country, a.id, @subtotal, @shipping, @total, s.id, @period
            from subscriptions s
            join customers c on c.id = s.customer_id
            join addresses a on a.id = s.shipping_address_id
            where s.id = @Id
            on conflict (subscription_id, renewal_period) where subscription_id is not null do nothing
            returning id
            """,
            parameters, transaction);

        if (orderId is not null)
        {
            await connection.ExecuteAsync(
                """
                insert into order_items (order_id, product_id, product_name, purchase_mode, frequency_days,
                                         quantity, unit_price, line_total)
                select @orderId, si.product_id, p.name, 'subscription', s.frequency_days,
                       si.quantity, si.unit_price, si.quantity * si.unit_price
                from subscription_items si
                join subscriptions s on s.id = si.subscription_id
                join products p on p.id = si.product_id
                where si.subscription_id = @Id
                """,
                new { orderId, subscription.Id }, transaction);
        }

        var order = await connection.QuerySingleAsync<RenewalOrder>(
            """
            select o.id, o.order_number, o.status, o.total, o.ship_full_name, o.ship_line1, o.ship_line2,
                   o.ship_city, o.ship_region, o.ship_postal_code,
                   (select string_agg(oi.quantity || ' × ' || oi.product_name, ', ' order by oi.product_name)
                    from order_items oi where oi.order_id = o.id) as items
            from orders o
            where o.subscription_id = @Id and o.renewal_period = @period
            """,
            parameters, transaction);

        await transaction.CommitAsync();
        return order;
    }

    /// <summary>Marks the renewal order paid and moves the subscription on to its next period.</summary>
    /// <param name="paymentMethodType">Recorded on the subscription if it wasn't known yet.</param>
    public async Task MarkRenewalPaidAsync(
        RenewalOrder order, DueSubscription subscription, string provider, string? reference, string? paymentMethodType)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(
            """
            update orders
            set status = 'paid', placed_at = now(), payment_provider = @provider, payment_reference = @reference,
                fulfillment_status = 'pending'
            where id = @orderId and status = 'pending'
            """,
            new { orderId = order.Id, provider, reference }, transaction);

        await connection.ExecuteAsync(
            "update subscriptions set payment_method_type = coalesce(payment_method_type, @paymentMethodType) where id = @Id",
            new { subscription.Id, paymentMethodType }, transaction);

        await AdvanceAsync(connection, transaction, subscription);
        await transaction.CommitAsync();
    }

    /// <summary>For a period whose order was already paid (for example, before a crash): just move on.</summary>
    public async Task AdvanceAsync(DueSubscription subscription)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await AdvanceAsync(connection, transaction, subscription);
        await transaction.CommitAsync();
    }

    private static Task AdvanceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DueSubscription subscription) =>
        connection.ExecuteAsync(
            """
            update subscriptions
            set next_charge_on = next_charge_on + frequency_days,
                failed_attempts = 0, retry_after = null, last_failure = null
            where id = @Id and next_charge_on = @NextChargeOn
            """,
            subscription, transaction);

    public async Task RecordFailureAsync(long subscriptionId, string reason, DateTime retryAfter)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            update subscriptions
            set failed_attempts = failed_attempts + 1, retry_after = @retryAfter, last_failure = @reason
            where id = @subscriptionId
            """,
            new { subscriptionId, reason, retryAfter });
    }

    /// <summary>Gives up after repeated failures: pauses the subscription and cancels the unpaid renewal order.</summary>
    public async Task PauseForFailureAsync(long subscriptionId, long orderId, string reason)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(
            """
            update subscriptions
            set status = 'paused', paused_at = now(), failed_attempts = failed_attempts + 1,
                retry_after = null, last_failure = @reason
            where id = @subscriptionId
            """,
            new { subscriptionId, reason }, transaction);

        await connection.ExecuteAsync(
            "update orders set status = 'cancelled' where id = @orderId and status = 'pending'",
            new { orderId }, transaction);

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<ReminderDue>> GetRemindersDueAsync(int daysBefore)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var reminders = await connection.QueryAsync<ReminderDue>(
            """
            select s.id as subscription_id, c.email, s.next_charge_on, s.payment_method_type,
                   string_agg(si.quantity || ' × ' || p.name, ', ' order by p.name) as items,
                   sum(si.quantity * si.unit_price) as subtotal
            from subscriptions s
            join customers c on c.id = s.customer_id
            join subscription_items si on si.subscription_id = s.id
            join products p on p.id = si.product_id
            where s.status = 'active'
              and s.next_charge_on = current_date + @daysBefore
              and s.reminder_sent_for is distinct from s.next_charge_on
            group by s.id, c.email
            """,
            new { daysBefore });
        return reminders.AsList();
    }

    public async Task MarkReminderSentAsync(long subscriptionId, DateTime renewalDate)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update subscriptions set reminder_sent_for = @renewalDate where id = @subscriptionId",
            new { subscriptionId, renewalDate });
    }
}
