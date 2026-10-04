using System.ComponentModel.DataAnnotations;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin.Models;

public class AdminUser
{
    public long Id { get; init; }
    public string Email { get; init; } = "";
    public string PasswordHash { get; init; } = "";
}

public class LoginForm
{
    [Required(ErrorMessage = "Enter your email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

/// <summary>A product as edited in the admin, including the fields the storefront never shows.</summary>
public class ProductForm
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Enter a URL slug.")]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Use lowercase letters, numbers and hyphens, like vitamin-d3-k2.")]
    [StringLength(100)]
    public string Slug { get; set; } = "";

    [Required(ErrorMessage = "Enter a SKU.")]
    [StringLength(50)]
    public string Sku { get; set; } = "";

    public string Kind { get; set; } = Product.KindSingle;

    [StringLength(200)]
    public string? ShortSpec { get; set; }

    public string? Description { get; set; }

    public string? Ingredients { get; set; }
    public string? SuggestedUse { get; set; }
    public string? Warnings { get; set; }

    [Range(0, 100000, ErrorMessage = "Enter a price of 0 or more.")]
    public decimal Price { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stock can't be negative.")]
    public int StockQuantity { get; set; }

    public int SortOrder { get; set; }

    /// <summary>What the supplier charges us per unit. Single products only.</summary>
    [Range(0, 100000, ErrorMessage = "Enter a cost of 0 or more.")]
    public decimal? SupplierCost { get; set; }

    /// <summary>Gross shipping weight of one unit, in pounds. Single products only.</summary>
    [Range(0, 1000, ErrorMessage = "Enter a weight of 0 or more.")]
    public decimal? ShippingWeightLb { get; set; }

    /// <summary>When the supplier cost or weight last changed. Display only.</summary>
    public DateTime? CostsUpdatedAt { get; set; }

    /// <summary>The Shopify variant Supliful ships for this product: a numeric ID or a gid://shopify/ProductVariant/ ID.</summary>
    [RegularExpression(@"^(\d+|gid://shopify/ProductVariant/\d+)$", ErrorMessage = "Enter the Shopify variant ID, a number like 44012345678901.")]
    public string? FulfillmentVariantId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public bool IsNew { get; set; }

    public const int MaxComponentQuantity = 12;

    /// <summary>For stacks: how many of each single product it contains, keyed by product id. 0 means not included.</summary>
    public Dictionary<long, int> ComponentQuantities { get; set; } = [];

    public int QuantityOf(long productId) => ComponentQuantities.GetValueOrDefault(productId);

    public bool IsStack => Kind == Product.KindStack;
}

public record ProductEditViewModel(ProductForm Form, IReadOnlyList<ProductForm> Singles)
{
    public bool IsNew => Form.Id == 0;
}

public record OrderListViewModel(IReadOnlyList<Order> Orders, string? Status)
{
    public static readonly string[] Statuses = ["pending", "paid", "fulfilled", "cancelled", "refunded"];
}

public class SubscriptionRow
{
    public long Id { get; init; }
    public string Email { get; init; } = "";
    public string Status { get; init; } = "";
    public int FrequencyDays { get; init; }
    public DateTime? NextChargeOn { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Items { get; init; } = "";
    public decimal RenewalTotal { get; init; }
    public string? PaymentProvider { get; init; }
    public string? PaymentMethodType { get; init; }
    public bool HasSavedMethod { get; init; }
    public int FailedAttempts { get; init; }
    public string? LastFailure { get; init; }
}

public static class AdminFormat
{
    public static string DateTime(DateTime? value) =>
        value is null ? "" : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";

    public static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "";
}
