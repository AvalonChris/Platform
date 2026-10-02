using Microsoft.AspNetCore.Mvc;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Controllers;

[Route("admin/orders")]
public class OrdersController(AdminOrderRepository orders) : AdminController
{
    [HttpGet("/admin")]
    public IActionResult Home() => RedirectToAction(nameof(Index));

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status)
    {
        if (!OrderListViewModel.Statuses.Contains(status))
            status = null;

        return View(new OrderListViewModel(await orders.ListAsync(status), status));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id)
    {
        var order = await orders.GetAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("{id:long}/fulfill")]
    public async Task<IActionResult> Fulfill(long id)
    {
        TempData["Message"] = await orders.MarkFulfilledAsync(id)
            ? "Order marked as fulfilled."
            : "Only paid orders can be marked as fulfilled.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        TempData["Message"] = await orders.CancelAsync(id)
            ? "Order cancelled, along with any subscriptions it started. No refund was issued."
            : "Only pending or paid orders can be cancelled.";
        return RedirectToAction(nameof(Detail), new { id });
    }
}
