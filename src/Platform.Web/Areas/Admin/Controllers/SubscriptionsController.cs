using Microsoft.AspNetCore.Mvc;
using Platform.Web.Areas.Admin.Data;

namespace Platform.Web.Areas.Admin.Controllers;

[Route("admin/subscriptions")]
public class SubscriptionsController(AdminOrderRepository orders) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await orders.ListSubscriptionsAsync());
}
