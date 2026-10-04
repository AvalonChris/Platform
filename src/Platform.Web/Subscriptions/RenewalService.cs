using Microsoft.Extensions.Options;
using Platform.Web.Email;
using Platform.Web.Models;
using Platform.Web.Payments;

namespace Platform.Web.Subscriptions;

public record RenewalRunSummary(int RemindersSent, int Renewed, int Failed, int Paused);

/// <summary>
/// Sends renewal reminders and charges subscriptions that are due. Each renewal creates an order at the price
/// the customer signed up for, charges their saved payment method, and on success moves the subscription on
/// to its next period. Failed charges are retried, then the subscription is paused.
/// </summary>
public class RenewalService(
    RenewalRepository repository,
    PayPalCheckout payPal,
    IOptions<PayPalOptions> payPalOptions,
    IOptions<StoreOptions> storeOptions,
    IEmailSender emailSender,
    IHostEnvironment environment,
    ILogger<RenewalService> logger)
{
    public const int ReminderDaysBefore = 3;
    public const int MaxAttempts = 3;
    public const int RetryAfterDays = 2;

    private StoreOptions Store => storeOptions.Value;
    private string AccountLink => Store.AbsoluteUrl("/account");

    public async Task<RenewalRunSummary> RunAsync()
    {
        var reminders = await SendRemindersAsync();
        int renewed = 0, failed = 0, paused = 0;

        foreach (var subscription in await repository.GetDueAsync())
        {
            try
            {
                switch (await RenewAsync(subscription))
                {
                    case Outcome.Renewed: renewed++; break;
                    case Outcome.Failed: failed++; break;
                    case Outcome.Paused: paused++; break;
                }
            }
            catch (Exception exception)
            {
                // Leave it due: the next run tries again with the same order, so nothing is charged twice.
                logger.LogError(exception, "Renewal of subscription {SubscriptionId} failed unexpectedly", subscription.Id);
                failed++;
            }
        }

        return new RenewalRunSummary(reminders, renewed, failed, paused);
    }

    private enum Outcome { Renewed, Failed, Paused }

    private async Task<Outcome> RenewAsync(DueSubscription subscription)
    {
        var order = await repository.GetOrCreateRenewalOrderAsync(subscription, Store.ShippingFor);

        if (order.Status == "paid")
        {
            await repository.AdvanceAsync(subscription);
            return Outcome.Renewed;
        }

        var payment = await ChargeAsync(subscription, order);
        if (payment.Approved)
        {
            await repository.MarkRenewalPaidAsync(order, subscription, payment.Provider, payment.Reference, payment.PaymentMethodType);
            logger.LogInformation("Renewed subscription {SubscriptionId} as order {OrderNumber}", subscription.Id, order.OrderNumber);
            await SendAsync(EmailTemplates.RenewalReceipt(Store.BrandName, subscription.Email, order.OrderNumber, order.Items,
                order.Total, subscription.NextChargeOn.AddDays(subscription.FrequencyDays), AccountLink));
            return Outcome.Renewed;
        }

        var reason = payment.Error ?? "Payment declined";
        if (subscription.FailedAttempts + 1 >= MaxAttempts)
        {
            await repository.PauseForFailureAsync(subscription.Id, order.Id, reason);
            logger.LogWarning("Paused subscription {SubscriptionId} after {Attempts} failed renewals: {Reason}",
                subscription.Id, MaxAttempts, reason);
            await SendAsync(EmailTemplates.SubscriptionPausedForPayment(Store.BrandName, subscription.Email, AccountLink));
            return Outcome.Paused;
        }

        var retryOn = DateTime.Today.AddDays(RetryAfterDays);
        await repository.RecordFailureAsync(subscription.Id, reason, retryOn);
        logger.LogWarning("Renewal of subscription {SubscriptionId} failed ({Reason}); retrying on {RetryOn:yyyy-MM-dd}",
            subscription.Id, reason, retryOn);
        await SendAsync(EmailTemplates.RenewalFailed(Store.BrandName, subscription.Email, retryOn, AccountLink));
        return Outcome.Failed;
    }

    private async Task<PaymentResult> ChargeAsync(DueSubscription subscription, RenewalOrder order)
    {
        var provider = subscription.PaymentProvider ?? "";

        if (provider is "paypal" or "paypal-sandbox")
        {
            var settings = payPalOptions.Value;
            if (!settings.IsConfigured)
                return Declined(provider, "PayPal is not configured");
            if (provider != settings.ProviderName)
                return Declined(provider, $"Saved with {provider} but the store is using {settings.ProviderName}");
            if (subscription.PaymentMethodReference is null)
                return Declined(provider, "No saved payment method");

            var shipping = new ShippingDetails(order.ShipFullName, order.ShipLine1, order.ShipLine2,
                order.ShipCity, order.ShipRegion, order.ShipPostalCode);
            return await payPal.ChargeSavedMethodAsync(order.OrderNumber, order.Total, shipping,
                subscription.PaymentMethodReference, subscription.PaymentMethodType, subscription.FailedAttempts + 1);
        }

        // Subscriptions started with the development test gateway renew with it too, but never in production.
        if (provider == "test" && environment.IsDevelopment())
            return await new TestPaymentGateway().ChargeAsync(new PaymentRequest(order.OrderNumber, subscription.Email, order.Total, "USD"));

        return Declined(provider, $"Renewals aren't supported for payment provider '{provider}'");
    }

    private static PaymentResult Declined(string provider, string reason) => new(false, provider, null, reason);

    private async Task<int> SendRemindersAsync()
    {
        var sent = 0;
        foreach (var reminder in await repository.GetRemindersDueAsync(ReminderDaysBefore))
        {
            var total = reminder.Subtotal + Store.ShippingFor(reminder.Subtotal);
            await SendAsync(EmailTemplates.RenewalReminder(Store.BrandName, reminder.Email, reminder.NextChargeOn,
                reminder.Items, total, EmailTemplates.PaymentMethodName(reminder.PaymentMethodType), AccountLink));
            await repository.MarkReminderSentAsync(reminder.SubscriptionId, reminder.NextChargeOn);
            sent++;
        }
        return sent;
    }

    /// <summary>A failed email never stops a renewal; it's logged instead.</summary>
    private async Task SendAsync(EmailMessage message)
    {
        try
        {
            await emailSender.SendAsync(message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not send email '{Subject}' to {To}", message.Subject, message.To);
        }
    }
}

/// <summary>Runs renewals every hour, starting a minute after the site starts.</summary>
public class RenewalWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RenewalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Renewals:Enabled", true))
        {
            logger.LogInformation("Subscription renewals are disabled.");
            return;
        }

        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var summary = await scope.ServiceProvider.GetRequiredService<RenewalService>().RunAsync();
                if (summary != new RenewalRunSummary(0, 0, 0, 0))
                    logger.LogInformation("Renewal run: {Summary}", summary);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Renewal run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
