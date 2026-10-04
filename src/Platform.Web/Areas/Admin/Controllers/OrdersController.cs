using Microsoft.AspNetCore.Mvc;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Areas.Admin.Models;
using Platform.Web.Email;
using Platform.Web.Fulfillment;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin.Controllers;

[Route("admin/orders")]
public class OrdersController(AdminOrderRepository orders, FulfillmentRepository fulfillment, OrderEmails orderEmails) : AdminController
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

    [HttpPost("{id:long}/ship")]
    public async Task<IActionResult> Ship(long id, string? carrier, string? trackingNumber, bool notifyCustomer)
    {
        trackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim();
        carrier = trackingNumber is null || !Tracking.Carriers.Contains(carrier) ? null : carrier;

        if (!await orders.MarkShippedAsync(id, carrier, trackingNumber))
        {
            TempData["Message"] = "Only paid orders can be marked as shipped.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (notifyCustomer && await orders.GetAsync(id) is { } order)
        {
            await orderEmails.SendShippedAsync(order);
            TempData["Message"] = $"Order marked as shipped and {order.Order.Email} was emailed.";
        }
        else
        {
            TempData["Message"] = "Order marked as shipped. No email was sent.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("{id:long}/resend")]
    public async Task<IActionResult> Resend(long id)
    {
        TempData["Message"] = await fulfillment.RequeueAsync(id)
            ? "Queued to be sent for fulfilment again within a minute."
            : "Only paid orders whose fulfilment failed or was skipped can be sent again.";
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
