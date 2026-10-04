using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Platform.Web.Areas.Admin;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Controllers;
using Platform.Web.Data;
using Platform.Web.Email;
using Platform.Web.Fulfillment;
using Platform.Web.Models;
using Platform.Web.Payments;
using Platform.Web.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Platform");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Platform' is not configured. Set it with " +
        "`dotnet user-secrets set ConnectionStrings:Platform \"Host=localhost;Database=platform;Username=...;Password=...\"` " +
        "or the ConnectionStrings__Platform environment variable.");
}

// Postgres columns are snake_case; model properties are PascalCase.
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<ProductRepository>();
builder.Services.AddScoped<CartRepository>();
builder.Services.AddScoped<OrderRepository>();

// Used when PayPal isn't configured. The test gateway approves everything, so it must never run outside development.
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IPaymentGateway, TestPaymentGateway>();
else
    builder.Services.AddSingleton<IPaymentGateway, UnconfiguredPaymentGateway>();
// PayPal takes payments when its credentials are set; otherwise the gateway above is used.
builder.Services.Configure<PayPalOptions>(builder.Configuration.GetSection("Payments:PayPal"));
builder.Services.AddSingleton<PayPalClient>();
builder.Services.AddSingleton<PayPalCheckout>();
builder.Services.AddScoped<PayPalWebhookRepository>();
builder.Services.AddScoped<PayPalWebhooks>();

// Keys that protect sign-in and antiforgery cookies are kept on disk, so restarts under IIS don't sign
// everyone out or break forms that are open. The app pool needs write access to this folder.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
builder.Services.AddDataProtection()
    .SetApplicationName("Platform")
    .PersistKeysToFileSystem(new DirectoryInfo(string.IsNullOrWhiteSpace(keysPath)
        ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")
        : keysPath));

// The payment page's script sends the antiforgery token in this header.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection("Store"));
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// Relays paid orders to Supliful through a Shopify store (see Fulfillment/).
builder.Services.Configure<FulfillmentOptions>(builder.Configuration.GetSection("Fulfillment"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<FulfillmentRepository>();
builder.Services.AddSingleton<ShopifyClient>();
builder.Services.AddHostedService<FulfillmentWorker>();

// Customer accounts, subscription renewals and email.
builder.Services.AddScoped<CustomerAccountRepository>();
builder.Services.AddScoped<RenewalRepository>();
builder.Services.AddScoped<RenewalService>();
builder.Services.AddHostedService<RenewalWorker>();

builder.Services.AddScoped<OrderEmails>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
if (builder.Configuration.GetSection("Email").Get<EmailOptions>()?.IsSmtpConfigured == true)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, FileEmailSender>();

builder.Services.AddScoped<AdminUserRepository>();
builder.Services.AddScoped<AdminProductRepository>();
builder.Services.AddScoped<AdminOrderRepository>();
builder.Services.AddScoped<AdminCostRepository>();

builder.Services.AddAuthentication().AddCookie(AdminAuth.Scheme, options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/login";
    options.Cookie.Name = "admin_auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
}).AddCookie(CustomerAuth.Scheme, options =>
{
    options.LoginPath = "/account/sign-in";
    options.AccessDeniedPath = "/account/sign-in";
    options.Cookie.Name = "customer_auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});
builder.Services.AddAuthorization();

// Slows down password guessing on the admin sign-in form.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AdminAuth.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));

    // Limits how many sign-in emails one visitor can trigger.
    options.AddPolicy(CustomerAuth.SignInRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) }));
});

var app = builder.Build();

// `dotnet run -- migrate` applies pending migrations; `dotnet run -- seed` loads placeholder products.
if (args.Contains("migrate") || args.Contains("seed"))
{
    var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
    var dbDirectory = Path.Combine(AppContext.BaseDirectory, "Db");

    if (args.Contains("migrate"))
        await Migrator.MigrateAsync(dataSource, Path.Combine(dbDirectory, "Migrations"), app.Logger);
    if (args.Contains("seed"))
        await Migrator.RunScriptAsync(dataSource, Path.Combine(dbDirectory, "Seed", "dev_seed.sql"), app.Logger);
    return;
}

// `dotnet run -- create-admin you@example.com` creates an admin or resets its password.
if (args.Contains("create-admin"))
{
    var users = new AdminUserRepository(app.Services.GetRequiredService<NpgsqlDataSource>());
    var email = args.SkipWhile(arg => arg != "create-admin").Skip(1).FirstOrDefault();
    Environment.ExitCode = await AdminCli.CreateAdminAsync(users, email);
    return;
}

// `dotnet run -- run-renewals` sends due reminders and charges due subscriptions once, then exits.
if (args.Contains("run-renewals"))
{
    using var scope = app.Services.CreateScope();
    var summary = await scope.ServiceProvider.GetRequiredService<RenewalService>().RunAsync();
    app.Logger.LogInformation("Renewal run: {Summary}", summary);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
