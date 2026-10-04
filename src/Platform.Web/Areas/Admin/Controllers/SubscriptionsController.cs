using Microsoft.AspNetCore.Mvc;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Subscriptions;

namespace Platform.Web.Areas.Admin.Controllers;

[Route("admin/subscriptions")]
public class SubscriptionsController(AdminOrderRepository orders, RenewalService renewals) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await orders.ListSubscriptionsAsync());

    /// <summary>Runs the hourly renewal job now: sends due reminders and charges due subscriptions.</summary>
    [HttpPost("run-renewals")]
    public async Task<IActionResult> RunRenewals()
    {
        var summary = await renewals.RunAsync();
        TempData["Message"] =
            $"Renewals run: {summary.Renewed} renewed, {summary.Failed} failed (will retry), {summary.Paused} paused, {summary.RemindersSent} reminders sent.";
        return RedirectToAction(nameof(Index));
    }
}
