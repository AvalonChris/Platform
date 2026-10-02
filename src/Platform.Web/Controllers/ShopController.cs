using Microsoft.AspNetCore.Mvc;
using Platform.Web.Data;
using Platform.Web.Models;

namespace Platform.Web.Controllers;

public class ShopController(ProductRepository products) : Controller
{
    [HttpGet("/shop")]
    public async Task<IActionResult> Index(string? kind)
    {
        if (kind is not (Models.Product.KindSingle or Models.Product.KindStack))
            kind = null;

        return View(new ShopViewModel(await products.ListAsync(kind), kind));
    }

    [HttpGet("/products/{slug}")]
    public async Task<IActionResult> Product(string slug)
    {
        var product = await products.GetBySlugAsync(slug);
        if (product is null)
            return NotFound();

        var components = product.IsStack
            ? await products.GetStackComponentsAsync(product.Id)
            : [];

        return View(new ProductViewModel(product, components));
    }
}
