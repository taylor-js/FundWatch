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
        private const int CACHE_DURATION_MINUTES = 15;

        public AppUserStocksController(
            ApplicationDbContext context,
            ILogger<AppUserStocksController> logger,
            UserManager<IdentityUser> userManager,
            StockService stockService,
            IMemoryCache cache)
        {
            _context = context;
            _logger = logger;
            _userManager = userManager;
            _stockService = stockService;
            _cache = cache;
        }

        // GET: AppUserStocks/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var viewModel = await PrepareDashboardViewModel(userId);
            return View(viewModel);
        }

        private async Task<PortfolioDashboardViewModel> PrepareDashboardViewModel(string userId)
        {
            try
            {
                var userStocks = await _context.UserStocks
                    .Where(u => u.UserId == userId)
                    .ToListAsync();

                _logger.LogInformation($"Found {userStocks.Count} stocks for user {userId}");

                if (!userStocks.Any())
                {
                    _logger.LogInformation($"No stocks found for user {userId}");
                    return new PortfolioDashboardViewModel
                    {
                        UserStocks = new List<AppUserStock>(),
                        PortfolioMetrics = new PortfolioMetrics(),
                        SectorDistribution = new Dictionary<string, decimal>(),
                        PerformanceData = new Dictionary<string, List<PerformancePoint>>(),
                        CompanyDetails = new Dictionary<string, CompanyDetails>()
                    };
                }

                var stockSymbols = userStocks.Select(s => s.StockSymbol).Distinct().ToList();
                _logger.LogInformation($"Fetching data for symbols: {string.Join(", ", stockSymbols)}");

                var realTimePricesTask = _stockService.GetRealTimePricesAsync(stockSymbols);
                var companyDetailsTask = _stockService.GetCompanyDetailsAsync(stockSymbols);
                var historicalDataTask = _stockService.GetRealTimeDataAsync(stockSymbols, 1825);

                await Task.WhenAll(realTimePricesTask, companyDetailsTask, historicalDataTask);

                var realTimePrices = await realTimePricesTask;
                var companyDetails = await companyDetailsTask;
                var historicalData = await historicalDataTask;

                _logger.LogInformation($"Retrieved {realTimePrices.Count} real-time prices");
                _logger.LogInformation($"Retrieved {companyDetails.Count} company details");

                // Create a list to track stocks that need updating
                var stocksToUpdate = new List<AppUserStock>();

                foreach (var stock in userStocks)
                {
                    if (realTimePrices.TryGetValue(stock.StockSymbol, out decimal currentPrice))
                    {
                        if (currentPrice > 0 && stock.CurrentPrice != currentPrice)  // Only update if price is different
                        {
                            stock.CurrentPrice = currentPrice;
                            stocksToUpdate.Add(stock);
                            _logger.LogInformation(
                                "Updated {Symbol}: Price={NewPrice}, Shares={Shares}, Value={Value}",
                                stock.StockSymbol,
                                currentPrice,
                                stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0),
                                currentPrice * (stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0))
                            );
                        }
                    }
                    else if (historicalData.TryGetValue(stock.StockSymbol, out var dataPoints) && dataPoints.Any())
                    {
                        // Fallback to most recent historical price if real-time is not available
                        var latestPrice = dataPoints.OrderByDescending(d => d.Date).First().Close;
                        if (latestPrice > 0 && stock.CurrentPrice != latestPrice)
                        {
                            stock.CurrentPrice = latestPrice;
                            stocksToUpdate.Add(stock);
                        }
                    }
                }

                // Save all updates in a single transaction
                if (stocksToUpdate.Any())
                {
                    await _context.SaveChangesAsync();
                }

                // Calculate metrics
                var portfolioMetrics = CalculatePortfolioMetrics(userStocks, realTimePrices, companyDetails);

                _logger.LogInformation(
                    "Portfolio Metrics Calculated: Value=${Value}, Gain=${Gain}, Performance={Performance}%, Sectors={Sectors}, Best={Best}({BestReturn}%), Worst={Worst}({WorstReturn}%)",
                    portfolioMetrics.TotalValue,
                    portfolioMetrics.TotalGain,
                    portfolioMetrics.TotalPerformance,
                    portfolioMetrics.UniqueSectors,
                    portfolioMetrics.BestPerformingStock,
                    portfolioMetrics.BestPerformingStockReturn,
                    portfolioMetrics.WorstPerformingStock,
                    portfolioMetrics.WorstPerformingStockReturn
                );

                // Calculate sector distribution
                var sectorDistribution = CalculateSectorDistribution(userStocks, companyDetails);

                // Calculate performance data
                var performanceData = CalculatePerformanceData(userStocks, historicalData);

                return new PortfolioDashboardViewModel
                {
                    UserStocks = userStocks,
                    PortfolioMetrics = portfolioMetrics,
                    SectorDistribution = sectorDistribution,
                    PerformanceData = performanceData,
                    CompanyDetails = companyDetails
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing dashboard data for user {UserId}", userId);
                throw;
            }
        }


        // GET: AppUserStocks
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var stocks = await _context.UserStocks
                .Where(u => u.UserId == userId)
                .ToListAsync();

            var symbols = stocks.Select(s => s.StockSymbol).ToList();
            var companyDetails = await _stockService.GetCompanyDetailsAsync(symbols);

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
                    _logger.LogWarning("Attempted to access CreateOrEdit without being logged in");
                    return RedirectToAction("Login", "Account");
                }

                if (id == 0)
                {
                    // Creating new stock position
                    var appUserStock = new AppUserStock
                    {
                        UserId = userId,
                        DatePurchased = DateTime.UtcNow.Date
                    };
                    return View(appUserStock);
                }
                else
                {
                    // Editing existing stock position
                    var appUserStock = await _context.UserStocks
                        .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

                    if (appUserStock == null)
                    {
                        _logger.LogWarning("User {UserId} attempted to edit non-existent or unauthorized stock {StockId}",
                            userId, id);
                        return NotFound();
                    }

                    // Get the stock details for the dropdown's initial value
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateOrEdit GET action for ID: {Id}", id);
                throw;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrEdit([Bind("Id,UserId,StockSymbol,PurchasePrice,DatePurchased,NumberOfSharesPurchased,CurrentPrice,DateSold,NumberOfSharesSold")] AppUserStock appUserStock)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(appUserStock);
                }

                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    ModelState.AddModelError("", "User ID is missing. Please log in again.");
                    return View(appUserStock);
                }

                // Validate dates
                if (appUserStock.DateSold.HasValue && appUserStock.DatePurchased.HasValue &&
                    appUserStock.DateSold.Value < appUserStock.DatePurchased.Value)
                {
                    ModelState.AddModelError("DateSold", "Sale date cannot be before purchase date");
                    return View(appUserStock);
                }

                // Convert dates to UTC
                appUserStock.DatePurchased = appUserStock.DatePurchased.HasValue
                    ? ConvertToUtc(appUserStock.DatePurchased.Value)
                    : null;
                appUserStock.DateSold = appUserStock.DateSold.HasValue
                    ? ConvertToUtc(appUserStock.DateSold.Value)
                    : null;

                if (appUserStock.Id == 0)
                {
                    // Create new stock position
                    appUserStock.UserId = userId;
                    appUserStock.StockSymbol = appUserStock.StockSymbol.ToUpper();

                    // Get current price
                    var currentPrices = await _stockService.GetRealTimePricesAsync(new List<string> { appUserStock.StockSymbol });
                    if (currentPrices.TryGetValue(appUserStock.StockSymbol, out decimal currentPrice))
                    {
                        appUserStock.CurrentPrice = currentPrice;
                    }

                    _context.Add(appUserStock);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("New stock position created for {Symbol} by user {UserId}",
                        appUserStock.StockSymbol, userId);
                }
                else
                {
                    // Update existing stock position
                    var existingStock = await _context.UserStocks
                        .FirstOrDefaultAsync(s => s.Id == appUserStock.Id && s.UserId == userId);

                    if (existingStock == null)
                    {
                        return NotFound();
                    }

                    // Update properties
                    existingStock.StockSymbol = appUserStock.StockSymbol.ToUpper();
                    existingStock.PurchasePrice = appUserStock.PurchasePrice;
                    existingStock.DatePurchased = appUserStock.DatePurchased;
                    existingStock.NumberOfSharesPurchased = appUserStock.NumberOfSharesPurchased;
                    existingStock.DateSold = appUserStock.DateSold;
                    existingStock.NumberOfSharesSold = appUserStock.NumberOfSharesSold;

                    // Update current price
                    var currentPrices = await _stockService.GetRealTimePricesAsync(new List<string> { existingStock.StockSymbol });
                    if (currentPrices.TryGetValue(existingStock.StockSymbol, out decimal currentPrice))
                    {
                        existingStock.CurrentPrice = currentPrice;
                    }

                    _context.Update(existingStock);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Stock position {Id} updated by user {UserId}",
                        existingStock.Id, userId);
                }

                return RedirectToAction(nameof(Dashboard));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving stock position for user {UserId}", appUserStock.UserId);
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

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                var appUserStock = await _context.UserStocks
                    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

                if (appUserStock != null)
                {
                    _context.UserStocks.Remove(appUserStock);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Stock position {Id} deleted by user {UserId}", id, userId);
                }

                return RedirectToAction(nameof(Dashboard));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting stock position {Id}", id);
                throw;
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
        public async Task<IActionResult> SearchStocks(string term = "", int limit = 50, int offset = 0)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1)
            {
                return Json(new { success = false, message = "Please enter at least one character." });
            }

            try
            {
                // Check cache first
                var cacheKey = $"SearchStocks_{term}";
                if (_cache.TryGetValue(cacheKey, out List<StockSymbolData> cachedStocks))
                {
                    _logger.LogInformation($"Returning cached stocks for term: {term}");
                    return Json(new { success = true, stocks = cachedStocks });
                }

                var stocks = await _stockService.GetAllStocksAsync(term);

                if (!stocks.Any())
                {
                    return Json(new { success = false, message = "No matching stocks found." });
                }

                // Cache the results
                _cache.Set(cacheKey, stocks, TimeSpan.FromMinutes(5));

                // Return the stocks
                return Json(new { success = true, stocks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching stocks for term: {Term}", term);
                return Json(new { success = false, message = "An error occurred while searching stocks." });
            }
        }




        private PortfolioMetrics CalculatePortfolioMetrics(
    List<AppUserStock> userStocks,
    Dictionary<string, decimal> realTimePrices,
    Dictionary<string, CompanyDetails> companyDetails)
        {
            var metrics = new PortfolioMetrics();
            _logger.LogInformation("Starting portfolio metrics calculation");

            // Filter to only active stocks with valid prices
            var activeStocks = userStocks.Where(s =>
                (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0 &&
                s.CurrentPrice > 0).ToList();

            if (!activeStocks.Any())
            {
                _logger.LogInformation("No active stocks found with valid prices");
                return metrics;
            }

            decimal totalValue = 0;
            decimal totalCost = 0;
            decimal bestReturn = decimal.MinValue;
            decimal worstReturn = decimal.MaxValue;
            string bestStock = null;
            string worstStock = null;

            foreach (var stock in activeStocks)
            {
                var activeShares = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);
                var currentPrice = realTimePrices.TryGetValue(stock.StockSymbol, out decimal price) && price > 0
                    ? price
                    : stock.CurrentPrice;

                _logger.LogInformation(
                    "Processing {Symbol}: Shares={Shares}, CurrentPrice=${Price}, PurchasePrice=${PurchasePrice}",
                    stock.StockSymbol,
                    activeShares,
                    currentPrice,
                    stock.PurchasePrice);

                var currentValue = activeShares * currentPrice;
                var costBasis = activeShares * stock.PurchasePrice;

                if (costBasis <= 0)
                {
                    _logger.LogWarning("Skipping {Symbol} - invalid cost basis", stock.StockSymbol);
                    continue;
                }

                totalValue += currentValue;
                totalCost += costBasis;

                var returnPercent = ((currentValue - costBasis) / costBasis) * 100;

                _logger.LogInformation(
                    "{Symbol}: CurrentValue=${CurrentValue}, CostBasis=${CostBasis}, Return={Return}%",
                    stock.StockSymbol,
                    currentValue,
                    costBasis,
                    returnPercent);

                // Update best performer
                if (returnPercent > bestReturn)
                {
                    bestReturn = returnPercent;
                    bestStock = stock.StockSymbol;
                }

                // Update worst performer
                if (returnPercent < worstReturn)
                {
                    worstReturn = returnPercent;
                    worstStock = stock.StockSymbol;
                }
            }

            // Set the calculated values
            metrics.TotalValue = totalValue;
            metrics.TotalCost = totalCost;
            metrics.TotalGain = totalValue - totalCost;
            metrics.TotalPerformance = totalCost > 0 ? (metrics.TotalGain / totalCost) * 100 : 0;

            // Set the performance metrics
            if (bestStock != null)
            {
                metrics.BestPerformingStock = bestStock;
                metrics.BestPerformingStockReturn = bestReturn;
                metrics.WorstPerformingStock = worstStock;
                metrics.WorstPerformingStockReturn = worstReturn;
            }
            else
            {
                _logger.LogWarning("No valid performance metrics calculated");
                metrics.BestPerformingStock = "N/A";
                metrics.WorstPerformingStock = "N/A";
                metrics.BestPerformingStockReturn = 0;
                metrics.WorstPerformingStockReturn = 0;
            }

            // Calculate stock counts
            metrics.TotalStocks = activeStocks.Count;

            // Calculate sector diversity
            var sectors = companyDetails.Values
                .Where(d => !string.IsNullOrEmpty(d.Industry))
                .Select(d => d.Industry)
                .Distinct()
                .ToList();

            metrics.UniqueSectors = sectors.Count;

            _logger.LogInformation(
                "Final Portfolio Metrics:\n" +
                "Total Value: ${TotalValue}\n" +
                "Total Cost: ${TotalCost}\n" +
                "Total Gain: ${TotalGain}\n" +
                "Total Performance: {TotalPerformance}%\n" +
                "Best Performer: {Best} ({BestReturn}%)\n" +
                "Worst Performer: {Worst} ({WorstReturn}%)\n" +
                "Active Stocks: {StockCount}\n" +
                "Unique Sectors: {SectorCount}",
                totalValue,
                totalCost,
                metrics.TotalGain,
                metrics.TotalPerformance,
                metrics.BestPerformingStock,
                metrics.BestPerformingStockReturn,
                metrics.WorstPerformingStock,
                metrics.WorstPerformingStockReturn,
                metrics.TotalStocks,
                metrics.UniqueSectors);

            return metrics;
        }

        private Dictionary<string, decimal> CalculateSectorDistribution(
            List<AppUserStock> userStocks,
            Dictionary<string, CompanyDetails> companyDetails)
        {
            return userStocks
                .Where(s => companyDetails.ContainsKey(s.StockSymbol))
                .GroupBy(s => companyDetails[s.StockSymbol].Industry ?? "Unknown")
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(s =>
                        (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) * s.CurrentPrice)
                );
        }

        private Dictionary<string, List<PerformancePoint>> CalculatePerformanceData(
    List<AppUserStock> userStocks,
    Dictionary<string, List<StockDataPoint>> historicalData)
        {
            var performanceData = new Dictionary<string, List<PerformancePoint>>();

            // Guard clause for empty datasets
            if (!userStocks.Any() || !historicalData.Any())
            {
                return performanceData;
            }

            foreach (var stock in userStocks.Where(s =>
                (s.NumberOfSharesPurchased - (s.NumberOfSharesSold ?? 0)) > 0)) // Only include stocks with active shares
            {
                if (historicalData.TryGetValue(stock.StockSymbol, out var stockHistory) &&
                    stockHistory != null &&
                    stockHistory.Any())
                {
                    var points = stockHistory
                        .Where(h => h.Close > 0) // Only include points with valid prices
                        .Select(h => new PerformancePoint
                        {
                            Date = h.Date,
                            Value = h.Close,
                            PercentageChange = ((h.Close - stock.PurchasePrice) / stock.PurchasePrice) * 100
                        })
                        .ToList();

                    if (points.Any()) // Only add if we have valid points
                    {
                        performanceData[stock.StockSymbol] = points;
                    }
                }
            }

            return performanceData;
        }
        [HttpGet]
        public async Task<IActionResult> GetHistoricalPrice(string stockSymbol, DateTime date)
        {
            try
            {
                _logger.LogInformation("Fetching historical price for symbol {Symbol} on date {Date}.", stockSymbol, date);

                // If the date is today, try to get real-time price first
                if (date.Date == DateTime.UtcNow.Date)
                {
                    var realTimePrices = await _stockService.GetRealTimePricesAsync(new List<string> { stockSymbol });
                    if (realTimePrices.TryGetValue(stockSymbol, out decimal realTimePrice) && realTimePrice > 0)
                    {
                        return Json(new { success = true, price = realTimePrice });
                    }
                }

                var data = await _stockService.GetRealTimeDataAsync(new List<string> { stockSymbol }, 1825);
                if (data.TryGetValue(stockSymbol, out var dataPoints) && dataPoints.Any())
                {
                    // Handle date mismatch due to timezone
                    var price = dataPoints.FirstOrDefault(dp => dp.Date.Date == date.Date)?.Close ?? 0;

                    if (price > 0)
                    {
                        return Json(new { success = true, price });
                    }

                    _logger.LogWarning("No matching data found for symbol {Symbol} on date {Date}.", stockSymbol, date);
                    return Json(new { success = false, message = "No data found for the given date." });
                }

                _logger.LogWarning("No data points found for symbol {Symbol}.", stockSymbol);
                return Json(new { success = false, message = "No data available." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical price for {Symbol} on {Date}", stockSymbol, date);
                return Json(new { success = false, message = "An error occurred while fetching price." });
            }
        }
    }
}