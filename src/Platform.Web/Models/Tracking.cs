namespace Platform.Web.Models;

public static class Tracking
{
    public static readonly string[] Carriers = ["USPS", "UPS", "FedEx", "Other"];

    /// <summary>The carrier's public tracking page for this number, or null when there isn't a known one.</summary>
    public static string? UrlFor(string? carrier, string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return null;

        var escaped = Uri.EscapeDataString(number.Trim());
        return carrier switch
        {
            "USPS" => $"https://tools.usps.com/go/TrackConfirmAction?tLabels={escaped}",
            "UPS" => $"https://www.ups.com/track?tracknum={escaped}",
            "FedEx" => $"https://www.fedex.com/fedextrack/?trknbr={escaped}",
            _ => null
        };
    }
}
