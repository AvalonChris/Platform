using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Email;
using Platform.Web.Models;
using Platform.Web.Payments;

namespace Platform.Web.Controllers;

/// <summary>
/// Checkout: the customer enters their details, which creates a pending order. With PayPal configured they then
/// pay on a separate page and the order is marked paid once PayPal confirms the payment. Without it, the
/// one-step test gateway (development) or the unconfigured gateway (production) is used.
/// </summary>
public class CheckoutController(
    CartRepository carts,
    OrderRepository orders,
    IPaymentGateway payments,
    PayPalCheckout payPal,
    IOptions<PayPalOptions> payPalOptions,
    IOptions<StoreOptions> options,
    OrderEmails orderEmails,
    ILogger<CheckoutController> logger) : Controller
{
    private bool UsePayPal => payPalOptions.Value.IsConfigured;

    [HttpGet("/checkout")]
    public async Task<IActionResult> Index()
    {
        var cart = await LoadCartAsync();
        if (cart.Items.Count == 0)
            return RedirectToAction("Index", "Cart");

        return View(BuildViewModel(new CheckoutForm(), cart));
    }

    [HttpPost("/checkout")]
    public async Task<IActionResult> Place(CheckoutForm form)
    {
        var cart = await LoadCartAsync();
        if (cart.Items.Count == 0 || CartCookie.Read(Request) is not { } cartId)
            return RedirectToAction("Index", "Cart");

        if (ModelState.IsValid && !UsStates.IsValid(form.Region))
            ModelState.AddModelError("Form.Region", "Choose your state.");
        if (cart.HasSubscription && !form.AcceptRenewal)
            ModelState.AddModelError("Form.AcceptRenewal", "Please agree to the subscription terms to continue.");

        if (!ModelState.IsValid)
            return View("Index", BuildViewModel(form, cart));

        var shipping = options.Value.ShippingFor(cart.Subtotal);
        var order = await orders.CreatePendingAsync(form, cart, shipping);

        if (UsePayPal)
            return RedirectToAction(nameof(Pay), new { token = order.PublicToken });

        var payment = await payments.ChargeAsync(
            new PaymentRequest(order.OrderNumber, order.Email, order.Total, "USD"));

        if (!payment.Approved)
        {
            await orders.CancelAsync(order.Id);
            return View("Index", BuildViewModel(form, cart, payment.Error ?? "Your payment could not be processed."));
        }

        if (await orders.MarkPaidAsync(order, payment, cartId))
            await SendConfirmationAsync(order.PublicToken);
        return RedirectToAction(nameof(Complete), new { token = order.PublicToken });
    }

    [HttpGet("/checkout/pay/{token:guid}")]
    public async Task<IActionResult> Pay(Guid token)
    {
        if (!UsePayPal)
            return RedirectToAction(nameof(Index));

        var pending = await orders.GetPendingAsync(token);
        if (pending is null)
            return NotFound();
        if (!pending.IsPending)
            return pending.Status == "cancelled" ? RedirectToAction("Index", "Cart") : RedirectToAction(nameof(Complete), new { token });

        var order = await orders.GetByTokenAsync(token);
        return View(new PaymentPageViewModel(
            order!, token, payPalOptions.Value.ClientId, AllowAlternativeFunding: !pending.HasSubscription));
    }

    [HttpPost("/checkout/pay/{token:guid}/paypal-order")]
    public async Task<IActionResult> CreatePayPalOrder(Guid token, [FromBody] CreatePayPalOrderRequest request)
    {
        var pending = await orders.GetPendingAsync(token);
        if (pending is null || !pending.IsPending)
            return PaymentError("This order can no longer be paid. Please return to your cart.");

        try
        {
            var returnUrl = Url.Action(nameof(Pay), "Checkout", new { token }, Request.Scheme)!;
            var payPalOrderId = await payPal.CreateOrderAsync(pending, request.Source, returnUrl);
            await orders.SetPaymentSessionAsync(pending.Id, payPalOrderId);
            return Json(new { id = payPalOrderId });
        }
        catch (Exception exception) when (exception is PayPalException or HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Could not create a PayPal order for {OrderNumber}", pending.OrderNumber);
            return PaymentError("PayPal is not available right now. Please try again in a moment.");
        }
    }

    [HttpPost("/checkout/pay/{token:guid}/capture")]
    public async Task<IActionResult> CapturePayPalOrder(Guid token, [FromBody] CapturePayPalOrderRequest request)
    {
        var pending = await orders.GetPendingAsync(token);
        if (pending is null)
            return PaymentError("Order not found.");

        var completeUrl = Url.Action(nameof(Complete), new { token })!;
        if (pending.Status == "paid")
            return Json(new { redirect = completeUrl });
        if (!pending.IsPending)
            return PaymentError("This order can no longer be paid. Please return to your cart.");

        try
        {
            var result = await payPal.CaptureAsync(pending, request.OrderId);
            if (!result.Payment.Approved)
                return PaymentError(result.Payment.Error ?? "Your payment was not completed.", result.Restart);

            // Only the request that actually marks it paid sends the email, so a repeated request doesn't send twice.
            if (await orders.MarkPaidAsync(pending, result.Payment, CartCookie.Read(Request)))
                await SendConfirmationAsync(token);
            return Json(new { redirect = completeUrl });
        }
        catch (Exception exception) when (exception is PayPalException or HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Could not confirm the PayPal payment for {OrderNumber}", pending.OrderNumber);
            return PaymentError(
                "We couldn't confirm your payment with PayPal. Please don't pay again; check your email or contact us.");
        }
    }

    [HttpGet("/orders/{token:guid}")]
    public async Task<IActionResult> Complete(Guid token)
    {
        var order = await orders.GetByTokenAsync(token);
        return order is null ? NotFound() : View(order);
    }

    private async Task SendConfirmationAsync(Guid token)
    {
        if (await orders.GetByTokenAsync(token) is { } order)
            await orderEmails.SendConfirmationAsync(order);
    }

    private JsonResult PaymentError(string message, bool restart = false)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return Json(new { error = message, restart });
    }

    private async Task<CartViewModel> LoadCartAsync()
    {
        var cartId = CartCookie.Read(Request);
        var lines = cartId is null ? [] : await carts.GetLinesAsync(cartId.Value);
        return CartViewModel.From(lines, options.Value);
    }

    private CheckoutViewModel BuildViewModel(CheckoutForm form, CartViewModel cart, string? paymentError = null) =>
        new()
        {
            Form = form,
            Cart = cart,
            Shipping = options.Value.ShippingFor(cart.Subtotal),
            PaymentDescription = UsePayPal
                ? "You'll pay on the next step with PayPal or a credit or debit card."
                : payments.Description,
            PayOnNextStep = UsePayPal,
            PaymentError = paymentError
        };
}

public record CreatePayPalOrderRequest(string Source);

public record CapturePayPalOrderRequest(string OrderId);
