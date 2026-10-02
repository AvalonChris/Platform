namespace Platform.Web;

/// <summary>The visitor's cart is identified by a cookie holding the cart's id.</summary>
public static class CartCookie
{
    private const string Name = "cart_id";

    public static Guid? Read(HttpRequest request) =>
        Guid.TryParse(request.Cookies[Name], out var cartId) ? cartId : null;

    public static void Write(HttpContext context, Guid cartId) =>
        context.Response.Cookies.Append(Name, cartId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
}
