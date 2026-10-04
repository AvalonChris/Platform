using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Email;

namespace Platform.Web.Payments;

public enum WebhookOutcome { Processed, Rejected, Failed }

/// <summary>
/// Handles notifications PayPal sends about payments made on our site: completed payments whose browser
/// confirmation never arrived, refunds, reversals, disputes and deleted saved payment methods.
/// Every event is checked with PayPal first and processed once, however often PayPal resends it.
/// </summary>
public class PayPalWebhooks(
    PayPalClient client,
    PayPalCheckout checkout,
    PayPalWebhookRepository events,
    OrderRepository orders,
    OrderEmails orderEmails,
    IOptions<PayPalOptions> options,
    IHostEnvironment environment,
    ILogger<PayPalWebhooks> logger)
{
    public async Task<WebhookOutcome> HandleAsync(string rawBody, IHeaderDictionary headers)
    {
        if (!await IsGenuineAsync(rawBody, headers))
            return WebhookOutcome.Rejected;

        var json = JsonNode.Parse(rawBody);
        var eventId = PayPalClient.Text(json?["id"]);
        var eventType = PayPalClient.Text(json?["event_type"]);
        var resource = json?["resource"];
        if (eventId is null || eventType is null)
            return WebhookOutcome.Rejected;

        if (await events.RecordAsync(eventId, eventType, PayPalClient.Text(resource?["id"]), rawBody))
            return WebhookOutcome.Processed;

        try
        {
            await DispatchAsync(eventType, resource);
            await events.MarkProcessedAsync(eventId);
            return WebhookOutcome.Processed;
        }
        catch (Exception exception)
        {
            // Not marked processed, so PayPal's retry of the same event will try again.
            logger.LogError(exception, "PayPal webhook {EventId} ({EventType}) failed", eventId, eventType);
            await events.MarkFailedAsync(eventId, exception.Message);
            return WebhookOutcome.Failed;
        }
    }

    private async Task<bool> IsGenuineAsync(string rawBody, IHeaderDictionary headers)
    {
        var settings = options.Value;
        if (settings.SkipWebhookVerification && environment.IsDevelopment())
        {
            logger.LogWarning("Accepting a PayPal webhook without verification (development setting).");
            return true;
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookId) || !settings.IsConfigured)
        {
            logger.LogError("PayPal webhook rejected: Payments:PayPal:WebhookId (or the PayPal credentials) isn't set.");
            return false;
        }

        string Header(string name) => headers[name].ToString();
        var genuine = await client.VerifyWebhookSignatureAsync(
            Header("PAYPAL-TRANSMISSION-ID"), Header("PAYPAL-TRANSMISSION-TIME"), Header("PAYPAL-CERT-URL"),
            Header("PAYPAL-AUTH-ALGO"), Header("PAYPAL-TRANSMISSION-SIG"), settings.WebhookId, rawBody);

        if (!genuine)
            logger.LogWarning("PayPal webhook rejected: signature verification failed.");
        return genuine;
    }

    private Task DispatchAsync(string eventType, JsonNode? resource) => eventType switch
    {
        "CHECKOUT.ORDER.APPROVED" => CompletePaymentAsync(PayPalClient.Text(resource?["id"])),
        "PAYMENT.CAPTURE.COMPLETED" => CompletePaymentAsync(
            PayPalClient.Text(resource?["supplementary_data"]?["related_ids"]?["order_id"])),
        "PAYMENT.CAPTURE.REFUNDED" => RefundAsync(resource),
        "PAYMENT.CAPTURE.REVERSED" => ReversalAsync(resource),
        "CUSTOMER.DISPUTE.CREATED" or "CUSTOMER.DISPUTE.UPDATED" or "CUSTOMER.DISPUTE.RESOLVED" => DisputeAsync(resource),
        "VAULT.PAYMENT-TOKEN.DELETED" => SavedMethodDeletedAsync(resource),
        _ => Task.CompletedTask
    };

    /// <summary>
    /// The customer approved or paid, but our order may still be pending, for example if they closed the browser
    /// before the payment page confirmed it. Capturing again is safe: PayPal returns the original capture.
    /// </summary>
    private async Task CompletePaymentAsync(string? payPalOrderId)
    {
        if (payPalOrderId is null || await events.FindOrderTokenBySessionAsync(payPalOrderId) is not { } token)
            return;

        var pending = await orders.GetPendingAsync(token);
        if (pending is null || !pending.IsPending)
            return;

        var result = await checkout.CaptureAsync(pending, payPalOrderId);
        if (!result.Payment.Approved)
        {
            logger.LogWarning("Webhook could not complete order {OrderNumber}: {Error}", pending.OrderNumber, result.Payment.Error);
            return;
        }

        if (await orders.MarkPaidAsync(pending, result.Payment, cartId: null))
        {
            logger.LogInformation("Order {OrderNumber} marked paid from a PayPal webhook", pending.OrderNumber);
            if (await orders.GetByTokenAsync(token) is { } order)
                await orderEmails.SendConfirmationAsync(order);
        }
    }

    private async Task RefundAsync(JsonNode? refund)
    {
        // The refund links back ("up") to the capture it refunds.
        var captureId = refund?["links"]?.AsArray()
            .Where(link => PayPalClient.Text(link?["rel"]) == "up")
            .Select(link => PayPalClient.Text(link?["href"])?.Split('/').LastOrDefault())
            .FirstOrDefault();
        if (captureId is null)
            return;

        var refundAmount = Decimal(refund?["amount"]?["value"]) ?? 0;
        var totalRefunded = Decimal(refund?["seller_payable_breakdown"]?["total_refunded_amount"]?["value"]);

        var orderNumber = await events.ApplyRefundAsync(captureId, totalRefunded, refundAmount);
        logger.LogInformation("PayPal refund of {Amount} on capture {CaptureId} recorded for order {OrderNumber}",
            refundAmount, captureId, orderNumber ?? "(none)");
    }

    private async Task ReversalAsync(JsonNode? capture)
    {
        if (PayPalClient.Text(capture?["id"]) is not { } captureId)
            return;

        var orderNumber = await events.ApplyReversalAsync(captureId);
        logger.LogWarning("PayPal reversed capture {CaptureId} (order {OrderNumber})", captureId, orderNumber ?? "(none)");
    }

    private async Task DisputeAsync(JsonNode? dispute)
    {
        var disputeId = PayPalClient.Text(dispute?["dispute_id"]);
        var captureIds = dispute?["disputed_transactions"]?.AsArray()
            .Select(transaction => PayPalClient.Text(transaction?["seller_transaction_id"]))
            .OfType<string>()
            .ToList() ?? [];
        if (disputeId is null || captureIds.Count == 0)
            return;

        var status = PayPalClient.Text(dispute?["status"]);
        var reason = PayPalClient.Text(dispute?["reason"]);
        var updated = await events.ApplyDisputeAsync(captureIds, disputeId, status, reason);
        logger.LogWarning("PayPal dispute {DisputeId} ({Status}, {Reason}) on orders {Orders}",
            disputeId, status, reason, string.Join(", ", updated));
    }

    private async Task SavedMethodDeletedAsync(JsonNode? token)
    {
        if (PayPalClient.Text(token?["id"]) is not { } tokenId)
            return;

        var subscriptions = await events.RemoveSavedPaymentMethodAsync(tokenId);
        logger.LogInformation("Saved payment method {TokenId} was deleted in PayPal; {Count} subscriptions affected", tokenId, subscriptions);
    }

    private static decimal? Decimal(JsonNode? node) =>
        decimal.TryParse(PayPalClient.Text(node), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
