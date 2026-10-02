using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Web.Data;
using Platform.Web.Models;
using Platform.Web.Payments;

namespace Platform.Web.Controllers;

public class CheckoutController(
    CartRepository carts,
    OrderRepository orders,
    IPaymentGateway payments,
    IOptions<StoreOptions> options) : Controller
{
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

        var payment = await payments.ChargeAsync(
            new PaymentRequest(order.OrderNumber, form.Email.Trim(), cart.Subtotal + shipping, "USD"));

        if (!payment.Approved)
        {
            await orders.CancelAsync(order.Id);
            return View("Index", BuildViewModel(form, cart, payment.Error ?? "Your payment could not be processed."));
        }

        await orders.MarkPaidAsync(order, payment, cartId);
        return RedirectToAction(nameof(Complete), new { token = order.PublicToken });
    }

    [HttpGet("/orders/{token:guid}")]
    public async Task<IActionResult> Complete(Guid token)
    {
        var order = await orders.GetByTokenAsync(token);
        return order is null ? NotFound() : View(order);
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
            PaymentDescription = payments.Description,
            PaymentError = paymentError
        };
}
