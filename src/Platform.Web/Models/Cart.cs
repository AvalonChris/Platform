namespace Platform.Web.Models;

public static class PurchaseModes
{
    public const string Once = "once";
    public const string Subscription = "subscription";
}

public class CartLine
{
    public long Id { get; init; }
    public long ProductId { get; init; }
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ImageUrl { get; init; }
    public decimal Price { get; init; }
    public string PurchaseMode { get; init; } = PurchaseModes.Once;
    public int? FrequencyDays { get; init; }
    public int Quantity { get; init; }

    public bool IsSubscription => PurchaseMode == PurchaseModes.Subscription;
}

public record CartItemViewModel(CartLine Line, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Line.Quantity;

    public string PlanText => Line.IsSubscription
        ? $"Subscription · every {Line.FrequencyDays} days"
        : "One-time purchase";
}

public record CartViewModel(IReadOnlyList<CartItemViewModel> Items)
{
    public decimal Subtotal => Items.Sum(item => item.LineTotal);

    public bool HasSubscription => Items.Any(item => item.Line.IsSubscription);

    /// <summary>What the subscription items cost at each renewal.</summary>
    public decimal RenewalTotal => Items.Where(item => item.Line.IsSubscription).Sum(item => item.LineTotal);

    /// <summary>Prices the lines at today's product prices and subscription discount.</summary>
    public static CartViewModel From(IEnumerable<CartLine> lines, StoreOptions options) =>
        new(lines
            .Select(line => new CartItemViewModel(
                line,
                line.IsSubscription ? options.SubscribedPrice(line.Price) : line.Price))
            .ToList());
}
