using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Models;

namespace Platform.Web.Payments;

/// <param name="Restart">True when the customer should pick another payment method in the PayPal window.</param>
public record PayPalCaptureResult(PaymentResult Payment, bool Restart = false);

public record ShippingDetails(string FullName, string Line1, string Line2, string City, string Region, string PostalCode);

/// <summary>
/// Turns a pending order into a PayPal order, and checks a captured PayPal payment before the order is marked paid.
/// Orders containing subscriptions ask PayPal to save the payment method for renewals.
/// </summary>
public class PayPalCheckout(
    PayPalClient client,
    IOptions<PayPalOptions> payPalOptions,
    IOptions<StoreOptions> storeOptions,
    ILogger<PayPalCheckout> logger)
{
    public const string SourcePayPal = "paypal";
    public const string SourceCard = "card";

    public async Task<string> CreateOrderAsync(PendingOrder order, string source, string returnUrl)
    {
        var address = new JsonObject
        {
            ["address_line_1"] = order.ShipLine1,
            ["admin_area_2"] = order.ShipCity,
            ["admin_area_1"] = order.ShipRegion,
            ["postal_code"] = order.ShipPostalCode,
            ["country_code"] = "US"
        };
        if (order.ShipLine2.Length > 0)
            address["address_line_2"] = order.ShipLine2;

        var purchaseUnit = new JsonObject
        {
            ["reference_id"] = order.OrderNumber,
            ["custom_id"] = order.OrderNumber,
            ["invoice_id"] = order.OrderNumber,
            ["description"] = Truncate($"{storeOptions.Value.BrandName} order {order.OrderNumber}", 127),
            ["amount"] = new JsonObject { ["currency_code"] = "USD", ["value"] = Amount(order.Total) },
            ["shipping"] = new JsonObject
            {
                ["name"] = new JsonObject { ["full_name"] = order.ShipFullName },
                ["address"] = address
            }
        };

        var body = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray(purchaseUnit)
        };

        // Other button sources (Venmo, Pay Later, PayPal's guest card button) send no payment_source, so their
        // payment methods aren't saved. Venmo and Pay Later are only offered on orders without subscriptions.
        if (source == SourcePayPal)
            body["payment_source"] = new JsonObject { ["paypal"] = PayPalSource(order, returnUrl) };
        else if (source == SourceCard)
            body["payment_source"] = new JsonObject { ["card"] = CardSource(order) };

        var created = await client.CreateOrderAsync(body);
        return PayPalClient.Text(created["id"]) ?? throw new PayPalException("PayPal did not return an order id.");
    }

    public async Task<PayPalCaptureResult> CaptureAsync(PendingOrder order, string payPalOrderId)
    {
        JsonNode result;
        try
        {
            result = await client.CaptureOrderAsync(payPalOrderId);
        }
        catch (PayPalException exception) when (exception.Issue == "INSTRUMENT_DECLINED")
        {
            return Declined("That payment method was declined. Please choose another one.", restart: true);
        }
        catch (PayPalException exception) when (exception.Issue == "ORDER_ALREADY_CAPTURED")
        {
            result = await client.GetOrderAsync(payPalOrderId);
        }
        catch (PayPalException exception) when (exception.StatusCode == 422)
        {
            logger.LogWarning("PayPal declined order {OrderNumber}: {Issue} {Message}",
                order.OrderNumber, exception.Issue, exception.Message);
            return Declined("Your payment could not be completed. Please check your details or use another payment method.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // The capture may or may not have happened, so ask PayPal.
            logger.LogWarning(exception, "PayPal capture for order {OrderNumber} timed out; checking its status.", order.OrderNumber);
            result = await client.GetOrderAsync(payPalOrderId);
        }

        var unit = result["purchase_units"]?[0];
        var capture = unit?["payments"]?["captures"]?[0];
        var captureStatus = PayPalClient.Text(capture?["status"]);

        if (PayPalClient.Text(result["status"]) != "COMPLETED" || captureStatus != "COMPLETED")
        {
            logger.LogWarning("PayPal order {PayPalOrderId} for {OrderNumber} not completed: order {Status}, capture {CaptureStatus}",
                payPalOrderId, order.OrderNumber, PayPalClient.Text(result["status"]), captureStatus);
            return Declined(captureStatus == "PENDING"
                ? "PayPal is still reviewing this payment. We'll confirm your order by email once it clears."
                : "Your payment was not completed. Please try again or use another payment method.");
        }

        // Guard against a payment for a different order or amount being used to mark this one paid.
        var customId = PayPalClient.Text(capture?["custom_id"]) ?? PayPalClient.Text(unit?["custom_id"]);
        var amount = PayPalClient.Text(capture?["amount"]?["value"]);
        var currency = PayPalClient.Text(capture?["amount"]?["currency_code"]);
        if (customId != order.OrderNumber || amount != Amount(order.Total) || currency != "USD")
        {
            logger.LogError("PayPal capture {PayPalOrderId} does not match order {OrderNumber}: {CustomId} {Amount} {Currency}",
                payPalOrderId, order.OrderNumber, customId, amount, currency);
            return Declined("Your payment did not match this order. Please contact us before trying again.");
        }

        var paymentSource = result["payment_source"];
        var payPalVaultId = PayPalClient.Text(paymentSource?["paypal"]?["attributes"]?["vault"]?["id"]);
        var cardVaultId = PayPalClient.Text(paymentSource?["card"]?["attributes"]?["vault"]?["id"]);
        var vaultId = payPalVaultId ?? cardVaultId;
        var vaultType = payPalVaultId is not null ? "paypal" : cardVaultId is not null ? "card" : null;
        if (order.HasSubscription && vaultId is null)
            logger.LogWarning("Order {OrderNumber} has subscriptions but PayPal saved no payment method; renewals can't be charged automatically.",
                order.OrderNumber);

        return new PayPalCaptureResult(new PaymentResult(
            true, payPalOptions.Value.ProviderName, PayPalClient.Text(capture?["id"]), null, vaultId, vaultType));
    }

    /// <summary>
    /// Charges a saved payment method without the customer present, for a subscription renewal. The attempt number
    /// makes a retried call after a crash return the original result instead of charging twice.
    /// </summary>
    public async Task<PaymentResult> ChargeSavedMethodAsync(
        string orderNumber, decimal total, ShippingDetails shipping, string vaultId, string? vaultType, int attempt)
    {
        if (vaultType is not null)
            return await ChargeSavedMethodAsAsync(vaultType, orderNumber, total, shipping, vaultId, attempt);

        // The type wasn't recorded (subscriptions from before it was). A token of the wrong type is rejected
        // without charging, so try it as a card first, then as a PayPal account.
        var asCard = await ChargeSavedMethodAsAsync("card", orderNumber, total, shipping, vaultId, attempt);
        return asCard.Approved
            ? asCard
            : await ChargeSavedMethodAsAsync("paypal", orderNumber, total, shipping, vaultId, attempt);
    }

    private async Task<PaymentResult> ChargeSavedMethodAsAsync(
        string vaultType, string orderNumber, decimal total, ShippingDetails shipping, string vaultId, int attempt)
    {
        var paymentSource = vaultType == "card"
            ? new JsonObject
            {
                ["card"] = new JsonObject
                {
                    ["vault_id"] = vaultId,
                    ["stored_credential"] = new JsonObject
                    {
                        ["payment_initiator"] = "MERCHANT",
                        ["payment_type"] = "RECURRING",
                        ["usage"] = "SUBSEQUENT"
                    }
                }
            }
            : new JsonObject { ["paypal"] = new JsonObject { ["vault_id"] = vaultId } };

        var body = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = orderNumber,
                ["custom_id"] = orderNumber,
                ["invoice_id"] = orderNumber,
                ["description"] = Truncate($"{storeOptions.Value.BrandName} subscription renewal {orderNumber}", 127),
                ["amount"] = new JsonObject { ["currency_code"] = "USD", ["value"] = Amount(total) },
                ["shipping"] = ShippingJson(shipping)
            }),
            ["payment_source"] = paymentSource
        };

        JsonNode result;
        try
        {
            result = await client.CreateOrderAsync(body, requestId: $"renewal-{orderNumber}-{attempt}-{vaultType}");
        }
        // 403 is what PayPal returns for a saved payment method that is unknown or no longer usable.
        catch (PayPalException exception) when (exception.StatusCode is 400 or 403 or 404 or 422)
        {
            logger.LogWarning("PayPal declined renewal {OrderNumber}: {Issue} {Message}", orderNumber, exception.Issue, exception.Message);
            return new PaymentResult(false, payPalOptions.Value.ProviderName, null, exception.Issue ?? exception.Message);
        }

        var capture = result["purchase_units"]?[0]?["payments"]?["captures"]?[0];
        var amount = PayPalClient.Text(capture?["amount"]?["value"]);
        if (PayPalClient.Text(result["status"]) != "COMPLETED" || PayPalClient.Text(capture?["status"]) != "COMPLETED")
        {
            var status = $"{PayPalClient.Text(result["status"])}/{PayPalClient.Text(capture?["status"])}";
            logger.LogWarning("Renewal {OrderNumber} was not completed by PayPal: {Status}", orderNumber, status);
            return new PaymentResult(false, payPalOptions.Value.ProviderName, null, $"Payment not completed ({status})");
        }
        if (amount != Amount(total))
        {
            logger.LogError("Renewal {OrderNumber} captured {Amount} instead of {Total}", orderNumber, amount, Amount(total));
            return new PaymentResult(false, payPalOptions.Value.ProviderName, PayPalClient.Text(capture?["id"]),
                $"Captured amount {amount} does not match {Amount(total)}");
        }

        return new PaymentResult(true, payPalOptions.Value.ProviderName, PayPalClient.Text(capture?["id"]), null, vaultId, vaultType);
    }

    private static JsonObject ShippingJson(ShippingDetails shipping)
    {
        var address = new JsonObject
        {
            ["address_line_1"] = shipping.Line1,
            ["admin_area_2"] = shipping.City,
            ["admin_area_1"] = shipping.Region,
            ["postal_code"] = shipping.PostalCode,
            ["country_code"] = "US"
        };
        if (shipping.Line2.Length > 0)
            address["address_line_2"] = shipping.Line2;

        return new JsonObject
        {
            ["name"] = new JsonObject { ["full_name"] = shipping.FullName },
            ["address"] = address
        };
    }

    private JsonObject PayPalSource(PendingOrder order, string returnUrl)
    {
        var source = new JsonObject
        {
            ["experience_context"] = new JsonObject
            {
                ["brand_name"] = Truncate(storeOptions.Value.BrandName, 127),
                ["shipping_preference"] = "SET_PROVIDED_ADDRESS",
                ["user_action"] = "PAY_NOW",
                ["return_url"] = returnUrl,
                ["cancel_url"] = returnUrl
            }
        };

        if (order.HasSubscription)
        {
            source["attributes"] = new JsonObject
            {
                ["vault"] = new JsonObject
                {
                    ["store_in_vault"] = "ON_SUCCESS",
                    ["usage_type"] = "MERCHANT",
                    ["customer_type"] = "CONSUMER"
                }
            };
        }

        return source;
    }

    private static JsonObject CardSource(PendingOrder order)
    {
        var attributes = new JsonObject
        {
            ["verification"] = new JsonObject { ["method"] = "SCA_WHEN_REQUIRED" }
        };
        if (order.HasSubscription)
            attributes["vault"] = new JsonObject { ["store_in_vault"] = "ON_SUCCESS" };

        return new JsonObject { ["attributes"] = attributes };
    }

    private PayPalCaptureResult Declined(string message, bool restart = false) =>
        new(new PaymentResult(false, payPalOptions.Value.ProviderName, null, message), restart);

    public static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
