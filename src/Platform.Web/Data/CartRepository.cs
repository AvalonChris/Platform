using Dapper;
using Npgsql;
using Platform.Web.Models;

namespace Platform.Web.Data;

public class CartRepository(NpgsqlDataSource dataSource)
{
    public const int MaxQuantity = 99;

    public async Task<bool> ExistsAsync(Guid cartId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from carts where id = @cartId)", new { cartId });
    }

    public async Task<Guid> CreateAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<Guid>("insert into carts default values returning id");
    }

    public async Task<IReadOnlyList<CartLine>> GetLinesAsync(Guid cartId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var lines = await connection.QueryAsync<CartLine>(
            """
            select ci.id, ci.product_id, p.slug, p.name, p.image_url, p.price,
                   ci.purchase_mode, ci.frequency_days, ci.quantity
            from cart_items ci
            join products p on p.id = ci.product_id
            where ci.cart_id = @cartId
            order by ci.id
            """,
            new { cartId });
        return lines.AsList();
    }

    public async Task<int> CountItemsAsync(Guid cartId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "select coalesce(sum(quantity), 0)::int from cart_items where cart_id = @cartId", new { cartId });
    }

    /// <summary>Adds to the matching line if one exists. Returns false if the product is missing or inactive.</summary>
    public async Task<bool> AddItemAsync(Guid cartId, long productId, string purchaseMode, int? frequencyDays, int quantity)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            insert into cart_items (cart_id, product_id, purchase_mode, frequency_days, quantity)
            select @cartId, p.id, @purchaseMode, @frequencyDays, @quantity
            from products p
            where p.id = @productId and p.is_active
            on conflict (cart_id, product_id, purchase_mode, frequency_days)
            do update set quantity = least(cart_items.quantity + excluded.quantity, @MaxQuantity)
            """,
            new { cartId, productId, purchaseMode, frequencyDays, quantity, MaxQuantity });

        if (rows > 0)
            await connection.ExecuteAsync("update carts set updated_at = now() where id = @cartId", new { cartId });

        return rows > 0;
    }

    public async Task SetQuantityAsync(Guid cartId, long itemId, int quantity)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "update cart_items set quantity = @quantity where id = @itemId and cart_id = @cartId",
            new { cartId, itemId, quantity });
    }

    public async Task RemoveItemAsync(Guid cartId, long itemId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "delete from cart_items where id = @itemId and cart_id = @cartId", new { cartId, itemId });
    }
}
