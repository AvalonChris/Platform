using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Areas.Admin.Models;

public class CostSettings
{
    [Range(0, 1000)]
    public decimal FulfillmentFirstUnit { get; set; }

    [Range(0, 1000)]
    public decimal FulfillmentAdditionalUnit { get; set; }

    [Range(0, 100)]
    public decimal SupplierProcessingPercent { get; set; }

    [Range(0, 100)]
    public decimal CardProcessingPercent { get; set; }

    [Range(0, 1000)]
    public decimal CardProcessingFixed { get; set; }

    [Range(0, 1000)]
    public decimal ShippingExtraLbRate { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>A package weighing up to <see cref="MaxWeightLb"/> ships for <see cref="Rate"/>.</summary>
public class ShippingRate
{
    [Range(0.001, 1000)]
    public decimal? MaxWeightLb { get; set; }

    [Range(0, 1000)]
    public decimal? Rate { get; set; }
}

public class CostSettingsViewModel
{
    public CostSettings Settings { get; set; } = new();
    public List<ShippingRate> ShippingRates { get; set; } = [];
}

/// <summary>A product's price and cost inputs, as read for the profit report.</summary>
public class ProfitProduct
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public decimal Price { get; init; }
    public decimal? SupplierCost { get; init; }
    public decimal? ShippingWeightLb { get; init; }
    public bool IsActive { get; init; }
    public DateTime? CostsUpdatedAt { get; init; }

    public bool IsStack => Kind == Platform.Web.Models.Product.KindStack;
}

public class StackItemRow
{
    public long StackProductId { get; init; }
    public long ComponentProductId { get; init; }
    public int Quantity { get; init; }
}

/// <summary>One product or stack sold as a single order, priced one-time and by subscription.</summary>
public class ProfitRow
{
    public long ProductId { get; init; }
    public string Name { get; init; } = "";
    public bool IsStack { get; init; }
    public bool IsActive { get; init; }
    public decimal Price { get; init; }

    /// <summary>Why the row can't be calculated, or null when it can.</summary>
    public string? Missing { get; init; }

    public decimal ProductCost { get; init; }
    public decimal Fulfillment { get; init; }
    public decimal Shipping { get; init; }
    public decimal SupplierFee { get; init; }
    public decimal SupplierTotal => ProductCost + Fulfillment + Shipping + SupplierFee;

    public decimal ShippingCharged { get; init; }
    public decimal CardFee { get; init; }
    public decimal Profit { get; init; }
    public decimal Margin => Price + ShippingCharged == 0 ? 0 : Profit / (Price + ShippingCharged);

    public decimal SubscriptionPrice { get; init; }
    public decimal SubscriptionProfit { get; init; }

    /// <summary>The lowest one-time price that doesn't lose money, assuming the customer pays no shipping.</summary>
    public decimal BreakEven { get; init; }

    /// <summary>For stacks, the oldest of the component dates.</summary>
    public DateTime? CostsUpdatedAt { get; init; }
}

public record ProfitReportViewModel(
    IReadOnlyList<ProfitRow> Rows, CostSettings Settings, int SubscriptionDiscountPercent);
