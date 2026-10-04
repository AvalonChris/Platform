using Dapper;
using Npgsql;
using Platform.Web.Models;

namespace Platform.Web.Data;

public class ProductRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        "id, slug, sku, name, kind, short_spec, description, price, image_url, is_featured, is_new";

    public async Task<IReadOnlyList<Product>> GetFeaturedAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var products = await connection.QueryAsync<Product>(
            $"select {Columns} from products where is_active and is_featured order by sort_order, name");
        return products.AsList();
    }

    public async Task<IReadOnlyList<Product>> GetNewAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var products = await connection.QueryAsync<Product>(
            $"select {Columns} from products where is_active and is_new order by sort_order, name");
        return products.AsList();
    }

    /// <param name="kind">"single", "stack", or null for everything.</param>
    public async Task<IReadOnlyList<Product>> ListAsync(string? kind)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var products = await connection.QueryAsync<Product>(
            $"""
            select {Columns} from products
            where is_active and (@kind::text is null or kind = @kind)
            order by sort_order, name
            """,
            new { kind });
        return products.AsList();
    }

    public async Task<Product?> GetBySlugAsync(string slug)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Product>(
            $"select {Columns}, ingredients, suggested_use, warnings from products where is_active and slug = @slug",
            new { slug });
    }

    public async Task<IReadOnlyList<Product>> GetStackComponentsAsync(long stackProductId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var products = await connection.QueryAsync<Product>(
            """
            select p.id, p.slug, p.sku, p.name, p.kind, p.short_spec, p.description, p.price,
                   p.image_url, p.is_featured, p.is_new, si.quantity as stack_quantity
            from stack_items si
            join products p on p.id = si.component_product_id
            where si.stack_product_id = @stackProductId
            order by p.sort_order, p.name
            """,
            new { stackProductId });
        return products.AsList();
    }
}
