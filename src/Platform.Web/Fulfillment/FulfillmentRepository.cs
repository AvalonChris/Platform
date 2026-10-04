using Dapper;
using Npgsql;

namespace Platform.Web.Fulfillment;

public class FulfillmentOrder
{
    public long Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public string Email { get; init; } = "";
    public string ShipFullName { get; init; } = "";
    public string ShipLine1 { get; init; } = "";
    public string ShipLine2 { get; init; } = "";
    public string ShipCity { get; init; } = "";
    public string ShipRegion { get; init; } = "";
    public string ShipPostalCode { get; init; } = "";
    public string ShipCountry { get; init; } = "";
    public string? PaymentProvider { get; init; }
}

public class FulfillmentLine
{
    public string Name { get; init; } = "";
    public string? VariantId { get; init; }
    public int Quantity { get; init; }
}

public class FulfillmentRepository(NpgsqlDataSource dataSource)
{
    /// <summary>Paid orders waiting to be sent, plus failed ones that still have attempts left.</summary>
    public async Task<IReadOnlyList<FulfillmentOrder>> GetDueAsync(int maxAttempts)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var orders = await connection.QueryAsync<FulfillmentOrder>(
            """
            select id, order_number, email, ship_full_name, ship_line1, ship_line2, ship_city,
                   ship_region, ship_postal_code, ship_country, payment_provider
            from orders
            where status = 'paid'
              and (fulfillment_status = 'pending'
                   or (fulfillment_status = 'failed' and fulfillment_attempts < @maxAttempts))
            order by id
            limit 20
            """,
            new { maxAttempts });
        return orders.AsList();
    }

    /// <summary>What to ship: stacks are replaced by their component products.</summary>
    public async Task<IReadOnlyList<FulfillmentLine>> GetLinesAsync(long orderId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var lines = await connection.QueryAsync<FulfillmentLine>(
            """
            select p.name, p.fulfillment_variant_id as variant_id,
                   sum(oi.quantity * coalesce(si.quantity, 1))::int as quantity
            from order_items oi
            join products ordered on ordered.id = oi.product_id
            left join stack_items si on ordered.kind = 'stack' and si.stack_product_id = ordered.id
            join products p on p.id = coalesce(si.component_product_id, ordered.id)
            where oi.order_id = @orderId
            group by p.id, p.name, p.fulfillment_variant_id
            order by p.name
            """,
            new { orderId });
        return lines.AsList();
    }

    public Task MarkSentAsync(long orderId, string reference) => UpdateAsync(
        """
        update orders
        set fulfillment_status = 'sent', fulfillment_reference = @reference, fulfillment_error = null,
            fulfillment_attempts = fulfillment_attempts + 1, fulfillment_updated_at = now()
        where id = @orderId
        """,
        new { orderId, reference });

    public Task MarkFailedAsync(long orderId, string error) => UpdateAsync(
        """
        update orders
        set fulfillment_status = 'failed', fulfillment_error = @error,
            fulfillment_attempts = fulfillment_attempts + 1, fulfillment_updated_at = now()
        where id = @orderId
        """,
        new { orderId, error });

    public Task MarkSkippedAsync(long orderId, string reason) => UpdateAsync(
        """
        update orders
        set fulfillment_status = 'skipped', fulfillment_error = @reason, fulfillment_updated_at = now()
        where id = @orderId
        """,
        new { orderId, reason });

    /// <summary>Queues a failed or skipped order to be sent again. Returns false if it can't be.</summary>
    public async Task<bool> RequeueAsync(long orderId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteAsync(
            """
            update orders
            set fulfillment_status = 'pending', fulfillment_error = null, fulfillment_attempts = 0,
                fulfillment_updated_at = now()
            where id = @orderId and status = 'paid' and fulfillment_status in ('failed', 'skipped')
            """,
            new { orderId }) > 0;
    }

    private async Task UpdateAsync(string sql, object parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }
}
