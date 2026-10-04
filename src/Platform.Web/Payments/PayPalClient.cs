using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Platform.Web.Payments;

public class PayPalOptions
{
    /// <summary>"sandbox" for test payments, "live" for real ones.</summary>
    public string Environment { get; set; } = "sandbox";

    public string ClientId { get; set; } = "";

    /// <summary>Keep it in user-secrets or an environment variable, never in appsettings.json.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>The id PayPal shows for the webhook registered for this app, used to verify incoming webhooks.</summary>
    public string WebhookId { get; set; } = "";

    /// <summary>Development only: accept webhooks without checking them with PayPal, for local testing.</summary>
    public bool SkipWebhookVerification { get; set; }

    public bool IsSandbox => !string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    public string ApiBase => IsSandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com";

    /// <summary>Recorded on orders. Sandbox orders are never sent for fulfilment.</summary>
    public string ProviderName => IsSandbox ? "paypal-sandbox" : "paypal";
}

/// <param name="Issue">PayPal's error code, such as INSTRUMENT_DECLINED, when it gave one.</param>
public class PayPalException(string message, string? issue = null, int statusCode = 0) : Exception(message)
{
    public string? Issue { get; } = issue;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Calls the PayPal REST API (Orders v2) with an OAuth token cached until shortly before it expires.</summary>
public class PayPalClient(IHttpClientFactory httpClientFactory, IOptions<PayPalOptions> options)
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset accessTokenExpires;

    /// <param name="requestId">Repeating a request id returns the original result instead of creating a second order.</param>
    public Task<JsonNode> CreateOrderAsync(JsonObject order, string? requestId = null) =>
        SendAsync(HttpMethod.Post, "/v2/checkout/orders", order, requestId ?? Guid.NewGuid().ToString());
    /// <summary>The request id makes a repeated capture of the same order safe.</summary>
    public Task<JsonNode> CaptureOrderAsync(string orderId) =>
        SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", new JsonObject(),
            requestId: $"capture-{orderId}");

    public Task<JsonNode> GetOrderAsync(string orderId) =>
        SendAsync(HttpMethod.Get, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}", body: null, requestId: null);

    /// <summary>
    /// Asks PayPal whether a webhook really came from it. The event is passed through exactly as received,
    /// because re-serializing it can change the bytes and make the check fail.
    /// </summary>
    public async Task<bool> VerifyWebhookSignatureAsync(
        string transmissionId, string transmissionTime, string certUrl, string authAlgo, string transmissionSig,
        string webhookId, string rawEvent)
    {
        var fields = new JsonObject
        {
            ["auth_algo"] = authAlgo,
            ["cert_url"] = certUrl,
            ["transmission_id"] = transmissionId,
            ["transmission_sig"] = transmissionSig,
            ["transmission_time"] = transmissionTime,
            ["webhook_id"] = webhookId
        }.ToJsonString();
        var body = fields[..^1] + ",\"webhook_event\":" + rawEvent + "}";

        var token = await GetAccessTokenAsync();
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.ApiBase + "/v1/notifications/verify-webhook-signature")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return false;

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        return Text(json?["verification_status"]) == "SUCCESS";
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, string? requestId)
    {
        var token = await GetAccessTokenAsync();
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(method, options.Value.ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Prefer", "return=representation");
        if (requestId is not null)
            request.Headers.Add("PayPal-Request-Id", requestId);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();

        if (!response.IsSuccessStatusCode)
        {
            var detail = json["details"]?[0];
            throw new PayPalException(
                Text(detail?["description"]) ?? Text(json["message"]) ?? $"PayPal returned HTTP {(int)response.StatusCode}.",
                Text(detail?["issue"]),
                (int)response.StatusCode);
        }

        return json;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpires)
            return accessToken;

        await tokenLock.WaitAsync();
        try
        {
            if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpires)
                return accessToken;

            var settings = options.Value;
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiBase + "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")])
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}")));

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new PayPalException(
                    $"PayPal sign-in failed (HTTP {(int)response.StatusCode}). Check the client ID, secret and environment.");

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())
                ?? throw new PayPalException("PayPal returned an empty sign-in response.");

            accessToken = Text(json["access_token"]) ?? throw new PayPalException("PayPal returned no access token.");
            var expiresIn = json["expires_in"]?.GetValue<int>() ?? 0;
            accessTokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 300, 60));
            return accessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
