using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Platform.Web.Areas.Admin;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Data;
using Platform.Web.Models;
using Platform.Web.Payments;

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

// No real payment processor yet: the test gateway approves everything, so it must never run outside development.
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IPaymentGateway, TestPaymentGateway>();
else
    builder.Services.AddSingleton<IPaymentGateway, UnconfiguredPaymentGateway>();
builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection("Store"));
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddScoped<AdminUserRepository>();
builder.Services.AddScoped<AdminProductRepository>();
builder.Services.AddScoped<AdminOrderRepository>();

builder.Services.AddAuthentication().AddCookie(AdminAuth.Scheme, options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/login";
    options.Cookie.Name = "admin_auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
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
