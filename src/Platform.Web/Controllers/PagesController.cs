using Microsoft.AspNetCore.Mvc;

namespace Platform.Web.Controllers;

/// <summary>Policy and contact pages. Their wording lives in Views/Pages; business details come from the Store settings.</summary>
public class PagesController : Controller
{
    [HttpGet("/shipping-and-refunds")]
    public IActionResult ShippingAndRefunds() => View();

    [HttpGet("/subscription-terms")]
    public IActionResult SubscriptionTerms() => View();

    [HttpGet("/privacy")]
    public IActionResult Privacy() => View();

    [HttpGet("/terms")]
    public IActionResult Terms() => View();

    [HttpGet("/contact")]
    public IActionResult Contact() => View();
}
