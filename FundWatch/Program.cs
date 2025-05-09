// File: FundWatch/Program.cs

using FundWatch.Data;
using FundWatch.Models;
using FundWatch.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor;
using System.IO;
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
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Database connection string is not configured.");
    }
    services.AddDbContext<AuthDbContext>(options => options.UseSqlServer(connectionString));
    services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

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
    var polygonApiKey = configuration["PolygonApi:ApiKey"];
    if (string.IsNullOrEmpty(polygonApiKey))
    {
        throw new InvalidOperationException("Polygon API key is not configured.");
    }
    services.AddHttpClient("PolygonApi", client =>
    {
        client.BaseAddress = new Uri(configuration["PolygonApi:BaseUrl"] ?? "https://api.polygon.io");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {polygonApiKey}");
    });


    // Core Services
    services.AddMemoryCache(options =>
    {
        options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
    });
    services.AddLogging();
    services.AddScoped<StockService>();
    services.AddScoped<ChartDataService>();
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

    // Data Protection Configuration - using file system for now
    services.AddDataProtection()
        .SetApplicationName("FundWatch")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FundWatch", "DataProtection-Keys")));
    
    // Syncfusion Configuration
    var directLicenseKey = "Ngo9BigBOggjHTQxAR8/V1NMaF5cXmBCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXxcc3VURWVdWE11WUA=";
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(directLicenseKey);
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
    // app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCookiePolicy();

    // Route Configuration
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=AppUserStocks}/{action=Dashboard}/{id?}");

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
