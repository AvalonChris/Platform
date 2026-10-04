namespace Platform.Web.Models;

public class Product
{
    public const string KindSingle = "single";
    public const string KindStack = "stack";

    public long Id { get; init; }
    public string Slug { get; init; } = "";
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = KindSingle;
    public string ShortSpec { get; init; } = "";
    public string Description { get; init; } = "";
    public string Ingredients { get; init; } = "";
    public string SuggestedUse { get; init; } = "";
    public string Warnings { get; init; } = "";
    public decimal Price { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsNew { get; init; }

    /// <summary>Only set when loaded as part of a stack: how many of this product the stack contains.</summary>
    public int StackQuantity { get; init; } = 1;

    public bool IsStack => Kind == KindStack;
}
