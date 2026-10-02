using Microsoft.AspNetCore.Mvc;
using Platform.Web.Data;
using Platform.Web.Models;

namespace Platform.Web.Controllers;

public class HomeController(ProductRepository products) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index() =>
        View(new HomeViewModel(await products.GetFeaturedAsync(), await products.GetNewAsync()));

    [Route("/error")]
    public IActionResult Error() => View();
}
