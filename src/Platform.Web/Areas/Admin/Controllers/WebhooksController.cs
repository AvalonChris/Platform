using Microsoft.AspNetCore.Mvc;
using Platform.Web.Data;

namespace Platform.Web.Areas.Admin.Controllers;

/// <summary>A log of PayPal webhooks received, for checking the webhook setup and investigating payments.</summary>
[Route("admin/webhooks")]
public class WebhooksController(PayPalWebhookRepository events) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await events.ListRecentAsync());
}
