using FundWatch.Data;
using FundWatch.Models;
using FundWatch.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
ConfigureApp(app);

app.Run();

void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Database Configuration
    var connectionString = configuration.GetConnectionString("HerokuPostgres");
    services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
    services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

    // Identity Configuration
    services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

    // MVC and Razor Pages
    services.AddControllersWithViews()
        .AddRazorRuntimeCompilation();
    services.AddRazorPages();

    // HTTP and API Services
    services.AddHttpClient();
    services.AddHttpClient("PolygonApi", client =>
    {
        client.BaseAddress = new Uri(configuration["PolygonApi:BaseUrl"]);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });

    // Core Services
    services.AddMemoryCache(options =>
    {
        options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
    });
    services.AddLogging();
    services.AddScoped<StockService>();
    services.AddHostedService<StockDataBackgroundService>();
    // Email Service (Mock for development)
    services.AddSingleton<IEmailSender, MockEmailSender>();

    // Cookie Policy Configuration
    services.Configure<CookiePolicyOptions>(options =>
    {
        options.MinimumSameSitePolicy = SameSiteMode.None;
        options.Secure = CookieSecurePolicy.Always;
        options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
    });

    // Identity Cookie Configuration
    services.ConfigureApplicationCookie(options =>
    {
        options.Events.OnRedirectToLogin = context =>
        {
            context.HttpContext.Response.Redirect("/Identity/Account/Login");
            return Task.CompletedTask;
        };
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Ensure HTTPS is being used
        options.Cookie.SameSite = SameSiteMode.Lax; // Change to 'Lax' if 'Strict' causes issues
    });


    // Syncfusion Configuration
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
        "Mgo+DSMBMAY9C3t2U1hhQlJBfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTX5bdERiWXtfc3BdT2FU"
    );
    services.AddSyncfusionBlazor();
}

void ConfigureApp(WebApplication app)
{
    // Development/Production environment configuration
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    // Middleware Pipeline
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCookiePolicy();

    // Route Configuration
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

    app.MapControllerRoute(
        name: "Identity_Area",
        pattern: "Identity/{**catchall}",
        defaults: new { area = "Identity" });

    app.MapRazorPages();
}

// Mock Email Sender for Development
public class MockEmailSender : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        return Task.CompletedTask;
    }
}