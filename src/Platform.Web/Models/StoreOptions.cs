using System.Globalization;

namespace Platform.Web.Models;

public class StoreOptions
{
    public string BrandName { get; set; } = "";

    /// <summary>The site's public address, used for links in emails, for example https://yourstore.com.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public string AbsoluteUrl(string path) => PublicBaseUrl.TrimEnd('/') + path;

    /// <summary>The registered business name shown in the legal pages and footer.</summary>
    public string LegalName { get; set; } = "";

    /// <summary>Where customers write for support, refunds and privacy requests.</summary>
    public string ContactEmail { get; set; } = "";

    /// <summary>The US state whose law governs the terms.</summary>
    public string LegalState { get; set; } = "";

    /// <summary>Shown as "Last updated" on the legal pages.</summary>
    public string PoliciesUpdated { get; set; } = "";

    public int GuaranteeDays { get; set; } = 30;
    public int SubscriptionDiscountPercent { get; set; }

    public decimal FlatShippingRate { get; set; }

    /// <summary>Orders with a subtotal at or above this ship free. Null means the flat rate always applies.</summary>
    public decimal? FreeShippingThreshold { get; set; }

    public decimal SubscribedPrice(decimal price) =>
        Math.Round(price * (100 - SubscriptionDiscountPercent) / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal ShippingFor(decimal subtotal) =>
        FreeShippingThreshold is { } threshold && subtotal >= threshold ? 0m : FlatShippingRate;
}

public static class Money
{
    public static string Format(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
