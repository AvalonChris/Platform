using Platform.Web.Areas.Admin.Models;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin;

/// <summary>
/// Works out what one order of a product or stack costs us and earns, from the supplier cost, fulfilment
/// fees, shipping by weight, the supplier's processing fee and our own card fees.
/// </summary>
public static class ProfitCalculator
{
    public static IReadOnlyList<ProfitRow> Calculate(
        IReadOnlyList<ProfitProduct> products,
        IReadOnlyList<StackItemRow> stackItems,
        CostSettings settings,
        IReadOnlyList<ShippingRate> shippingRates,
        StoreOptions store)
    {
        var byId = products.ToDictionary(product => product.Id);

        return products
            .Select(product =>
            {
                var lines = product.IsStack
                    ? stackItems
                        .Where(item => item.StackProductId == product.Id && byId.ContainsKey(item.ComponentProductId))
                        .Select(item => (Product: byId[item.ComponentProductId], item.Quantity))
                        .ToList()
                    : [(product, 1)];
                return CalculateRow(product, lines, settings, shippingRates, store);
            })
            .ToList();
    }

    private static ProfitRow CalculateRow(
        ProfitProduct product,
        List<(ProfitProduct Product, int Quantity)> lines,
        CostSettings settings,
        IReadOnlyList<ShippingRate> shippingRates,
        StoreOptions store)
    {
        var oldestUpdate = lines.Min(line => line.Product.CostsUpdatedAt);

        if (lines.Count == 0)
            return Incomplete(product, "Stack has no contents.", oldestUpdate);

        var missing = lines
            .Where(line => line.Product.SupplierCost is null || line.Product.ShippingWeightLb is null)
            .Select(line => line.Product.Name)
            .Distinct()
            .ToList();
        if (missing.Count > 0)
            return Incomplete(product, "Supplier cost or weight not set: " + string.Join(", ", missing), oldestUpdate);

        var productCost = lines.Sum(line => line.Product.SupplierCost!.Value * line.Quantity);

        // The supplier charges the first-unit fee once per distinct product, the lower fee for each extra unit.
        var fulfillment = lines.Sum(line =>
            settings.FulfillmentFirstUnit + settings.FulfillmentAdditionalUnit * (line.Quantity - 1));

        var weight = lines.Sum(line => line.Product.ShippingWeightLb!.Value * line.Quantity);
        if (ShippingFor(weight, shippingRates, settings.ShippingExtraLbRate) is not { } shipping)
            return Incomplete(product, "No shipping rates are set.", oldestUpdate);

        var supplierFee = Round((productCost + fulfillment + shipping) * settings.SupplierProcessingPercent / 100);
        var supplierTotal = productCost + fulfillment + shipping + supplierFee;

        var oneTime = Sale(product.Price, store, settings, supplierTotal);
        var subscriptionPrice = store.SubscribedPrice(product.Price);
        var subscription = Sale(subscriptionPrice, store, settings, supplierTotal);

        return new ProfitRow
        {
            ProductId = product.Id,
            Name = product.Name,
            IsStack = product.IsStack,
            IsActive = product.IsActive,
            Price = product.Price,
            ProductCost = productCost,
            Fulfillment = fulfillment,
            Shipping = shipping,
            SupplierFee = supplierFee,
            ShippingCharged = oneTime.ShippingCharged,
            CardFee = oneTime.CardFee,
            Profit = oneTime.Profit,
            SubscriptionPrice = subscriptionPrice,
            SubscriptionProfit = subscription.Profit,
            BreakEven = Round((supplierTotal + settings.CardProcessingFixed) / (1 - settings.CardProcessingPercent / 100)),
            CostsUpdatedAt = oldestUpdate
        };
    }

    /// <summary>A single order at this price: the customer also pays the store's shipping charge, if any.</summary>
    private static (decimal ShippingCharged, decimal CardFee, decimal Profit) Sale(
        decimal price, StoreOptions store, CostSettings settings, decimal supplierTotal)
    {
        var shippingCharged = store.ShippingFor(price);
        var charged = price + shippingCharged;
        var cardFee = Round(charged * settings.CardProcessingPercent / 100 + settings.CardProcessingFixed);
        return (shippingCharged, cardFee, charged - cardFee - supplierTotal);
    }

    public static decimal? ShippingFor(decimal weightLb, IReadOnlyList<ShippingRate> rates, decimal extraLbRate)
    {
        var bands = rates
            .Where(rate => rate.MaxWeightLb is not null && rate.Rate is not null)
            .OrderBy(rate => rate.MaxWeightLb)
            .ToList();
        if (bands.Count == 0)
            return null;

        var band = bands.FirstOrDefault(rate => weightLb <= rate.MaxWeightLb);
        if (band is not null)
            return band.Rate;

        var top = bands[^1];
        return top.Rate!.Value + Math.Ceiling(weightLb - top.MaxWeightLb!.Value) * extraLbRate;
    }

    private static ProfitRow Incomplete(ProfitProduct product, string reason, DateTime? costsUpdatedAt) => new()
    {
        ProductId = product.Id,
        Name = product.Name,
        IsStack = product.IsStack,
        IsActive = product.IsActive,
        Price = product.Price,
        Missing = reason,
        CostsUpdatedAt = costsUpdatedAt
    };

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
