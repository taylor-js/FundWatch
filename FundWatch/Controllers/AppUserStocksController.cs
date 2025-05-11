using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using FundWatch.Models;
using FundWatch.Services;
using FundWatch.Models.ViewModels;

namespace FundWatch.Controllers
{
    [Authorize]
    public class AppUserStocksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AppUserStocksController> _logger;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly StockService _stockService;
        private readonly ChartDataService _chartDataService;
        private readonly IMemoryCache _cache;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int CACHE_DURATION_MINUTES = 15;

        public AppUserStocksController(
        ApplicationDbContext context,
        ILogger<AppUserStocksController> logger,
        UserManager<IdentityUser> userManager,
        StockService stockService,
        ChartDataService chartDataService,
        IMemoryCache cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
            _chartDataService = chartDataService ?? throw new ArgumentNullException(nameof(chartDataService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        // GET: AppUserStocks/Dashboard
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }

            try
            {
                var viewModel = await PrepareDashboardViewModel(userId);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing dashboard for user {UserId}", userId);
                TempData["ErrorMessage"] = "Unable to load dashboard. Please try again later.";
                return View(new PortfolioDashboardViewModel());
            }
        }

        private async Task<PortfolioDashboardViewModel> PrepareDashboardViewModel(string userId)
        {
            // Start with a stopwatch to track performance
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Get user stocks data first
            var userStocks = await _context.UserStocks
                .Where(u => u.UserId == userId &&
                            (u.NumberOfSharesPurchased - (u.NumberOfSharesSold ?? 0)) > 0)
                .AsNoTracking()
                .ToListAsync();

            if (!userStocks.Any())
                return new PortfolioDashboardViewModel();

            var symbols = userStocks.Select(s => s.StockSymbol.Trim().ToUpper())
                .Distinct()
                .ToList();
            
            // Create a test request to check API status 
            var testSymbol = symbols.First();
            var testPrice = await _stockService.GetRealTimePricesAsync(new List<string> { testSymbol });
            
            if (testPrice == null || !testPrice.Any())
            {
                _logger.LogError("API connectivity test failed for symbol {Symbol}. API key may be invalid.", testSymbol);
                TempData["ApiError"] = "Unable to connect to stock data API. Please check your API key configuration.";
            }
                
            _logger.LogInformation("Preparing dashboard for user {UserId} with {SymbolCount} symbols", userId, symbols.Count);

            // Initialize the view model with stocks data
            var viewModel = new PortfolioDashboardViewModel { UserStocks = userStocks };
            
            try
            {
                // Fetch all required data in parallel
                var pricesTask = GetCachedPrices(symbols);
                var detailsTask = GetCachedCompanyDetails(symbols);
                var historicalDataTask = GetCachedHistoricalData(symbols);
                
                // Fetch real chart data in parallel
                var monthlyPerformanceTask = _chartDataService.CalculateMonthlyPerformanceAsync(userStocks);
                var rollingReturnsTask = _chartDataService.CalculateRollingReturnsAsync(userStocks);
                var portfolioGrowthTask = _chartDataService.CalculatePortfolioGrowthAsync(userStocks);
                var riskMetricsTask = _chartDataService.CalculateRiskMetricsAsync(userStocks);
                var drawdownDataTask = _chartDataService.CalculateDrawdownSeriesAsync(userStocks);

                // Wait for all data to be fetched
                await Task.WhenAll(
                    pricesTask, detailsTask, historicalDataTask,
                    monthlyPerformanceTask, rollingReturnsTask, portfolioGrowthTask, 
                    riskMetricsTask, drawdownDataTask
                );

                var cachedPrices = await pricesTask;
                var cachedDetails = await detailsTask;
                var historicalData = await historicalDataTask;

                // Update stock objects with latest prices and sector/industry data
                UpdateUserStocksWithExtendedData(userStocks, cachedPrices, cachedDetails);

                // Calculate basic metrics first
                viewModel.PortfolioMetrics = await CalculatePortfolioMetrics(userStocks, cachedPrices, cachedDetails);
                viewModel.SectorDistribution = CalculateSectorDistribution(userStocks, cachedDetails);
                viewModel.CompanyDetails = cachedDetails;

                // Calculate performance data - this is what feeds the chart
                var performanceData = CalculatePerformanceData(userStocks, historicalData);
                
                // Verify that performance data contains valid entries
                int validSeriesCount = 0;
                foreach (var series in performanceData)
                {
                    if (series.Value != null && series.Value.Count > 0)
                    {
                        validSeriesCount++;
                        _logger.LogInformation("Series {Symbol} has {Count} data points", 
                            series.Key, series.Value.Count);
                    }
                }
                
                if (validSeriesCount == 0)
                {
                    _logger.LogWarning("No valid performance data was calculated for dashboard");
                }
                else
                {
                    _logger.LogInformation("Generated {Count} valid data series for chart", validSeriesCount);
                }
                
                viewModel.PerformanceData = performanceData;
                viewModel.HistoricalData = historicalData;
                
                // Set real chart data from ChartDataService
                viewModel.MonthlyPerformanceData = await monthlyPerformanceTask;
                viewModel.RollingReturnsData = await rollingReturnsTask;
                viewModel.PortfolioGrowthData = await portfolioGrowthTask;
                viewModel.RiskMetrics = await riskMetricsTask;
                viewModel.DrawdownData = await drawdownDataTask;
                
                // Log all chart data metrics with extra detail for drawdown data
                _logger.LogInformation(
                    "Chart data metrics - Monthly: {MonthlyCount}, Rolling: {RollingCount}, Growth: {GrowthCount}, Risk: {RiskCount}, Drawdown: {DrawdownCount}",
                    viewModel.MonthlyPerformanceData?.Count ?? 0,
                    viewModel.RollingReturnsData?.Count ?? 0,
                    viewModel.PortfolioGrowthData?.Count ?? 0,
                    viewModel.RiskMetrics?.Count ?? 0,
                    viewModel.DrawdownData?.Count ?? 0);

                // Special logging for drawdown data to check date range
                if (viewModel.DrawdownData != null && viewModel.DrawdownData.Any())
                {
                    var firstDate = viewModel.DrawdownData.OrderBy(d => d.Date).First().Date;
                    var lastDate = viewModel.DrawdownData.OrderBy(d => d.Date).Last().Date;
                    var dateSpan = (lastDate - firstDate).TotalDays;

                    _logger.LogInformation(
                        "Drawdown data date range: {FirstDate} to {LastDate} ({Days} days, {Years:F1} years)",
                        firstDate.ToString("yyyy-MM-dd"),
                        lastDate.ToString("yyyy-MM-dd"),
                        dateSpan,
                        dateSpan / 365.25);

                    // Check for max drawdown values too
                    var maxPortfolioDrawdown = viewModel.DrawdownData.Min(d => d.PortfolioDrawdown);
                    var maxBenchmarkDrawdown = viewModel.DrawdownData.Min(d => d.BenchmarkDrawdown);

                    _logger.LogInformation(
                        "Drawdown extremes: Portfolio {PortfolioMax:F2}%, Benchmark {BenchmarkMax:F2}%",
                        maxPortfolioDrawdown,
                        maxBenchmarkDrawdown);
                }
                
                // Cache the performance data separately with longer duration
                var performanceDataCacheKey = $"PerformanceData_{userId}";
                _cache.Set(performanceDataCacheKey, performanceData, TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing dashboard data");
            }
            
            sw.Stop();
            _logger.LogInformation("Dashboard preparation completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            return viewModel;
        }


        private DateTime GetNextTradingDay()
        {
            var tomorrow = DateTime.UtcNow.AddDays(1).Date;
            while (tomorrow.DayOfWeek == DayOfWeek.Saturday || tomorrow.DayOfWeek == DayOfWeek.Sunday)
            {
                tomorrow = tomorrow.AddDays(1);
            }
            return tomorrow.AddHours(13).AddMinutes(30); // 9:30 AM Eastern Time
        }

        private async Task<Dictionary<string, decimal>> GetCachedPrices(List<string> symbols)
        {
            var cachedPrices = new Dictionary<string, decimal>();
            var symbolsToFetch = new List<string>();

            foreach (var symbol in symbols)
            {
                // Check both cache key formats for compatibility
                var cacheKeys = new[] { $"RealTimePrice_{symbol}", $"Price_{symbol}", $"DailyClosingPrice_{symbol}" };
                bool found = false;
                
                foreach (var cacheKey in cacheKeys)
                {
                    if (_cache.TryGetValue(cacheKey, out decimal price))
                    {
                        cachedPrices[symbol] = price;
                        found = true;
                        break;
                    }
                }
                
                if (!found)
                {
                    symbolsToFetch.Add(symbol);
                }
            }

            if (symbolsToFetch.Any())
            {
                var freshPrices = await _stockService.GetRealTimePricesAsync(symbolsToFetch);
                foreach (var kvp in freshPrices)
                {
                    // Use consistent cache keys
                    var cacheKey = $"Price_{kvp.Key}";
                    _cache.Set(cacheKey, kvp.Value, TimeSpan.FromHours(4)); // Increased cache duration
                    cachedPrices[kvp.Key] = kvp.Value;
                }
            }

            return cachedPrices;
        }

        private async Task<Dictionary<string, CompanyDetails>> GetCachedCompanyDetails(List<string> symbols)
        {
            var result = new Dictionary<string, CompanyDetails>();
            var uncachedSymbols = new List<string>();

            foreach (var symbol in symbols)
            {
                if (_cache.TryGetValue($"Details_{symbol}", out CompanyDetails? details) && details != null)
                {
                    result[symbol] = details;
                }
                else
                {
                    uncachedSymbols.Add(symbol);
                }
            }

            if (uncachedSymbols.Any())
            {
                var fetchedDetails = await _stockService.GetCompanyDetailsAsync(uncachedSymbols);
                foreach (var (symbol, detail) in fetchedDetails)
                {
                    // Cache the company details with appropriate expiration
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(24))
                        .SetSlidingExpiration(TimeSpan.FromHours(1));
                    _cache.Set($"Details_{symbol}", detail, cacheOptions);

                    result[symbol] = detail;
                }
            }

            return result;
        }

        // Method to update stocks with current prices and sector/industry data
        private void UpdateUserStocksWithExtendedData(List<AppUserStock> stocks,
                                                     Dictionary<string, decimal> prices,
                                                     Dictionary<string, CompanyDetails> details)
        {
            foreach (var stock in stocks)
            {
                // Update current price from latest API data if available
                if (prices.TryGetValue(stock.StockSymbol, out var price))
                {
                    stock.CurrentPrice = price;
                }

                // Update sector and industry information from Polygon.io API data
                if (details.TryGetValue(stock.StockSymbol, out var companyDetails))
                {
                    // Set sector from the industry/sector information
                    stock.Sector = !string.IsNullOrEmpty(companyDetails.Industry)
                        ? companyDetails.Industry
                        : "Other";

                    // Set industry from the more detailed industry info if available
                    if (companyDetails.Extended != null)
                    {
                        // Use more specific sector from extended data if available
                        if (!string.IsNullOrEmpty(companyDetails.Extended.Sector))
                        {
                            stock.Sector = companyDetails.Extended.Sector;
                        }

                        // Set industry from extended data
                        stock.Industry = !string.IsNullOrEmpty(companyDetails.Extended.IndustryGroup)
                            ? companyDetails.Extended.IndustryGroup
                            : "Other";
                    }
                }
                else
                {
                    // Default values if no data available
                    stock.Sector = "Other";
                    stock.Industry = "Other";
                }
            }
        }

        // Method to calculate sector distribution for the portfolio
        private Dictionary<string, decimal> CalculateSectorDistribution(List<AppUserStock> stocks, Dictionary<string, CompanyDetails> details)
        {
            var sectorDistribution = new Dictionary<string, decimal>();

            foreach (var stock in stocks)
            {
                // Since we've already populated the Sector property, use that directly
                var sector = !string.IsNullOrEmpty(stock.Sector) ? stock.Sector : "Other";
                var value = stock.TotalValue;

                if (sectorDistribution.ContainsKey(sector))
                    sectorDistribution[sector] += value;
                else
                    sectorDistribution[sector] = value;
            }

            return sectorDistribution;
        }

        private async Task<Dictionary<string, List<StockDataPoint>>> GetCachedHistoricalData(List<string> symbols)
        {
            var result = new Dictionary<string, List<StockDataPoint>>();
            var uncachedSymbols = new List<string>();

            foreach (var symbol in symbols)
            {
                if (_cache.TryGetValue($"History_{symbol}", out List<StockDataPoint>? history) && history != null)
                {
                    result[symbol] = history;
                }
                else
                {
                    uncachedSymbols.Add(symbol);
                }
            }

            if (uncachedSymbols.Any())
            {
                // Process in smaller batches to avoid overloading
                const int batchSize = 3;
                for (int i = 0; i < uncachedSymbols.Count; i += batchSize)
                {
                    var batch = uncachedSymbols.Skip(i).Take(batchSize).ToList();
                    var history = await _stockService.GetRealTimeDataAsync(batch, 1825);
                    
                    foreach (var (symbol, data) in history)
                    {
                        result[symbol] = data;
                        // Cache for 24 hours
                        _cache.Set($"History_{symbol}", data, TimeSpan.FromHours(24));
                    }
                }
            }

            return result;
        }


        // GET: AppUserStocks
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var stocks = await _context.UserStocks
                .Where(u => u.UserId == userId)
                .ToListAsync();

            var symbols = stocks.Select(s => s.StockSymbol).ToList();
            var companyDetails = await GetCachedCompanyDetails(symbols);

            return View(new StockListViewModel
            {
                Stocks = stocks,
                CompanyDetails = companyDetails
            });
        }

        // GET: AppUserStocks/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                _logger.LogWarning("Details action called with null id");
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);
            _logger.LogInformation("Fetching details for stock with id {StockId} for user {UserId}", id, userId);

