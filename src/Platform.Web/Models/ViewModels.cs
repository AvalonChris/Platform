namespace Platform.Web.Models;

public record HomeViewModel(IReadOnlyList<Product> Featured, IReadOnlyList<Product> New);

public record ShopViewModel(IReadOnlyList<Product> Products, string? Kind);

public record ProductViewModel(Product Product, IReadOnlyList<Product> StackComponents);
