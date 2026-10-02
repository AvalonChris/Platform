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

    [Range(0, 100000, ErrorMessage = "Enter a price of 0 or more.")]
    public decimal Price { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stock can't be negative.")]
    public int StockQuantity { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public bool IsNew { get; set; }

    /// <summary>For stacks: the single products it contains.</summary>
    public List<long> ComponentIds { get; set; } = [];

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
}

public static class AdminFormat
{
    public static string DateTime(DateTime? value) =>
        value is null ? "" : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";

    public static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "";
}
