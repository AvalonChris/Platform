using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Models;

namespace Platform.Web.Controllers;

public class CartController(CartRepository carts, IOptions<StoreOptions> options) : Controller
{
    [HttpGet("/cart")]
    public async Task<IActionResult> Index()
    {
        var cartId = CartCookie.Read(Request);
        var lines = cartId is null ? [] : await carts.GetLinesAsync(cartId.Value);

        return View(CartViewModel.From(lines, options.Value));
    }

    [HttpPost("/cart/add")]
    public async Task<IActionResult> Add(long productId, string purchaseMode, int? frequencyDays, int quantity = 1)
    {
        if (purchaseMode is not (PurchaseModes.Once or PurchaseModes.Subscription))
            return BadRequest();

        if (purchaseMode == PurchaseModes.Once)
            frequencyDays = null;
        else if (frequencyDays is not (30 or 60 or 90))
            return BadRequest();

        quantity = Math.Clamp(quantity, 1, CartRepository.MaxQuantity);

        var cartId = CartCookie.Read(Request);
        if (cartId is null || !await carts.ExistsAsync(cartId.Value))
        {
            cartId = await carts.CreateAsync();
            CartCookie.Write(HttpContext, cartId.Value);
        }

        if (!await carts.AddItemAsync(cartId.Value, productId, purchaseMode, frequencyDays, quantity))
            return NotFound();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/cart/items/{id:long}")]
    public async Task<IActionResult> Update(long id, int quantity)
    {
        if (CartCookie.Read(Request) is { } cartId)
        {
            if (quantity < 1)
                await carts.RemoveItemAsync(cartId, id);
            else
                await carts.SetQuantityAsync(cartId, id, Math.Min(quantity, CartRepository.MaxQuantity));
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/cart/items/{id:long}/remove")]
    public async Task<IActionResult> Remove(long id)
    {
        if (CartCookie.Read(Request) is { } cartId)
            await carts.RemoveItemAsync(cartId, id);

        return RedirectToAction(nameof(Index));
    }
}
