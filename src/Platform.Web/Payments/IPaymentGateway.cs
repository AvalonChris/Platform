namespace Platform.Web.Payments;

public record PaymentRequest(string OrderNumber, string Email, decimal Amount, string Currency);

public record PaymentResult(bool Approved, string Provider, string? Reference, string? Error);

public interface IPaymentGateway
{
    /// <summary>Shown in the payment section of the checkout page.</summary>
    string Description { get; }

    Task<PaymentResult> ChargeAsync(PaymentRequest request);
}

/// <summary>Development only: approves every payment without collecting or charging a card.</summary>
public class TestPaymentGateway : IPaymentGateway
{
    public string Description => "Test mode. No card is collected or charged.";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request) =>
        Task.FromResult(new PaymentResult(true, "test", $"test-{Guid.NewGuid():N}", null));
}

/// <summary>Used outside development until a real processor is integrated: declines every payment.</summary>
public class UnconfiguredPaymentGateway : IPaymentGateway
{
    public string Description => "Online payment is not available yet.";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request) =>
        Task.FromResult(new PaymentResult(false, "none", null, "Online payment is not available yet."));
}
