using Dapper;
using Npgsql;

namespace Platform.Web.Data;

public class WebhookEventRow
{
    public string EventId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string? ResourceId { get; init; }
    public DateTime ReceivedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public string? Error { get; init; }
}

public class PayPalWebhookRepository(NpgsqlDataSource dataSource)
{
    /// <summary>Stores the event if it's new. Returns true if it was already processed (PayPal sends retries).</summary>
    public async Task<bool> RecordAsync(string eventId, string eventType, string? resourceId, string payload)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            insert into paypal_webhook_events (event_id, event_type, resource_id, payload)
            values (@eventId, @eventType, @resourceId, @payload::jsonb)
            on conflict (event_id) do nothing
            """,
            new { eventId, eventType, resourceId, payload });

        return await connection.ExecuteScalarAsync<bool>(
            "select processed_at is not null from paypal_webhook_events where event_id = @eventId", new { eventId });
    }

    public async Task MarkProcessedAsync(string eventId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update paypal_webhook_events set processed_at = now(), error = null where event_id = @eventId", new { eventId });
    }

    public async Task MarkFailedAsync(string eventId, string error)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update paypal_webhook_events set error = @error where event_id = @eventId", new { eventId, error });
    }

    /// <summary>The order that a PayPal order (checkout session) was created for.</summary>
    public async Task<Guid?> FindOrderTokenBySessionAsync(string payPalOrderId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<Guid?>(
            "select public_token from orders where payment_session_id = @payPalOrderId", new { payPalOrderId });
    }

    /// <summary>
    /// Records the total refunded so far on the order paid by this capture. A full refund marks the order
    /// refunded. Returns the order number, or null if no order has this capture.
    /// </summary>
    public async Task<string?> ApplyRefundAsync(string captureId, decimal? totalRefunded, decimal refundAmount)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>(
            """
            update orders
            set refunded_total = least(total, coalesce(@totalRefunded, refunded_total + @refundAmount)),
                refunded_at = now(),
                status = case
                    when least(total, coalesce(@totalRefunded, refunded_total + @refundAmount)) >= total then 'refunded'
                    else status
                end
            where payment_reference = @captureId
            returning order_number
            """,
            new { captureId, totalRefunded, refundAmount });
    }

    /// <summary>A reversal takes the whole payment back, for example after a chargeback.</summary>
    public async Task<string?> ApplyReversalAsync(string captureId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>(
            """
            update orders set refunded_total = total, refunded_at = now(), status = 'refunded'
            where payment_reference = @captureId
            returning order_number
            """,
            new { captureId });
    }

    public async Task<IReadOnlyList<string>> ApplyDisputeAsync(
        IReadOnlyList<string> captureIds, string disputeId, string? status, string? reason)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var orders = await connection.QueryAsync<string>(
            """
            update orders
            set dispute_id = @disputeId, dispute_status = @status, dispute_reason = @reason, dispute_updated_at = now()
            where payment_reference = any(@captureIds)
            returning order_number
            """,
            new { captureIds = captureIds.ToArray(), disputeId, status, reason });
        return orders.AsList();
    }

    /// <summary>The customer removed a saved payment method in PayPal, so it can't be charged for renewals any more.</summary>
    public async Task<int> RemoveSavedPaymentMethodAsync(string tokenId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteAsync(
            """
            update subscriptions
            set payment_method_reference = null, last_failure = 'Saved payment method was removed in PayPal'
            where payment_method_reference = @tokenId
            """,
            new { tokenId });
    }

    public async Task<IReadOnlyList<WebhookEventRow>> ListRecentAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var events = await connection.QueryAsync<WebhookEventRow>(
            """
            select event_id, event_type, resource_id, received_at, processed_at, error
            from paypal_webhook_events
            order by received_at desc
            limit 100
            """);
        return events.AsList();
    }
}