            var stock = await _context.UserStocks
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

            if (stock == null)
            {
                _logger.LogWarning("Stock with id {StockId} not found for user {UserId}", id, userId);
                return NotFound();
            }

            try
            {
                var companyDetails = await _stockService.GetCompanyDetailsAsync(new List<string> { stock.StockSymbol });
                var historicalData = await _stockService.GetRealTimeDataAsync(new List<string> { stock.StockSymbol }, 1825); // 5 years

                var viewModel = new Models.ViewModels.StockDetailsViewModel
                {
                    Stock = stock,
                    CompanyDetails = companyDetails[stock.StockSymbol],
                    HistoricalData = historicalData[stock.StockSymbol]
                };

                _logger.LogInformation("Successfully fetched details for stock with id {StockId} for user {UserId}", id, userId);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching details for stock with id {StockId} for user {UserId}", id, userId);
                return StatusCode(500, "Internal server error");
            }
        }


        [HttpGet]
        public async Task<IActionResult> CreateOrEdit(int id = 0)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
                }

                if (id == 0)
                {
                    return View(new AppUserStock
                    {
                        UserId = userId,
                        DatePurchased = DateTime.UtcNow.Date
                    });
                }

                var appUserStock = await _context.UserStocks
                    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

                if (appUserStock == null)
                {
                    _logger.LogWarning("User {UserId} attempted to edit non-existent or unauthorized stock {StockId}",
                        userId, id);
                    return NotFound();
                }

                try
                {
                    // Get an exact match for this stock symbol
                    var initialStock = await _stockService.GetExactStockAsync(appUserStock.StockSymbol);

                    if (initialStock != null)
                    {
                        ViewBag.InitialStock = new List<object>
                        {
                            new
                            {
                                symbol = initialStock.Symbol,
                                display = $"{initialStock.Symbol} - {initialStock.Name}",
                                fullName = initialStock.Name
                            }
                        };
                        
                        // Log to confirm data is correctly passed
                        _logger.LogInformation("Setting InitialStock for edit: Symbol={Symbol}, Name={Name}", 
                            initialStock.Symbol, initialStock.Name);
                    }
                    else
                    {
                        // Fallback: If we can't get stock details, at least populate with the symbol
                        ViewBag.InitialStock = new List<object>
                        {
                            new
                            {
                                symbol = appUserStock.StockSymbol,
                                display = appUserStock.StockSymbol,
                                fullName = string.Empty
                            }
                        };
                        
                        _logger.LogWarning("Using fallback stock data for {Symbol}", appUserStock.StockSymbol);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting stock details for {Symbol}", appUserStock.StockSymbol);
                    // Still provide a fallback
                    ViewBag.InitialStock = new List<object>
                    {
                        new
                        {
                            symbol = appUserStock.StockSymbol,
                            display = appUserStock.StockSymbol,
                            fullName = string.Empty
                        }
                    };
                }

                return View(appUserStock);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateOrEdit GET action for ID: {Id}", id);
                return RedirectToAction(nameof(Dashboard));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrEdit([Bind("Id,UserId,StockSymbol,PurchasePrice,DatePurchased,NumberOfSharesPurchased,CurrentPrice,DateSold,NumberOfSharesSold")] AppUserStock appUserStock)
        {
            if (!ModelState.IsValid)
            {
                // Preserve the stock information
                await PreserveStockInformation(appUserStock);
                return View(appUserStock);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(appUserStock.StockSymbol))
            {
                ModelState.AddModelError("StockSymbol", "Stock symbol is required");
                return View(appUserStock);
            }
            
            // Check if shares purchased equals shares sold
            if (appUserStock.NumberOfSharesSold.HasValue && 
                appUserStock.NumberOfSharesPurchased == appUserStock.NumberOfSharesSold.Value)
            {
                ModelState.AddModelError("NumberOfSharesSold", "Shares purchased equals shares sold. No data will be plotted.");
                TempData["EqualShares"] = "true";
                
                // Preserve the stock information
                await PreserveStockInformation(appUserStock);
                
                return View(appUserStock);
            }

            if (appUserStock.PurchasePrice <= 0)
            {
                // Double-check price data on server-side to ensure we have valid data
                try
                {
                    if (!string.IsNullOrEmpty(appUserStock.StockSymbol) && appUserStock.DatePurchased != default)
                    {
                        var purchaseDate = appUserStock.DatePurchased.ToString("yyyy-MM-dd");
                        var priceData = await _stockService.GetHistoricalPriceAsync(appUserStock.StockSymbol, appUserStock.DatePurchased);
                        if (priceData > 0)
                        {
                            appUserStock.PurchasePrice = priceData;
                            _logger.LogInformation("Updated purchase price from API for {Symbol}: {Price}", 
                                appUserStock.StockSymbol, priceData);
                        }
                        else
                        {
                            ModelState.AddModelError("PurchasePrice", "Purchase price must be greater than zero");
                            await PreserveStockInformation(appUserStock);
                            return View(appUserStock);
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("PurchasePrice", "Purchase price must be greater than zero");
                        await PreserveStockInformation(appUserStock);
                        return View(appUserStock);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching price data for {Symbol}", appUserStock.StockSymbol);
                    ModelState.AddModelError("PurchasePrice", "Purchase price must be greater than zero");
                    await PreserveStockInformation(appUserStock);
                    return View(appUserStock);
                }
            }

            if (appUserStock.NumberOfSharesPurchased <= 0)
            {
                ModelState.AddModelError("NumberOfSharesPurchased", "Number of shares must be greater than zero");
                await PreserveStockInformation(appUserStock);
                return View(appUserStock);
            }

            if (appUserStock.DateSold.HasValue)
            {
                if (appUserStock.DateSold.Value < appUserStock.DatePurchased)
                {
                    ModelState.AddModelError("DateSold", "Sale date cannot be before purchase date");
                    await PreserveStockInformation(appUserStock);
                    return View(appUserStock);
                }

                if (appUserStock.NumberOfSharesSold.HasValue &&
                    appUserStock.NumberOfSharesSold.Value > appUserStock.NumberOfSharesPurchased)
                {
                    ModelState.AddModelError("NumberOfSharesSold", "Cannot sell more shares than purchased");
                    await PreserveStockInformation(appUserStock);
                    return View(appUserStock);
                }
            }


            try
            {
                // Normalize dates to UTC
                appUserStock.DatePurchased = DateTime.SpecifyKind(appUserStock.DatePurchased.Date, DateTimeKind.Utc);
                appUserStock.DateSold = appUserStock.DateSold.HasValue
                    ? DateTime.SpecifyKind(appUserStock.DateSold.Value.Date, DateTimeKind.Utc)
                    : (DateTime?)null;


                appUserStock.StockSymbol = appUserStock.StockSymbol.Trim().ToUpper();

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    if (appUserStock.Id == 0)
                    {
                        // Create new stock position
                        appUserStock.UserId = userId;

                        // Check if the stock already exists for this user
                        var existingStock = await _context.UserStocks
                            .FirstOrDefaultAsync(s => s.UserId == userId && s.StockSymbol == appUserStock.StockSymbol);
                        
                        if (existingStock != null)
                        {
                            // Stock already exists, set error message for client-side handling
                            TempData["DuplicateStock"] = "true";
                            TempData["ExistingStockId"] = existingStock.Id.ToString();
                            return View(appUserStock);
                        }

                        // Get current price
                        var currentPrices = await _stockService.GetRealTimePricesAsync(
                            new List<string> { appUserStock.StockSymbol });

                        if (currentPrices.TryGetValue(appUserStock.StockSymbol, out decimal currentPrice))
                        {
                            appUserStock.CurrentPrice = currentPrice;
                        }
                        else
                        {
                            appUserStock.CurrentPrice = appUserStock.PurchasePrice;
                        }

                        _context.Add(appUserStock);
                    }
                    else
                    {
                        var existingStock = await _context.UserStocks
                            .FirstOrDefaultAsync(s => s.Id == appUserStock.Id && s.UserId == userId);

                        if (existingStock == null)
                        {
                            return NotFound();
                        }

                        // Update properties
                        existingStock.StockSymbol = appUserStock.StockSymbol;
                        existingStock.PurchasePrice = appUserStock.PurchasePrice;
                        existingStock.DatePurchased = appUserStock.DatePurchased;
                        existingStock.NumberOfSharesPurchased = appUserStock.NumberOfSharesPurchased;
                        existingStock.DateSold = appUserStock.DateSold;
                        existingStock.NumberOfSharesSold = appUserStock.NumberOfSharesSold;

                        // Update current price
                        var currentPrices = await _stockService.GetRealTimePricesAsync(
                            new List<string> { existingStock.StockSymbol });

                        if (currentPrices.TryGetValue(existingStock.StockSymbol, out decimal currentPrice))
                        {
                            existingStock.CurrentPrice = currentPrice;
                        }

                        _context.Update(existingStock);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return RedirectToAction(nameof(Dashboard));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving stock position for user {UserId}", userId);
                ModelState.AddModelError("", "An error occurred while saving the stock position.");
                await PreserveStockInformation(appUserStock);
                return View(appUserStock);
            }
        }

        public async Task<IActionResult> Delete(int? id)
        {
            try
            {
                if (id == null)
                {
                    return NotFound();
                }

                var userId = _userManager.GetUserId(User);
                var appUserStock = await _context.UserStocks
                    .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

                if (appUserStock == null)
                {
                    _logger.LogWarning("User {UserId} attempted to delete non-existent or unauthorized stock {StockId}",
                        userId, id);
                    return NotFound();
                }

                return View(appUserStock);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Delete GET action for ID: {Id}", id);
                throw;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }

            try
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var appUserStock = await _context.UserStocks
                        .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

                    if (appUserStock == null)
                    {
                        return NotFound();
                    }

                    _context.UserStocks.Remove(appUserStock);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return RedirectToAction(nameof(Dashboard));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting stock position {Id} for user {UserId}", id, userId);
                TempData["ErrorMessage"] = "Unable to delete stock position. Please try again later.";
                return RedirectToAction(nameof(Dashboard));
            }
        }

        private static DateTime ConvertToUtc(DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime.ToUniversalTime();
        }

        [HttpGet]
        public async Task<IActionResult> GetRealTimeUpdates()
        {
            var userId = _userManager.GetUserId(User);
            var stocks = await _context.UserStocks
                .Where(u => u.UserId == userId)
                .ToListAsync();

            var symbols = stocks.Select(s => s.StockSymbol).Distinct().ToList();
            var realTimePrices = await _stockService.GetRealTimePricesAsync(symbols);

            return Json(new { prices = realTimePrices, timestamp = DateTime.UtcNow });
        }

        [HttpGet]
        public async Task<IActionResult> SearchStocks(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1)
            {
                return Json(new { success = false, message = "Please enter at least one character." });
            }

            try
            {
                var cacheKey = $"SearchStocks_{term.Trim().ToUpper()}";
                if (_cache.TryGetValue(cacheKey, out List<StockSymbolData>? cachedStocks) && cachedStocks != null)
                {
                    return Json(new { success = true, stocks = cachedStocks });
                }

                var stocks = await _stockService.GetAllStocksAsync(term);
                if (!stocks.Any())
                {
                    return Json(new { success = false, message = "No matching stocks found." });
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(2));

                _cache.Set(cacheKey, stocks, cacheOptions);
                return Json(new { success = true, stocks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching stocks for term: {Term}", term);
                return Json(new { success = false, message = "An error occurred while searching stocks." });
            }
        }

        private Task<PortfolioMetrics> CalculatePortfolioMetrics(
    List<AppUserStock> userStocks,
    Dictionary<string, decimal> realTimePrices,
    Dictionary<string, CompanyDetails> companyDetails)
        {
            var metrics = new PortfolioMetrics();

            try
            {
                var activeStocks = userStocks.Where(s =>
                    !string.IsNullOrWhiteSpace(s.StockSymbol) &&
                    (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0).ToList();

                if (!activeStocks.Any())
                {
                    return Task.FromResult(metrics);
                }

                decimal totalValue = 0;
                decimal totalCost = 0;
                decimal totalGain = 0;
                decimal bestReturn = decimal.MinValue;
                decimal worstReturn = decimal.MaxValue;
                string? bestStock = null;
                string? worstStock = null;

                foreach (var stock in activeStocks)
                {
                    var activeShares = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);

                    // Get current price, defaulting to stored current price if real-time price is unavailable
                    var currentPrice = realTimePrices.TryGetValue(stock.StockSymbol, out decimal price) && price > 0
                        ? price
                        : stock.CurrentPrice;

                    if (currentPrice <= 0 || stock.PurchasePrice <= 0)
                    {
                        _logger.LogWarning("Invalid price data for {Symbol}: CurrentPrice={CurrentPrice}, PurchasePrice={PurchasePrice}",
                            stock.StockSymbol, currentPrice, stock.PurchasePrice);
                        continue;
                    }

                    var currentValue = activeShares * currentPrice;
                    var costBasis = activeShares * stock.PurchasePrice;

                    totalValue += currentValue;
                    totalCost += costBasis;
                    totalGain += currentValue - costBasis;

                    // Avoid division by zero by checking if costBasis is not zero
                    if (costBasis != 0)
                    {
                        var returnPercent = ((currentValue - costBasis) / costBasis) * 100;

                        if (returnPercent > bestReturn)
                        {
                            bestReturn = returnPercent;
                            bestStock = stock.StockSymbol;
                        }

                        if (returnPercent < worstReturn)
                        {
                            worstReturn = returnPercent;
                            worstStock = stock.StockSymbol;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cost basis is zero for stock {Symbol}. Skipping performance calculation.", stock.StockSymbol);
                    }
                }

                // Avoid division by zero when calculating total portfolio performance
                metrics.TotalValue = totalValue;
                metrics.TotalCost = totalCost;
                metrics.TotalGain = totalGain;
                metrics.TotalPerformance = totalCost != 0 ? (totalGain / totalCost) * 100 : 0;
                metrics.TotalStocks = activeStocks.Count;

                if (bestStock != null)
                {
                    metrics.BestPerformingStock = bestStock;
                    metrics.BestPerformingStockReturn = bestReturn;
                    metrics.WorstPerformingStock = worstStock ?? string.Empty;
                    metrics.WorstPerformingStockReturn = worstReturn;
                }

                var sectors = companyDetails.Values
                    .Where(d => !string.IsNullOrEmpty(d.Industry))
                    .Select(d => d.Industry)
                    .Distinct();

                metrics.UniqueSectors = sectors.Count();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating portfolio metrics");
            }

            return Task.FromResult(metrics);
        }

        private Dictionary<string, decimal> CalculateSectorDistributionLegacy(
        List<AppUserStock> userStocks,
        Dictionary<string, CompanyDetails> companyDetails)
        {
            try
            {
                return userStocks
                    .Where(s =>
                        !string.IsNullOrWhiteSpace(s.StockSymbol) &&
                        companyDetails.ContainsKey(s.StockSymbol) &&
                        (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0)
                    .GroupBy(s => companyDetails[s.StockSymbol].Industry ?? "Other")
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(s =>
                            (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) *
                            (s.CurrentPrice > 0 ? s.CurrentPrice : s.PurchasePrice))
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating sector distribution");
                return new Dictionary<string, decimal>();
            }
        }

        private Dictionary<string, List<PerformancePoint>> CalculatePerformanceData(
    List<AppUserStock> userStocks,
    Dictionary<string, List<StockDataPoint>> historicalData)
        {
            try
            {
                var performanceData = new Dictionary<string, List<PerformancePoint>>();

                foreach (var stock in userStocks.Where(s =>
                    !string.IsNullOrWhiteSpace(s.StockSymbol) &&
                    (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0))
                {
                    // Add some debug logging
                    _logger.LogInformation("Processing performance data for {Symbol}", stock.StockSymbol);

                    if (historicalData.TryGetValue(stock.StockSymbol, out var stockHistory) &&
                        stockHistory != null &&
                        stockHistory.Any())
                    {
                        _logger.LogInformation("Found {Count} historical points for {Symbol}", stockHistory.Count, stock.StockSymbol);

                        var points = stockHistory
                            .Where(h => h.Close > 0)
                            .Select(h => new PerformancePoint
                            {
                                Date = h.Date,
                                Value = h.Close,
                                PercentageChange = stock.PurchasePrice > 0
                                    ? ((h.Close - stock.PurchasePrice) / stock.PurchasePrice) * 100
                                    : 0
                            })
                            .Where(p => !double.IsInfinity((double)p.PercentageChange))
                            .OrderBy(p => p.Date)  // Make sure data is ordered by date
                            .ToList();

                        if (points.Any())
                        {
                            _logger.LogInformation("Added {Count} performance points for {Symbol}", points.Count, stock.StockSymbol);
                            performanceData[stock.StockSymbol] = points;
                        }
                        else
                        {
                            _logger.LogWarning("No valid performance points calculated for {Symbol}", stock.StockSymbol);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No historical data found for {Symbol}", stock.StockSymbol);
                    }
                }

                return performanceData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating performance data");
                return new Dictionary<string, List<PerformancePoint>>();
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetDailyPriceUpdates()
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                var stocks = await _context.UserStocks
                    .Where(u => u.UserId == userId &&
                               (u.NumberOfSharesPurchased - (u.NumberOfSharesSold ?? 0)) > 0)
                    .Select(s => s.StockSymbol)
                    .Distinct()
                    .ToListAsync();

                if (!stocks.Any())
                    return Json(new { prices = new Dictionary<string, decimal>() });

                var dailyPrices = await _stockService.GetRealTimePricesAsync(stocks);
                return Json(new { prices = dailyPrices, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching daily price updates");
                return StatusCode(500, "Error fetching price updates");
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetHistoricalPerformance()
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                var cacheKey = $"HistoricalPerformance_{userId}";

                Dictionary<string, List<PerformancePoint>>? performanceData = null;
                bool fromCache = _cache.TryGetValue(cacheKey, out performanceData);

                if (!fromCache)
                {
                    _logger.LogInformation("Cache miss for historical performance data, fetching from service");
                    
                    var stocks = await _context.UserStocks
                        .Where(u => u.UserId == userId &&
                                   (u.NumberOfSharesPurchased - (u.NumberOfSharesSold ?? 0)) > 0)
                        .ToListAsync();

                    if (!stocks.Any())
                    {
                        _logger.LogWarning("No stocks found for user {UserId}", userId);
                        return Json(new { performanceData = new Dictionary<string, List<PerformancePoint>>() });
                    }

                    var symbols = stocks.Select(s => s.StockSymbol).Distinct().ToList();
                    _logger.LogInformation("Fetching historical data for {Count} symbols", symbols.Count);
                    
                    var historicalDataConcurrent = await _stockService.GetRealTimeDataAsync(symbols, 1825);
                    var historicalData = new Dictionary<string, List<StockDataPoint>>(historicalDataConcurrent);

                    // Check if we got valid data
                    bool hasValidData = historicalData.Values.Any(list => list != null && list.Count > 0);
                    
                    if (!hasValidData)
                    {
                        _logger.LogWarning("No valid historical data returned from service");
                        return Json(new { performanceData = new Dictionary<string, List<PerformancePoint>>() });
                    }

                    performanceData = CalculatePerformanceData(stocks, historicalData);
                    
                    // Log what we're caching
                    foreach (var kvp in performanceData)
                    {
                        _logger.LogInformation("Symbol {Symbol}: {Count} data points", kvp.Key, kvp.Value?.Count ?? 0);
                    }

                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(4)); // Increased from minutes to hours

                    _cache.Set(cacheKey, performanceData, cacheEntryOptions);
                }
                else
                {
                    _logger.LogInformation("Using cached historical performance data for user {UserId}", userId);
                }

                return Json(new { performanceData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical performance");
                return StatusCode(500, "Error fetching performance data");
            }
        }

        [HttpGet]
        public async Task<IActionResult> CheckStockExists(string stockSymbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stockSymbol))
                {
                    return Json(new { exists = false });
                }

                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { exists = false });
                }

                var symbol = stockSymbol.Trim().ToUpper();
                var existingStock = await _context.UserStocks
                    .FirstOrDefaultAsync(s => s.UserId == userId && s.StockSymbol == symbol);

                return Json(new { 
                    exists = existingStock != null, 
                    stockId = existingStock?.Id ?? 0 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if stock exists: {Symbol}", stockSymbol);
                return Json(new { exists = false });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetHistoricalPrice(string stockSymbol, DateTime date)
        {
            try
            {
                _logger.LogInformation("Fetching historical price for symbol {Symbol} on date {Date}.", stockSymbol, date);
                var cacheKey = $"HistoricalPrice_{stockSymbol}_{date:yyyyMMdd}";

                if (!_cache.TryGetValue(cacheKey, out decimal price))
                {
                    if (date.Date >= DateTime.UtcNow.Date.AddDays(-1))
                    {
                        var dailyPrices = await GetCachedPrices(new List<string> { stockSymbol });
                        if (dailyPrices.TryGetValue(stockSymbol, out price) && price > 0)
                        {
                            _cache.Set(cacheKey, price, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                            return Json(new { success = true, price });
                        }
                    }

                    var dataConcurrent = await _stockService.GetRealTimeDataAsync(new List<string> { stockSymbol }, 1825);
                    var data = new Dictionary<string, List<StockDataPoint>>(dataConcurrent); // Convert to Dictionary

                    if (data.TryGetValue(stockSymbol, out var dataPoints) && dataPoints.Any())
                    {
                        price = dataPoints.FirstOrDefault(dp => dp.Date.Date == date.Date)?.Close ?? 0;
                        if (price > 0)
                        {
                            _cache.Set(cacheKey, price, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                            return Json(new { success = true, price });
                        }

                        _logger.LogWarning("No matching data found for symbol {Symbol} on date {Date}.", stockSymbol, date);
                        return Json(new { success = false, message = "No data found for the given date." });
                    }

                    _logger.LogWarning("No data points found for symbol {Symbol}.", stockSymbol);
                    return Json(new { success = false, message = "No data available." });
                }
                else
                {
                    return Json(new { success = true, price });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical price for {Symbol} on {Date}", stockSymbol, date);
                return Json(new { success = false, message = "An error occurred while fetching price." });
            }
        }
        
        // Helper method to preserve stock information in ViewBag when form validation fails
        private async Task PreserveStockInformation(AppUserStock appUserStock)
        {
            if (!string.IsNullOrWhiteSpace(appUserStock.StockSymbol))
            {
                try
                {
                    // Use the exact match method for more accurate results
                    var initialStock = await _stockService.GetExactStockAsync(appUserStock.StockSymbol);
                    
                    if (initialStock != null)
                    {
                        ViewBag.InitialStock = new List<object>
                        {
                            new
                            {
                                symbol = initialStock.Symbol,
                                display = $"{initialStock.Symbol} - {initialStock.Name}",
                                fullName = initialStock.Name
                            }
                        };
                        
                        _logger.LogInformation("Preserved stock information for {Symbol} in ViewBag", appUserStock.StockSymbol);
                    }
                    else
                    {
                        // Fallback: If no exact match found, still preserve the symbol
                        ViewBag.InitialStock = new List<object>
                        {
                            new
                            {
                                symbol = appUserStock.StockSymbol,
                                display = appUserStock.StockSymbol,
                                fullName = string.Empty
                            }
                        };
                        _logger.LogWarning("No stock details found for {Symbol}, using fallback", appUserStock.StockSymbol);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error preserving stock information for {Symbol}", appUserStock.StockSymbol);
                    
                    // Still provide a fallback
                    ViewBag.InitialStock = new List<object>
                    {
                        new
                        {
                            symbol = appUserStock.StockSymbol,
                            display = appUserStock.StockSymbol,
                            fullName = string.Empty
                        }
                    };
                }
            }
        }
    }
}