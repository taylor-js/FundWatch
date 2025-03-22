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
        private readonly IMemoryCache _cache;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int CACHE_DURATION_MINUTES = 15;

        public AppUserStocksController(
        ApplicationDbContext context,
        ILogger<AppUserStocksController> logger,
        UserManager<IdentityUser> userManager,
        StockService stockService,
        IMemoryCache cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
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

            var historicalData = await _stockService.GetRealTimeDataAsync(symbols, 90);
            var cachedPrices = await GetCachedPrices(symbols);
            var cachedDetails = await GetCachedCompanyDetails(symbols);

            var viewModel = new PortfolioDashboardViewModel
            {
                UserStocks = userStocks,
                PortfolioMetrics = await CalculatePortfolioMetrics(userStocks, cachedPrices, cachedDetails),
                SectorDistribution = CalculateSectorDistribution(userStocks, cachedDetails),
                PerformanceData = CalculatePerformanceData(userStocks, historicalData),
                HistoricalData = historicalData, // Populate HistoricalData here
                CompanyDetails = cachedDetails
            };

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
                var cacheKey = $"RealTimePrice_{symbol}";
                if (_cache.TryGetValue(cacheKey, out decimal price))
                {
                    cachedPrices[symbol] = price;
                }
                else
                {
                    symbolsToFetch.Add(symbol);
                }
            }

            if (symbolsToFetch.Any())
            {
                var freshPrices = await _stockService.GetRealTimePricesAsync(symbolsToFetch);
                foreach (var kvp in freshPrices)
                {
                    var cacheKey = $"RealTimePrice_{kvp.Key}";
                    _cache.Set(cacheKey, kvp.Value, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
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
                if (_cache.TryGetValue($"Details_{symbol}", out CompanyDetails details))
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

        private async Task<Dictionary<string, List<StockDataPoint>>> GetCachedHistoricalData(List<string> symbols)
        {
            var result = new Dictionary<string, List<StockDataPoint>>();
            var uncachedSymbols = new List<string>();

            foreach (var symbol in symbols)
            {
                if (_cache.TryGetValue($"History_{symbol}", out List<StockDataPoint> history))
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
                var history = await _stockService.GetRealTimeDataAsync(uncachedSymbols, 90);
                foreach (var (symbol, data) in history)
                {
                    result[symbol] = data;
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
                return NotFound();

            var userId = _userManager.GetUserId(User);
            var stock = await _context.UserStocks
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

            if (stock == null)
                return NotFound();

            var companyDetails = await _stockService.GetCompanyDetailsAsync(new List<string> { stock.StockSymbol });
            var historicalData = await _stockService.GetRealTimeDataAsync(new List<string> { stock.StockSymbol }, 365);

            var viewModel = new Models.ViewModels.StockDetailsViewModel
            {
                Stock = stock,
                CompanyDetails = companyDetails[stock.StockSymbol],
                HistoricalData = historicalData[stock.StockSymbol]
            };

            return View(viewModel);
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

                var stockDetails = await _stockService.GetAllStocksAsync(appUserStock.StockSymbol);
                var initialStock = stockDetails.FirstOrDefault();

                if (initialStock != null)
                {
                    ViewBag.InitialStock = new List<object>
                {
                    new
                    {
                        symbol = initialStock.Symbol,
                        display = $"{initialStock.Symbol} - {initialStock.Name}"
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

            if (appUserStock.PurchasePrice <= 0)
            {
                ModelState.AddModelError("PurchasePrice", "Purchase price must be greater than zero");
                return View(appUserStock);
            }

            if (appUserStock.NumberOfSharesPurchased <= 0)
            {
                ModelState.AddModelError("NumberOfSharesPurchased", "Number of shares must be greater than zero");
                return View(appUserStock);
            }

            if (appUserStock.DateSold.HasValue)
            {
                if (appUserStock.DateSold.HasValue &&
                    appUserStock.DateSold.Value < appUserStock.DatePurchased)
                {
                    ModelState.AddModelError("DateSold", "Sale date cannot be before purchase date");
                    return View(appUserStock);
                }


                if (appUserStock.NumberOfSharesSold.HasValue &&
                    appUserStock.NumberOfSharesSold.Value > appUserStock.NumberOfSharesPurchased)
                {
                    ModelState.AddModelError("NumberOfSharesSold", "Cannot sell more shares than purchased");
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
                if (_cache.TryGetValue(cacheKey, out List<StockSymbolData> cachedStocks))
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

        private async Task<PortfolioMetrics> CalculatePortfolioMetrics(
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
                    return metrics;
                }

                decimal totalValue = 0;
                decimal totalCost = 0;
                decimal totalGain = 0;
                decimal bestReturn = decimal.MinValue;
                decimal worstReturn = decimal.MaxValue;
                string bestStock = null;
                string worstStock = null;

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
                    metrics.WorstPerformingStock = worstStock;
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

            return metrics;
        }

        private Dictionary<string, decimal> CalculateSectorDistribution(
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
                    _logger.LogInformation($"Processing performance data for {stock.StockSymbol}");

                    if (historicalData.TryGetValue(stock.StockSymbol, out var stockHistory) &&
                        stockHistory != null &&
                        stockHistory.Any())
                    {
                        _logger.LogInformation($"Found {stockHistory.Count} historical points for {stock.StockSymbol}");

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
                            _logger.LogInformation($"Added {points.Count} performance points for {stock.StockSymbol}");
                            performanceData[stock.StockSymbol] = points;
                        }
                        else
                        {
                            _logger.LogWarning($"No valid performance points calculated for {stock.StockSymbol}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"No historical data found for {stock.StockSymbol}");
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

                if (!_cache.TryGetValue(cacheKey, out Dictionary<string, List<PerformancePoint>> performanceData))
                {
                    var stocks = await _context.UserStocks
                        .Where(u => u.UserId == userId &&
                                   (u.NumberOfSharesPurchased - (u.NumberOfSharesSold ?? 0)) > 0)
                        .ToListAsync();

                    if (!stocks.Any())
                        return Json(new { performanceData = new Dictionary<string, List<PerformancePoint>>() });

                    var symbols = stocks.Select(s => s.StockSymbol).Distinct().ToList();
                    var historicalData = await _stockService.GetRealTimeDataAsync(symbols, 90);
                    performanceData = CalculatePerformanceData(stocks, historicalData);

                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                    _cache.Set(cacheKey, performanceData, cacheEntryOptions);
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

                    var data = await _stockService.GetRealTimeDataAsync(new List<string> { stockSymbol }, 120);
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
    }
}