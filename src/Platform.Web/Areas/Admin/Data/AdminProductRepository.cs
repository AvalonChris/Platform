using Dapper;
using Npgsql;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Data;

public class AdminProductRepository(NpgsqlDataSource dataSource)
{
    private const string Columns =
        """
        id, slug, sku, name, kind, short_spec, description, price, image_url,
        stock_quantity, sort_order, is_active, is_featured, is_new, fulfillment_variant_id,
        ingredients, suggested_use, warnings, supplier_cost, shipping_weight_lb, costs_updated_at
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

        var components = await connection.QueryAsync<(long ProductId, int Quantity)>(
            "select component_product_id, quantity from stack_items where stack_product_id = @id", new { id });
        product.ComponentQuantities = components.ToDictionary(c => c.ProductId, c => c.Quantity);
        return product;
    }

    public async Task<long> CreateAsync(ProductForm form)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var id = await connection.ExecuteScalarAsync<long>(
            """
            insert into products (slug, sku, name, kind, short_spec, description, price, image_url,
                                  stock_quantity, sort_order, is_active, is_featured, is_new, fulfillment_variant_id,
                                  ingredients, suggested_use, warnings, supplier_cost, shipping_weight_lb,
                                  costs_updated_at)
            values (@Slug, @Sku, @Name, @Kind, @ShortSpec, @Description, @Price, @ImageUrl,
                    @StockQuantity, @SortOrder, @IsActive, @IsFeatured, @IsNew, @FulfillmentVariantId,
                    @Ingredients, @SuggestedUse, @Warnings, @SupplierCost, @ShippingWeightLb,
                    case when @SupplierCost is not null or @ShippingWeightLb is not null then now() end)
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
                is_active = @IsActive, is_featured = @IsFeatured, is_new = @IsNew,
                fulfillment_variant_id = @FulfillmentVariantId, ingredients = @Ingredients,
                suggested_use = @SuggestedUse, warnings = @Warnings,
                costs_updated_at = case
                    when supplier_cost is distinct from @SupplierCost
                      or shipping_weight_lb is distinct from @ShippingWeightLb then now()
                    else costs_updated_at
                end,
                supplier_cost = @SupplierCost, shipping_weight_lb = @ShippingWeightLb, updated_at = now()
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
        Ingredients = form.Ingredients?.Trim() ?? "",
        SuggestedUse = form.SuggestedUse?.Trim() ?? "",
        Warnings = form.Warnings?.Trim() ?? "",
        // Stacks take their costs from their contents.
        SupplierCost = form.IsStack ? null : form.SupplierCost,
        ShippingWeightLb = form.IsStack ? null : form.ShippingWeightLb,
        form.Price,
        ImageUrl = string.IsNullOrWhiteSpace(form.ImageUrl) ? null : form.ImageUrl.Trim(),
        form.StockQuantity,
        form.SortOrder,
        form.IsActive,
        form.IsFeatured,
        form.IsNew,
        // Stacks ship as their components, so they never carry a variant of their own.
        FulfillmentVariantId = form.IsStack || string.IsNullOrWhiteSpace(form.FulfillmentVariantId)
            ? null
            : form.FulfillmentVariantId.Trim().StartsWith("gid://")
                ? form.FulfillmentVariantId.Trim()
                : $"gid://shopify/ProductVariant/{form.FulfillmentVariantId.Trim()}"
    };

    private static async Task SaveComponentsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long productId, ProductForm form)
    {
        if (!form.IsStack)
            return;

        await connection.ExecuteAsync(
            "delete from stack_items where stack_product_id = @productId", new { productId }, transaction);

        var included = form.ComponentQuantities.Where(c => c.Value > 0).ToList();

        await connection.ExecuteAsync(
            """
            insert into stack_items (stack_product_id, component_product_id, quantity)
            select @productId, p.id, c.quantity
            from unnest(@ids, @quantities) as c (id, quantity)
            join products p on p.id = c.id and p.kind = 'single'
            """,
            new
            {
                productId,
                ids = included.Select(c => c.Key).ToArray(),
                quantities = included.Select(c => c.Value).ToArray()
            },
            transaction);
    }
}
