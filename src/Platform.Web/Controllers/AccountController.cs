using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Email;
using Platform.Web.Models;

namespace Platform.Web.Controllers;

public static class CustomerAuth
{
    /// <summary>Cookie scheme for customers, separate from the admin scheme.</summary>
    public const string Scheme = "Customer";

    public const string SignInRateLimitPolicy = "customer-sign-in";

    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(30);
}

public class SignInForm
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";
}

public record AccountViewModel(
    string Email, IReadOnlyList<AccountSubscription> Subscriptions, IReadOnlyList<AccountOrder> Orders, StoreOptions Store);

/// <summary>
/// Customer accounts. There are no passwords: customers sign in with a one-time link emailed to the address
/// they used at checkout, then manage their subscriptions and see their orders.
/// </summary>
[Route("account")]
public class AccountController(
    CustomerAccountRepository accounts,
    IEmailSender emailSender,
    IOptions<StoreOptions> storeOptions,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet("sign-in")]
    public IActionResult SignInForm() => View("SignIn", new SignInForm());

    [HttpPost("sign-in")]
    [EnableRateLimiting(CustomerAuth.SignInRateLimitPolicy)]
    public async Task<IActionResult> SendSignInLink(SignInForm form)
    {
        if (!ModelState.IsValid)
            return View("SignIn", form);

        // The response is the same whether or not the email is known, so it can't be used to look up customers.
        if (await accounts.FindCustomerIdAsync(form.Email) is { } customerId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            await accounts.CreateLoginTokenAsync(customerId, Hash(token), DateTime.UtcNow.Add(CustomerAuth.LinkLifetime));

            var store = storeOptions.Value;
            var link = store.AbsoluteUrl(Url.Action(nameof(ConfirmSignIn), new { token })!);
            try
            {
                await emailSender.SendAsync(EmailTemplates.SignInLink(store.BrandName, form.Email.Trim(), link));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not send a sign-in link to customer {CustomerId}", customerId);
            }
        }

        return View("SignInSent", form.Email.Trim());
    }

    /// <summary>
    /// The link from the email shows a button instead of signing in straight away, because email scanners open
    /// links automatically and would use up the one-time token.
    /// </summary>
    [HttpGet("sign-in/{token}")]
    public IActionResult ConfirmSignIn(string token) => View("ConfirmSignIn", token);

    [HttpPost("sign-in/{token}")]
    public async Task<IActionResult> CompleteSignIn(string token)
    {
        if (await accounts.ConsumeLoginTokenAsync(Hash(token)) is not { } customerId)
            return View("SignInExpired");

        var email = await accounts.GetEmailAsync(customerId) ?? "";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, customerId.ToString()), new Claim(ClaimTypes.Email, email)],
            CustomerAuth.Scheme);
        await HttpContext.SignInAsync(CustomerAuth.Scheme, new ClaimsPrincipal(identity));

        return RedirectToAction(nameof(Index));
    }

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var customerId = CustomerId;
        return View(new AccountViewModel(
            User.FindFirstValue(ClaimTypes.Email) ?? "",
            await accounts.GetSubscriptionsAsync(customerId),
            await accounts.GetOrdersAsync(customerId),
            storeOptions.Value));
    }

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("sign-out")]
    public async Task<IActionResult> LogOut()
    {
        await HttpContext.SignOutAsync(CustomerAuth.Scheme);
        return RedirectToAction("Index", "Home");
    }

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("subscriptions/{id:long}/skip")]
    public async Task<IActionResult> Skip(long id) =>
        Done(await accounts.SkipAsync(CustomerId, id), "Your next delivery was skipped.");

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("subscriptions/{id:long}/pause")]
    public async Task<IActionResult> Pause(long id) =>
        Done(await accounts.PauseAsync(CustomerId, id), "Your subscription is paused. Nothing will be charged until you resume it.");

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("subscriptions/{id:long}/resume")]
    public async Task<IActionResult> Resume(long id) =>
        Done(await accounts.ResumeAsync(CustomerId, id), "Your subscription is active again.");

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("subscriptions/{id:long}/frequency")]
    public async Task<IActionResult> ChangeFrequency(long id, int frequencyDays) =>
        Done(await accounts.ChangeFrequencyAsync(CustomerId, id, frequencyDays), $"Deliveries will now come every {frequencyDays} days.");

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpGet("subscriptions/{id:long}/cancel")]
    public async Task<IActionResult> ConfirmCancel(long id)
    {
        var subscription = (await accounts.GetSubscriptionsAsync(CustomerId))
            .FirstOrDefault(s => s.Id == id && s.Status != "cancelled");
        return subscription is null ? RedirectToAction(nameof(Index)) : View(subscription);
    }

    [Authorize(AuthenticationSchemes = CustomerAuth.Scheme)]
    [HttpPost("subscriptions/{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id) =>
        Done(await accounts.CancelAsync(CustomerId, id), "Your subscription is cancelled. You won't be charged again.");

    private long CustomerId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult Done(bool changed, string message)
    {
        TempData["AccountMessage"] = changed ? message : "That change couldn't be made. Please refresh and try again.";
        return RedirectToAction(nameof(Index));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
