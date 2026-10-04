using Microsoft.Extensions.Options;
using Platform.Web.Models;

namespace Platform.Web.Email;

/// <summary>Order confirmation and shipping emails. A failed send is logged and never stops the order from going through.</summary>
public class OrderEmails(IEmailSender sender, IOptions<StoreOptions> storeOptions, ILogger<OrderEmails> logger)
{
    private StoreOptions Store => storeOptions.Value;

    public Task SendConfirmationAsync(OrderViewModel order) => SendAsync(
        EmailTemplates.OrderConfirmation(Store.BrandName, order, OrderLink(order), Store.AbsoluteUrl("/account")),
        order.Order.OrderNumber);

    public Task SendShippedAsync(OrderViewModel order) => SendAsync(
        EmailTemplates.OrderShipped(Store.BrandName, order, OrderLink(order)),
        order.Order.OrderNumber);

    private string OrderLink(OrderViewModel order) => Store.AbsoluteUrl($"/orders/{order.Order.PublicToken}");

    private async Task SendAsync(EmailMessage message, string orderNumber)
    {
        try
        {
            await sender.SendAsync(message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not send '{Subject}' for order {OrderNumber}", message.Subject, orderNumber);
        }
    }
}
