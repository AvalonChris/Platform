using Dapper;
using Npgsql;

namespace Platform.Web.Data;

public class AccountSubscription
{
    public long Id { get; init; }
    public string Status { get; init; } = "";
    public int FrequencyDays { get; init; }
    public DateTime? NextChargeOn { get; init; }
    public string Items { get; init; } = "";
    public decimal Subtotal { get; init; }
    public string? PaymentMethodType { get; init; }
    public int FailedAttempts { get; init; }
    public string? LastFailure { get; init; }
}

public class AccountOrder
{
    public string OrderNumber { get; init; } = "";
    public Guid PublicToken { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; init; } = "";
    public decimal Total { get; init; }
    public bool IsRenewal { get; init; }
}

/// <summary>Customer sign-in links and the self-service subscription actions on the account page.</summary>
public class CustomerAccountRepository(NpgsqlDataSource dataSource)
{
    public static readonly int[] Frequencies = [30, 60, 90];

    public async Task<long?> FindCustomerIdAsync(string email)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long?>(
            "select id from customers where lower(email) = lower(@email)", new { email = email.Trim() });
    }

    public async Task<string?> GetEmailAsync(long customerId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "select email from customers where id = @customerId", new { customerId });
    }

    public async Task CreateLoginTokenAsync(long customerId, string tokenHash, DateTime expiresAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            insert into customer_login_tokens (token_hash, customer_id, expires_at)
            values (@tokenHash, @customerId, @expiresAt)
            """,
            new { tokenHash, customerId, expiresAt });
    }

    /// <summary>Uses up a sign-in token. Returns the customer, or null if it's unknown, used or expired.</summary>
    public async Task<long?> ConsumeLoginTokenAsync(string tokenHash)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long?>(
            """
            update customer_login_tokens set used_at = now()
            where token_hash = @tokenHash and used_at is null and expires_at > now()
            returning customer_id
            """,
            new { tokenHash });
    }

    public async Task<IReadOnlyList<AccountSubscription>> GetSubscriptionsAsync(long customerId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var subscriptions = await connection.QueryAsync<AccountSubscription>(
            """
            select s.id, s.status, s.frequency_days, s.next_charge_on, s.payment_method_type,
                   s.failed_attempts, s.last_failure,
                   string_agg(si.quantity || ' × ' || p.name, ', ' order by p.name) as items,
                   sum(si.quantity * si.unit_price) as subtotal
            from subscriptions s
            join subscription_items si on si.subscription_id = s.id
            join products p on p.id = si.product_id
            where s.customer_id = @customerId
            group by s.id
            order by case s.status when 'active' then 0 when 'paused' then 1 else 2 end, s.next_charge_on
            """,
            new { customerId });
        return subscriptions.AsList();
    }

    /// <summary>Paid and later orders; unpaid checkouts are left out.</summary>
    public async Task<IReadOnlyList<AccountOrder>> GetOrdersAsync(long customerId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var orders = await connection.QueryAsync<AccountOrder>(
            """
            select order_number, public_token, created_at, status, total, subscription_id is not null as is_renewal
            from orders
            where customer_id = @customerId and placed_at is not null
            order by id desc
            limit 50
            """,
            new { customerId });
        return orders.AsList();
    }

    /// <summary>Moves the next renewal back by one period.</summary>
    public Task<bool> SkipAsync(long customerId, long subscriptionId) => ChangeAsync(customerId, subscriptionId,
        """
        update subscriptions
        set next_charge_on = next_charge_on + frequency_days, failed_attempts = 0, retry_after = null, last_failure = null
        where id = @subscriptionId and customer_id = @customerId and status = 'active'
        """);

    public Task<bool> PauseAsync(long customerId, long subscriptionId) => ChangeAsync(customerId, subscriptionId,
        """
        update subscriptions set status = 'paused', paused_at = now()
        where id = @subscriptionId and customer_id = @customerId and status = 'active'
        """);

    /// <summary>Resumes a paused subscription. A renewal date that has passed while paused moves to tomorrow.</summary>
    public Task<bool> ResumeAsync(long customerId, long subscriptionId) => ChangeAsync(customerId, subscriptionId,
        """
        update subscriptions
        set status = 'active', paused_at = null, failed_attempts = 0, retry_after = null, last_failure = null,
            next_charge_on = greatest(next_charge_on, current_date + 1)
        where id = @subscriptionId and customer_id = @customerId and status = 'paused'
        """);

    public Task<bool> CancelAsync(long customerId, long subscriptionId) => ChangeAsync(customerId, subscriptionId,
        """
        update subscriptions set status = 'cancelled', cancelled_at = now()
        where id = @subscriptionId and customer_id = @customerId and status in ('active', 'paused')
        """);

    public Task<bool> ChangeFrequencyAsync(long customerId, long subscriptionId, int frequencyDays) =>
        Frequencies.Contains(frequencyDays)
            ? ChangeAsync(customerId, subscriptionId,
                """
                update subscriptions set frequency_days = @frequencyDays
                where id = @subscriptionId and customer_id = @customerId and status in ('active', 'paused')
                """,
                frequencyDays)
            : Task.FromResult(false);

    /// <summary>
    /// Applies one change to the customer's own subscription. Any unpaid renewal order left from a failed attempt
    /// is cancelled, since the period it was for no longer applies.
    /// </summary>
    private async Task<bool> ChangeAsync(long customerId, long subscriptionId, string sql, int frequencyDays = 0)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var changed = await connection.ExecuteAsync(sql, new { customerId, subscriptionId, frequencyDays }, transaction) > 0;
        if (changed)
        {
            await connection.ExecuteAsync(
                "update orders set status = 'cancelled' where subscription_id = @subscriptionId and status = 'pending'",
                new { subscriptionId }, transaction);
        }

        await transaction.CommitAsync();
        return changed;
    }
}
