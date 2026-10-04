using Microsoft.AspNetCore.Mvc;
using Platform.Web.Payments;

namespace Platform.Web.Controllers;

/// <summary>Endpoints that outside services call. They carry no antiforgery token; each verifies its caller instead.</summary>
[IgnoreAntiforgeryToken]
public class WebhooksController(PayPalWebhooks payPalWebhooks) : Controller
{
    /// <summary>
    /// PayPal retries a webhook until it gets a 2xx response, so a failure returns 500 to have it sent again.
    /// </summary>
    [HttpPost("/webhooks/paypal")]
    public async Task<IActionResult> PayPal()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        return await payPalWebhooks.HandleAsync(body, Request.Headers) switch
        {
            WebhookOutcome.Processed => Ok(),
            WebhookOutcome.Rejected => BadRequest(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
