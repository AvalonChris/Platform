using Microsoft.AspNetCore.Mvc;
using Platform.Web.Data;

namespace Platform.Web.ViewComponents;

public class CartLinkViewComponent(CartRepository carts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var cartId = CartCookie.Read(Request);
        var count = cartId is null ? 0 : await carts.CountItemsAsync(cartId.Value);
        return View(count);
    }
}
