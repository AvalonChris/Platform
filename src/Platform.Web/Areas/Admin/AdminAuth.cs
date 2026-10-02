using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Platform.Web.Areas.Admin;

public static class AdminAuth
{
    /// <summary>Cookie scheme for staff. Kept separate so customer sign-in can get its own scheme later.</summary>
    public const string Scheme = "Admin";

    public const string LoginRateLimitPolicy = "admin-login";
}

/// <summary>Base class for every admin page: requires a signed-in admin.</summary>
[Area("Admin")]
[Authorize(AuthenticationSchemes = AdminAuth.Scheme)]
public abstract class AdminController : Controller;
