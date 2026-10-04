using Microsoft.Extensions.Options;

namespace Platform.Web.Fulfillment;

/// <summary>Every minute, sends newly paid orders to Shopify and retries failed ones.</summary>
public class FulfillmentWorker(
    FulfillmentRepository repository,
    ShopifyClient shopify,
    IOptions<FulfillmentOptions> options,
    ILogger<FulfillmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Fulfilment relay is disabled; paid orders stay queued.");
            return;
        }
        if (!settings.IsConfigured)
        {
            logger.LogError("Fulfilment relay is enabled but ShopDomain or AccessToken is missing; nothing will be sent.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await SendDueOrdersAsync(settings);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Fulfilment run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueOrdersAsync(FulfillmentOptions settings)
    {
        foreach (var order in await repository.GetDueAsync(settings.MaxAttempts))
        {
            // Test-gateway and PayPal sandbox payments involve no real money.
            var isTestPayment = order.PaymentProvider == "test" || order.PaymentProvider?.EndsWith("-sandbox") == true;
            if (isTestPayment && !settings.SendTestOrders)
            {
                await repository.MarkSkippedAsync(order.Id, "Paid with test money, so it was not sent for fulfilment.");
                continue;
            }

            try
            {
                var lines = await repository.GetLinesAsync(order.Id);
                var unmapped = lines.Where(line => string.IsNullOrWhiteSpace(line.VariantId)).Select(line => line.Name).ToList();
                if (unmapped.Count > 0)
                    throw new FulfillmentException("No fulfilment variant ID set for: " + string.Join(", ", unmapped));

                var reference = await shopify.FindOrderAsync(order.OrderNumber)
                    ?? await shopify.CreateOrderAsync(order, lines);

                await repository.MarkSentAsync(order.Id, reference);
                logger.LogInformation("Sent order {OrderNumber} for fulfilment as {Reference}", order.OrderNumber, reference);
            }
            catch (Exception exception) when (exception is FulfillmentException or HttpRequestException or TaskCanceledException)
            {
                await repository.MarkFailedAsync(order.Id, exception.Message);
                logger.LogWarning("Could not send order {OrderNumber} for fulfilment: {Error}", order.OrderNumber, exception.Message);
            }
        }
    }
}
