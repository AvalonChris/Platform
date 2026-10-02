using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Models;

public class CheckoutForm
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your full name.")]
    [StringLength(100)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Enter your street address.")]
    [StringLength(100)]
    public string Line1 { get; set; } = "";

    [StringLength(100)]
    public string? Line2 { get; set; }

    [Required(ErrorMessage = "Enter your city.")]
    [StringLength(100)]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Choose your state.")]
    public string Region { get; set; } = "";

    [Required(ErrorMessage = "Enter your ZIP code.")]
    [RegularExpression(@"^\d{5}(-\d{4})?$", ErrorMessage = "Enter a 5-digit ZIP code.")]
    public string PostalCode { get; set; } = "";

    [StringLength(30)]
    public string? Phone { get; set; }

    public bool AcceptRenewal { get; set; }
}

public class CheckoutViewModel
{
    public required CheckoutForm Form { get; init; }
    public required CartViewModel Cart { get; init; }
    public required decimal Shipping { get; init; }
    public required string PaymentDescription { get; init; }
    public string? PaymentError { get; init; }

    public decimal Total => Cart.Subtotal + Shipping;
}

public class Order
{
    public long Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public string Email { get; init; } = "";
    public string Status { get; init; } = "";
    public string ShipFullName { get; init; } = "";
    public string ShipLine1 { get; init; } = "";
    public string ShipLine2 { get; init; } = "";
    public string ShipCity { get; init; } = "";
    public string ShipRegion { get; init; } = "";
    public string ShipPostalCode { get; init; } = "";
    public decimal Subtotal { get; init; }
    public decimal ShippingTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal Total { get; init; }
    public string? PaymentProvider { get; init; }
    public string? PaymentReference { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PlacedAt { get; init; }
}

public class OrderItem
{
    public string ProductName { get; init; } = "";
    public string PurchaseMode { get; init; } = PurchaseModes.Once;
    public int? FrequencyDays { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }

    public string PlanText => PurchaseMode == PurchaseModes.Subscription
        ? $"Subscription · every {FrequencyDays} days"
        : "One-time purchase";
}

public record OrderViewModel(Order Order, IReadOnlyList<OrderItem> Items);

public static class UsStates
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("AL", "Alabama"), ("AK", "Alaska"), ("AZ", "Arizona"), ("AR", "Arkansas"), ("CA", "California"),
        ("CO", "Colorado"), ("CT", "Connecticut"), ("DE", "Delaware"), ("DC", "District of Columbia"),
        ("FL", "Florida"), ("GA", "Georgia"), ("HI", "Hawaii"), ("ID", "Idaho"), ("IL", "Illinois"),
        ("IN", "Indiana"), ("IA", "Iowa"), ("KS", "Kansas"), ("KY", "Kentucky"), ("LA", "Louisiana"),
        ("ME", "Maine"), ("MD", "Maryland"), ("MA", "Massachusetts"), ("MI", "Michigan"), ("MN", "Minnesota"),
        ("MS", "Mississippi"), ("MO", "Missouri"), ("MT", "Montana"), ("NE", "Nebraska"), ("NV", "Nevada"),
        ("NH", "New Hampshire"), ("NJ", "New Jersey"), ("NM", "New Mexico"), ("NY", "New York"),
        ("NC", "North Carolina"), ("ND", "North Dakota"), ("OH", "Ohio"), ("OK", "Oklahoma"), ("OR", "Oregon"),
        ("PA", "Pennsylvania"), ("RI", "Rhode Island"), ("SC", "South Carolina"), ("SD", "South Dakota"),
        ("TN", "Tennessee"), ("TX", "Texas"), ("UT", "Utah"), ("VT", "Vermont"), ("VA", "Virginia"),
        ("WA", "Washington"), ("WV", "West Virginia"), ("WI", "Wisconsin"), ("WY", "Wyoming")
    ];

    public static bool IsValid(string? code) => All.Any(state => state.Code == code);
}
