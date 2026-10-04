using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Platform.Web.Fulfillment;

public class FulfillmentException(string message) : Exception(message);

/// <summary>
/// Creates orders in the Shopify store that Supliful watches. Shopify is only a relay: the customer
/// has already paid on our site, so orders are created as paid and Shopify sends no emails.
/// </summary>
public class ShopifyClient(IHttpClientFactory httpClientFactory, IOptions<FulfillmentOptions> options)
{
    public static string TagFor(string orderNumber) => $"platform-{orderNumber}";

    /// <summary>Finds an order this site already sent, so a retry after a crash doesn't ship twice.</summary>
    public async Task<string?> FindOrderAsync(string orderNumber)
    {
        var data = await QueryAsync(
            "query FindOrder($query: String!) { orders(first: 1, query: $query) { nodes { id } } }",
            new JsonObject { ["query"] = $"tag:'{TagFor(orderNumber)}'" });

        return data["orders"]?["nodes"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
    }

    public async Task<string> CreateOrderAsync(FulfillmentOrder order, IReadOnlyList<FulfillmentLine> lines)
    {
        var (firstName, lastName) = SplitName(order.ShipFullName);
        var address = new JsonObject
        {
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["address1"] = order.ShipLine1,
            ["address2"] = order.ShipLine2,
            ["city"] = order.ShipCity,
            ["provinceCode"] = order.ShipRegion,
            ["zip"] = order.ShipPostalCode,
            ["countryCode"] = order.ShipCountry.Trim()
        };

        var lineItems = new JsonArray();
        foreach (var line in lines)
            lineItems.Add(new JsonObject { ["variantId"] = line.VariantId, ["quantity"] = line.Quantity });

        var variables = new JsonObject
        {
            ["order"] = new JsonObject
            {
                ["email"] = order.Email,
                ["customer"] = new JsonObject
                {
                    ["toUpsert"] = new JsonObject
                    {
                        ["email"] = order.Email,
                        ["firstName"] = firstName,
                        ["lastName"] = lastName
                    }
                },
                ["lineItems"] = lineItems,
                ["shippingAddress"] = address,
                ["billingAddress"] = address.DeepClone(),
                ["financialStatus"] = "PAID",
                ["note"] = $"Order {order.OrderNumber} from our store. Payment was taken on our site.",
                ["tags"] = new JsonArray(TagFor(order.OrderNumber))
            },
            ["options"] = new JsonObject
            {
                ["inventoryBehaviour"] = "BYPASS",
                ["sendReceipt"] = false,
                ["sendFulfillmentReceipt"] = false
            }
        };

        var data = await QueryAsync(
            """
            mutation CreateOrder($order: OrderCreateOrderInput!, $options: OrderCreateOptionsInput) {
              orderCreate(order: $order, options: $options) {
                order { id }
                userErrors { field message }
              }
            }
            """,
            variables);

        var result = data["orderCreate"];
        var userErrors = result?["userErrors"]?.AsArray() ?? [];
        if (userErrors.Count > 0)
            throw new FulfillmentException(
                "Shopify rejected the order: " + string.Join("; ", userErrors.Select(error => error?["message"]?.GetValue<string>())));

        return result?["order"]?["id"]?.GetValue<string>()
            ?? throw new FulfillmentException("Shopify did not return an order id.");
    }

    private async Task<JsonNode> QueryAsync(string query, JsonObject variables)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://{settings.ShopDomain}/admin/api/{settings.ApiVersion}/graphql.json")
        {
            Content = JsonContent.Create(new JsonObject { ["query"] = query, ["variables"] = variables })
        };
        request.Headers.Add("X-Shopify-Access-Token", settings.AccessToken);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new FulfillmentException($"Shopify returned HTTP {(int)response.StatusCode}: {Shorten(body)}");

        var json = JsonNode.Parse(body) ?? throw new FulfillmentException("Shopify returned an empty response.");
        if (json["errors"] is JsonArray { Count: > 0 } errors)
            throw new FulfillmentException(
                "Shopify error: " + string.Join("; ", errors.Select(error => error?["message"]?.GetValue<string>())));

        return json["data"] ?? throw new FulfillmentException("Shopify returned no data.");
    }

    private static (string First, string Last) SplitName(string fullName)
    {
        var name = fullName.Trim();
        var space = name.LastIndexOf(' ');
        return space < 0 ? (name, name) : (name[..space], name[(space + 1)..]);
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
