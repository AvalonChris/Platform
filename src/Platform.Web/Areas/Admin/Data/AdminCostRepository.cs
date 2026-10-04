using Dapper;
using Npgsql;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Data;

public class AdminCostRepository(NpgsqlDataSource dataSource)
{
    public async Task<CostSettings> GetSettingsAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<CostSettings>(
            """
            select fulfillment_first_unit, fulfillment_additional_unit, supplier_processing_percent,
                   card_processing_percent, card_processing_fixed, shipping_extra_lb_rate, updated_at
            from cost_settings
            where id = 1
            """);
    }

    public async Task<IReadOnlyList<ShippingRate>> GetShippingRatesAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var rates = await connection.QueryAsync<ShippingRate>(
            "select max_weight_lb, rate from shipping_rates order by max_weight_lb");
        return rates.AsList();
    }

    /// <summary>Saves the settings and replaces the whole shipping table.</summary>
    public async Task SaveAsync(CostSettings settings, IReadOnlyList<ShippingRate> rates)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(
            """
            update cost_settings
            set fulfillment_first_unit = @FulfillmentFirstUnit,
                fulfillment_additional_unit = @FulfillmentAdditionalUnit,
                supplier_processing_percent = @SupplierProcessingPercent,
                card_processing_percent = @CardProcessingPercent,
                card_processing_fixed = @CardProcessingFixed,
                shipping_extra_lb_rate = @ShippingExtraLbRate,
                updated_at = now()
            where id = 1
            """,
            settings, transaction);

        await connection.ExecuteAsync("delete from shipping_rates", transaction: transaction);
        await connection.ExecuteAsync(
            "insert into shipping_rates (max_weight_lb, rate) values (@MaxWeightLb, @Rate)",
            rates, transaction);

        await transaction.CommitAsync();
    }

    public async Task<(IReadOnlyList<ProfitProduct> Products, IReadOnlyList<StackItemRow> StackItems)> GetProfitInputsAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var products = await connection.QueryAsync<ProfitProduct>(
            """
            select id, name, kind, price, supplier_cost, shipping_weight_lb, is_active, costs_updated_at
            from products
            order by is_active desc, sort_order, name
            """);
        var stackItems = await connection.QueryAsync<StackItemRow>(
            "select stack_product_id, component_product_id, quantity from stack_items");

        return (products.AsList(), stackItems.AsList());
    }
}
