using Dapper;
using Npgsql;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Data;

public class AdminProductRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        """
        id, slug, sku, name, kind, short_spec, description, price, image_url,
        stock_quantity, sort_order, is_active, is_featured, is_new
        """;

    public async Task<IReadOnlyList<ProductForm>> ListAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var products = await connection.QueryAsync<ProductForm>(
            $"select {Columns} from products order by sort_order, name");
        return products.AsList();
    }

    public async Task<ProductForm?> GetAsync(long id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var product = await connection.QuerySingleOrDefaultAsync<ProductForm>(
            $"select {Columns} from products where id = @id", new { id });
        if (product is null)
            return null;

        var componentIds = await connection.QueryAsync<long>(
            "select component_product_id from stack_items where stack_product_id = @id", new { id });
        product.ComponentIds = componentIds.AsList();
        return product;
    }

    public async Task<long> CreateAsync(ProductForm form)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var id = await connection.ExecuteScalarAsync<long>(
            """
            insert into products (slug, sku, name, kind, short_spec, description, price, image_url,
                                  stock_quantity, sort_order, is_active, is_featured, is_new)
            values (@Slug, @Sku, @Name, @Kind, @ShortSpec, @Description, @Price, @ImageUrl,
                    @StockQuantity, @SortOrder, @IsActive, @IsFeatured, @IsNew)
            returning id
            """,
            Parameters(form), transaction);

        await SaveComponentsAsync(connection, transaction, id, form);
        await transaction.CommitAsync();
        return id;
    }

    /// <summary>Updates everything except the kind, which is fixed once a product exists.</summary>
    public async Task UpdateAsync(ProductForm form)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(
            """
            update products
            set slug = @Slug, sku = @Sku, name = @Name, short_spec = @ShortSpec, description = @Description,
                price = @Price, image_url = @ImageUrl, stock_quantity = @StockQuantity, sort_order = @SortOrder,
                is_active = @IsActive, is_featured = @IsFeatured, is_new = @IsNew, updated_at = now()
            where id = @Id
            """,
            Parameters(form), transaction);

        await SaveComponentsAsync(connection, transaction, form.Id, form);
        await transaction.CommitAsync();
    }

    private static object Parameters(ProductForm form) => new
    {
        form.Id,
        form.Slug,
        form.Sku,
        form.Name,
        form.Kind,
        ShortSpec = form.ShortSpec ?? "",
        Description = form.Description ?? "",
        form.Price,
        ImageUrl = string.IsNullOrWhiteSpace(form.ImageUrl) ? null : form.ImageUrl.Trim(),
        form.StockQuantity,
        form.SortOrder,
        form.IsActive,
        form.IsFeatured,
        form.IsNew
    };

    private static async Task SaveComponentsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long productId, ProductForm form)
    {
        if (!form.IsStack)
            return;

        await connection.ExecuteAsync(
            "delete from stack_items where stack_product_id = @productId", new { productId }, transaction);

        await connection.ExecuteAsync(
            """
            insert into stack_items (stack_product_id, component_product_id)
            select @productId, id from products where id = any(@ComponentIds) and kind = 'single'
            """,
            new { productId, form.ComponentIds }, transaction);
    }
}
