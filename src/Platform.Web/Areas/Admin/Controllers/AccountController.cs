using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Controllers;

public class AccountController(AdminUserRepository users) : AdminController
{
    private static readonly PasswordHasher<string> Hasher = new();

    // Verified against when the email is unknown, so both cases take about the same time.
    private static readonly string DummyHash = Hasher.HashPassword("", "not-a-real-password");

    [AllowAnonymous]
    [HttpGet("/admin/login")]
    public IActionResult Login(string? returnUrl) => View(new LoginForm { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpPost("/admin/login")]
    [EnableRateLimiting(AdminAuth.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid)
            return View(form);

        var user = await users.GetByEmailAsync(form.Email.Trim());
        var result = Hasher.VerifyHashedPassword(form.Email, user?.PasswordHash ?? DummyHash, form.Password);

        if (user is null || result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "The email or password is incorrect.");
            return View(form);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Email)],
            AdminAuth.Scheme);
        await HttpContext.SignInAsync(AdminAuth.Scheme, new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(form.ReturnUrl) ? LocalRedirect(form.ReturnUrl!) : Redirect("/admin");
    }

    [HttpPost("/admin/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminAuth.Scheme);
        return Redirect("/admin/login");
    }
}
