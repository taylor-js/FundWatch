using FundWatch.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using FundWatch.Models;
using Syncfusion.Blazor;
using FundWatch.Controllers; // Ensure this using directive is included

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
builder.Services.AddRazorPages();

// Register StockService
builder.Services.AddHttpClient<StockService>();

var connectionString = builder.Configuration.GetConnectionString("HerokuPostgres");

// Register the DbContext for Identity
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register the DbContext for application-specific tables
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register Identity services with IdentityUser and IdentityRole
builder.Services.AddDefaultIdentity<IdentityUser>(options => { options.SignIn.RequireConfirmedAccount = false; })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddHttpClient<StockService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddTransient<StockService>();
builder.Services.AddLogging();
builder.Services.AddMemoryCache();
// Register a mock email sender for development
builder.Services.AddSingleton<IEmailSender, MockEmailSender>();

// Configure cookie policy to handle SameSite attribute
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.Secure = CookieSecurePolicy.Always; // Ensure cookies are sent over HTTPS
    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = opt =>
    {
        opt.HttpContext.Response.Redirect("/Identity/Account/Login");
        return Task.CompletedTask;
    };
});

// Register Syncfusion License
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Mgo+DSMBMAY9C3t2U1hhQlJBfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTX5bdERiWXtfc3BdT2FU\r\n");

// Register Syncfusion Blazor services
builder.Services.AddSyncfusionBlazor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error"); // Handle exceptions and redirect to Error page
    app.UseHsts(); // Add HTTP Strict Transport Security (HSTS) headers for added security
}

app.UseHttpsRedirection(); // Redirect HTTP to HTTPS
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication(); // Ensure authentication middleware is added
app.UseAuthorization();
app.UseCookiePolicy(); // Apply the cookie policy

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "AppUserStocks",
    pattern: "{controller=AppUserStocks}/{action=Dashboard}/{id?}");

app.MapControllerRoute(
    name: "AppStockSimulation",
    pattern: "{controller=AppStockSimulation}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "AppStockTransaction",
    pattern: "{controller=AppStockTransaction}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "AppWatchlist",
    pattern: "{controller=AppWatchlist}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

// Define the mock email sender class
public class MockEmailSender : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // For development/testing, just return a completed task
        return Task.CompletedTask;
    }
}