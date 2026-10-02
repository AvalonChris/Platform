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
    public decimal Price { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsNew { get; init; }

    public bool IsStack => Kind == KindStack;
}
