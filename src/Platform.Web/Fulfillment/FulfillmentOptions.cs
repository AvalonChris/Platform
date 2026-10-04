namespace Platform.Web.Fulfillment;

/// <summary>Settings for relaying paid orders to Supliful through a Shopify store.</summary>
public class FulfillmentOptions
{
    /// <summary>Off by default so no order leaves the system until the Shopify store is set up.</summary>
    public bool Enabled { get; set; }

    /// <summary>The store's myshopify.com domain, for example your-store.myshopify.com.</summary>
    public string ShopDomain { get; set; } = "";

    /// <summary>Admin API access token of the Shopify custom app. Keep it in user-secrets, never in appsettings.json.</summary>
    public string AccessToken { get; set; } = "";

    public string ApiVersion { get; set; } = "2026-07";

    /// <summary>Orders paid through the test gateway are skipped unless this is on, because Supliful would ship and bill them.</summary>
    public bool SendTestOrders { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ShopDomain) && !string.IsNullOrWhiteSpace(AccessToken);
}
