using Platform.Web.Models;

namespace Platform.Web.Email;

/// <summary>The plain-text emails the store sends.</summary>
public static class EmailTemplates
{
    public static EmailMessage SignInLink(string brand, string to, string link) => new(
        to,
        $"Your {brand} sign-in link",
        $"""
        Use this link to sign in to your {brand} account:

        {link}

        It works once and expires in 30 minutes. If you didn't ask to sign in, you can ignore this email.
        """);

    public static EmailMessage RenewalReminder(
        string brand, string to, DateTime renewalDate, string items, decimal total, string paymentMethod, string accountLink) => new(
        to,
        $"Your {brand} subscription renews on {renewalDate:MMMM d}",
        $"""
        Your subscription renews on {renewalDate:dddd, MMMM d}.

        Items: {items}
        Total: {Money.Format(total)}, charged to your saved {paymentMethod}.

        To skip this delivery, change how often it arrives, pause or cancel, visit your account:
        {accountLink}
        """);

    public static EmailMessage RenewalReceipt(
        string brand, string to, string orderNumber, string items, decimal total, DateTime nextRenewal, string accountLink) => new(
        to,
        $"{brand} order {orderNumber}: your subscription renewed",
        $"""
        Thanks! Your subscription renewed and order {orderNumber} is on its way to being packed.

        Items: {items}
        Total charged: {Money.Format(total)}

        Your next renewal is on {nextRenewal:MMMM d}. Manage your subscription:
        {accountLink}
        """);

    public static EmailMessage RenewalFailed(string brand, string to, DateTime retryOn, string accountLink) => new(
        to,
        $"We couldn't renew your {brand} subscription",
        $"""
        We tried to charge your saved payment method for your subscription renewal, but the payment didn't go through.

        We'll try again on {retryOn:MMMM d}. If your payment details have changed, or you'd like to skip or cancel, visit your account:
        {accountLink}
        """);

    public static EmailMessage SubscriptionPausedForPayment(string brand, string to, string accountLink) => new(
        to,
        $"Your {brand} subscription is paused",
        $"""
        We weren't able to charge your saved payment method after several tries, so we've paused your subscription. Nothing more will be charged.

        You can resume it from your account at any time:
        {accountLink}
        """);

    public static EmailMessage OrderConfirmation(string brand, OrderViewModel model, string orderLink, string accountLink)
    {
        var order = model.Order;
        var hasSubscription = model.Items.Any(item => item.PurchaseMode == PurchaseModes.Subscription);
        var subscriptionNote = hasSubscription
            ? $"""

              Subscription items renew automatically at the same price on the schedule shown above, and we'll email you a reminder before each renewal. You can skip, pause or cancel any time from your account:
              {accountLink}

              """
            : "\n";

        return new EmailMessage(
            order.Email,
            $"{brand} order {order.OrderNumber} confirmed",
            $"""
            Thanks for your order! We've received your payment and we're getting it ready.

            Order {order.OrderNumber}

            {ItemLines(model.Items)}

            Subtotal: {Money.Format(order.Subtotal)}
            Shipping: {(order.ShippingTotal == 0 ? "Free" : Money.Format(order.ShippingTotal))}
            Total: {Money.Format(order.Total)}

            Shipping to:
            {AddressLines(order)}
            {subscriptionNote}
            We'll email you again when it ships. View your order:
            {orderLink}
            """);
    }

    public static EmailMessage OrderShipped(string brand, OrderViewModel model, string orderLink)
    {
        var order = model.Order;
        var tracking = order.TrackingNumber is null
            ? ""
            : $"""
              Tracking: {order.TrackingCarrier} {order.TrackingNumber}
              {order.TrackingUrl}


              """;

        return new EmailMessage(
            order.Email,
            $"{brand} order {order.OrderNumber} has shipped",
            $"""
            Good news: your order {order.OrderNumber} is on its way.

            {tracking}{ItemLines(model.Items)}

            Shipping to:
            {AddressLines(order)}

            View your order:
            {orderLink}
            """);
    }

    public static string PaymentMethodName(string? type) => type == "card" ? "card" : "PayPal account";

    private static string ItemLines(IEnumerable<OrderItem> items) => string.Join("\n", items.Select(item =>
        $"{item.Quantity} × {item.ProductName} ({item.PlanText}): {Money.Format(item.LineTotal)}"));

    private static string AddressLines(Order order) => string.Join("\n", new[]
    {
        order.ShipFullName,
        order.ShipLine1,
        order.ShipLine2,
        $"{order.ShipCity}, {order.ShipRegion} {order.ShipPostalCode}"
    }.Where(line => !string.IsNullOrWhiteSpace(line)));
}
