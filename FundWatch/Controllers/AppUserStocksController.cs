using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using FundWatch.Models;
using System.Net.Http;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System;
using Microsoft.AspNetCore.Authorization;

namespace FundWatch.Controllers
{
    [Authorize]
    public class AppUserStocksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AppUserStocksController> _logger;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly StockService _stockService;

        public AppUserStocksController(ApplicationDbContext context, ILogger<AppUserStocksController> logger, UserManager<IdentityUser> userManager, StockService stockService)
        {
            _context = context;
            _logger = logger;
            _userManager = userManager;
            _stockService = stockService;
        }

        // GET: AppUserStocks
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var applicationDbContext = _context.UserStocks.Where(u => u.UserId == userId);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: AppUserStocks/Details/5
        public async Task<IActionResult> Details(int? id)
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
                return NotFound();
            }

            return View(appUserStock);
        }

        // GET: AppUserStocks/CreateOrEdit/5
        [HttpGet]
        public async Task<IActionResult> CreateOrEdit(int id = 0)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Fetch stock symbols from the StockService
            var stockSymbols = await _stockService.GetAllStocksAsync();
            ViewBag.StockSymbols = stockSymbols;

            if (id == 0)
            {
                var appUserStock = new AppUserStock
                {
                    UserId = userId ?? string.Empty,
                };
                return View(appUserStock);
            }
            else
            {
                var appUserStock = await _context.UserStocks.FindAsync(id);
                if (appUserStock == null || appUserStock.UserId != userId)
                {
                    return NotFound();
                }
                return View(appUserStock);
            }
        }


        // POST: AppUserStocks/CreateOrEdit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOrEdit([Bind("Id,UserId,StockSymbol,PurchasePrice,DatePurchased,NumberOfSharesPurchased,CurrentPrice,DateSold,NumberOfSharesSold")] AppUserStock appUserStock)
        {
            if (ModelState.IsValid)
            {
                var userId = _userManager.GetUserId(User);
                if (!string.IsNullOrEmpty(userId))
                {
                    // Convert DateTime fields to UTC
                    appUserStock.DatePurchased = ConvertToUtc(appUserStock.DatePurchased);
                    if (appUserStock.DateSold.HasValue)
                    {
                        appUserStock.DateSold = ConvertToUtc(appUserStock.DateSold.Value);
                    }

                    if (appUserStock.Id == 0)
                    {
                        appUserStock.UserId = userId;
                        _context.Add(appUserStock);
                    }
                    else
                    {
                        var existingStock = await _context.UserStocks.FindAsync(appUserStock.Id);
                        if (existingStock == null || existingStock.UserId != userId)
                        {
                            return NotFound();
                        }

                        existingStock.StockSymbol = appUserStock.StockSymbol;
                        existingStock.PurchasePrice = appUserStock.PurchasePrice;
                        existingStock.DatePurchased = appUserStock.DatePurchased;
                        existingStock.NumberOfSharesPurchased = appUserStock.NumberOfSharesPurchased;
                        existingStock.CurrentPrice = appUserStock.CurrentPrice;
                        existingStock.DateSold = appUserStock.DateSold;
                        existingStock.NumberOfSharesSold = appUserStock.NumberOfSharesSold;

                        _context.Update(existingStock);
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("AppUserStock with ID {Id} has been created or updated", appUserStock.Id);
                    return RedirectToAction(nameof(Dashboard));
                }
                else
                {
                    ModelState.AddModelError("", "User ID is missing. Please log in again.");
                }
            }

            return View(appUserStock);
        }

        public DateTime ConvertToUtc(DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime.ToUniversalTime();
        }

        // GET: AppUserStocks/Delete/5
        public async Task<IActionResult> Delete(int? id)
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
                return NotFound();
            }

            return View(appUserStock);
        }

        // POST: AppUserStocks/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var appUserStock = await _context.UserStocks.FindAsync(id);
            if (appUserStock != null && appUserStock.UserId == userId)
            {
                _context.UserStocks.Remove(appUserStock);
                await _context.SaveChangesAsync();
                _logger.LogInformation("AppUserStock with ID {Id} deleted", id);
            }

            return RedirectToAction(nameof(Dashboard));
        }

        private bool AppUserStockExists(int id)
        {
            return _context.UserStocks.Any(e => e.Id == id);
        }

        // GET: AppUserStocks/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("User is not logged in.");
                return RedirectToAction("Login", "Account");
            }

            var userStocks = await _context.UserStocks.Where(u => u.UserId == userId).ToListAsync();
            if (!userStocks.Any())
            {
                _logger.LogWarning($"No stocks found for user {userId}");
                return View();
            }

            var monthlyTrendData = new List<MonthlyTrendData>();
            var stockSummary = new List<StockSummaryData>();
            var bubbleChartData = new List<BubbleChartData>();

            foreach (var stock in userStocks)
            {
                try
                {
                    var realTimePrice = await _stockService.GetRealTimePriceAsync(stock.StockSymbol);
                    if (!realTimePrice.HasValue)
                    {
                        _logger.LogWarning($"No real-time price available for {stock.StockSymbol}");
                        continue;
                    }

                    stock.CurrentPrice = realTimePrice.Value;
                    var currentShares = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);
                    var currentValue = stock.TotalValue;

                    if (currentShares > 0)
                    {
                        // Use NotMapped properties for calculations
                        stockSummary.Add(new StockSummaryData
                        {
                            StockSymbol = stock.StockSymbol,
                            CurrentPrice = stock.CurrentPrice,
                            PurchasePrice = stock.PurchasePrice,
                            TotalShares = currentShares,
                            TotalValue = stock.TotalValue,
                            PerformancePercentage = stock.PerformancePercentage,
                            ValueChange = stock.ValueChange
                        });

                        bubbleChartData.Add(new BubbleChartData
                        {
                            StockSymbol = stock.StockSymbol,
                            CurrentPrice = stock.CurrentPrice,
                            TotalValue = stock.TotalValue,
                            Size = currentShares
                        });

                        // Fetch historical prices and process for monthly trend
                        var historicalPrices = await _stockService.GetHistoricalPricesAsync(stock.StockSymbol, stock.DatePurchased);
                        if (historicalPrices != null)
                        {
                            // Group by month and calculate average value per month
                            var groupedByMonth = historicalPrices
                                .GroupBy(p => p.Key.ToString("yyyy-MM"))
                                .Select(g => new MonthlyTrendData
                                {
                                    StockSymbol = stock.StockSymbol,
                                    Month = g.Key,
                                    TotalValue = g.Average(p => p.Value * currentShares) // Average of close price * shares
                                });

                            monthlyTrendData.AddRange(groupedByMonth);
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid data for {stock.StockSymbol}: CurrentShares={currentShares}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"An error occurred while processing stock: {stock.StockSymbol}");
                }
            }

            // Order monthly trend data by Month
            var orderedMonthlyTrendData = monthlyTrendData.OrderBy(m => m.Month).ToList();

            _logger.LogInformation($"Total monthly trend data points: {orderedMonthlyTrendData.Count}");
            _logger.LogInformation($"Total stock summary entries: {stockSummary.Count}");
            _logger.LogInformation($"Total bubble chart data points: {bubbleChartData.Count}");

            // Assign data to ViewBag for use in the view
            ViewBag.MonthlyTrendData = orderedMonthlyTrendData;
            ViewBag.StockSummary = stockSummary;
            ViewBag.BubbleChartData = bubbleChartData;

            return View(userStocks);
        }


        public class StockSummaryData
        {
            public string StockSymbol { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal PurchasePrice { get; set; }
            public int TotalShares { get; set; }
            public decimal TotalValue { get; set; }
            public decimal PerformancePercentage { get; set; }
            public decimal ValueChange { get; set; }

        }

        public class MonthlyTrendData
        {
            public string StockSymbol { get; set; }
            public string Month { get; set; }
            public decimal TotalValue { get; set; }
        }

        public class BubbleChartData
        {
            public string StockSymbol { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal TotalValue { get; set; }
            public int Size { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetHistoricalPrice(string stockSymbol, DateTime datePurchased)
        {
            if (string.IsNullOrEmpty(stockSymbol))
            {
                return BadRequest("Stock symbol is required.");
            }

            try
            {
                var historicalPrices = await _stockService.GetHistoricalPricesAsync(stockSymbol, datePurchased);

                if (historicalPrices != null && historicalPrices.Any())
                {
                    var closestDate = historicalPrices.Keys
                                        .OrderBy(d => Math.Abs((d - datePurchased).TotalDays))
                                        .First();
                    var price = historicalPrices[closestDate];

                    return Json(new { success = true, price });
                }

                return Json(new { success = false, message = "Price not found for the selected date." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical price for {StockSymbol}", stockSymbol);
                return Json(new { success = false, message = "An error occurred while fetching the price." });
            }
        }

    }

    public class StockService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private const string ApiKey = "0d6b96123dmshd1cdf284f3c5deap10eec8jsnc171c25e1d53"; // TODO: Replace with your actual RapidAPI key
        private const string BaseUrl = "https://yahoo-finance15.p.rapidapi.com/api/v1";

        public StockService(HttpClient httpClient, ILogger<StockService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", ApiKey);
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "yahoo-finance15.p.rapidapi.com");
        }

        public async Task<decimal?> GetRealTimePriceAsync(string stockSymbol)
        {
            if (string.IsNullOrEmpty(stockSymbol))
            {
                throw new ArgumentException("Stock symbol cannot be null or empty", nameof(stockSymbol));
            }

            var url = $"{BaseUrl}/markets/stock/quotes?ticker={stockSymbol}";

            try
            {
                _logger.LogInformation($"Fetching real-time price for {stockSymbol}");
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Raw JSON response for real-time price of {stockSymbol}: {json}");

                var data = JObject.Parse(json);
                var bodyArray = data["body"] as JArray;

                if (bodyArray != null && bodyArray.Count > 0)
                {
                    var firstItem = bodyArray.FirstOrDefault();
                    if (firstItem != null)
                    {
                        var currentPriceToken = firstItem["regularMarketPrice"];
                        if (currentPriceToken != null && decimal.TryParse(currentPriceToken.ToString(), out decimal price))
                        {
                            _logger.LogInformation($"Successfully fetched real-time price for {stockSymbol}: {price}");
                            return price;
                        }
                        else
                        {
                            _logger.LogWarning($"real-time price for {stockSymbol} is either null or cannot be parsed.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"No items found in 'body' array for {stockSymbol}");
                    }
                }
                else
                {
                    _logger.LogWarning($"'body' array not found or empty in the JSON response for {stockSymbol}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Failed to fetch real-time price for {stockSymbol}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error while fetching real-time price for {stockSymbol}");
            }

            return null;
        }


        public async Task<Dictionary<DateTime, decimal>> GetHistoricalPricesAsync(string stockSymbol, DateTime startDate)
        {
            if (string.IsNullOrEmpty(stockSymbol))
            {
                throw new ArgumentException("Stock symbol cannot be null or empty", nameof(stockSymbol));
            }

            var endDate = DateTime.UtcNow;
            var startTimestamp = ((DateTimeOffset)startDate).ToUnixTimeSeconds();
            var endTimestamp = ((DateTimeOffset)endDate).ToUnixTimeSeconds();
            var url = $"{BaseUrl}/markets/stock/history?symbol={stockSymbol}&interval=1d&diffandsplits=false&from={startTimestamp}&to={endTimestamp}";

            var historicalPrices = new Dictionary<DateTime, decimal>();

            try
            {
                _logger.LogInformation($"Fetching historical prices for {stockSymbol} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Raw JSON response for {stockSymbol}: {json}");

                var data = JObject.Parse(json);
                var body = data["body"]?.ToObject<Dictionary<string, JObject>>();

                if (body != null && body.Any())
                {
                    foreach (var entry in body)
                    {
                        if (entry.Value != null)
                        {
                            // Parse the Unix timestamp from the key
                            if (long.TryParse(entry.Key, out long timestamp))
                            {
                                var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

                                // Use the "close" price as the representative price
                                if (decimal.TryParse(entry.Value["close"]?.ToString(), out decimal closePrice))
                                {
                                    historicalPrices[date] = closePrice;
                                }
                            }
                        }
                    }

                    if (historicalPrices.Count > 0)
                    {
                        _logger.LogInformation($"Successfully fetched {historicalPrices.Count} historical prices for {stockSymbol}");
                    }
                    else
                    {
                        _logger.LogWarning($"No historical prices found in the parsed data for {stockSymbol}");
                    }
                }
                else
                {
                    _logger.LogWarning($"No historical prices found in the response data for {stockSymbol}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Failed to fetch historical prices for {stockSymbol}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error while fetching historical prices for {stockSymbol}");
            }

            return historicalPrices;
        }
        public async Task<List<StockSymbolData>> GetAllStocksAsync()
        {
            var allStocks = new List<StockSymbolData>();
            var url = $"{BaseUrl}/markets/stock/market-status";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                var stocksArray = data["body"]?["stockMarketList"] as JArray;

                if (stocksArray != null)
                {
                    foreach (var stock in stocksArray)
                    {
                        allStocks.Add(new StockSymbolData
                        {
                            Symbol = stock["ticker"]?.ToString() ?? string.Empty,
                            Name = stock["name"]?.ToString() ?? string.Empty
                        });
                    }
                }
                _logger.LogInformation($"Successfully fetched {allStocks.Count} stocks");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all stocks");
            }

            return allStocks;
        }

        public class StockSymbolData
        {
            public string Symbol { get; set; }
            public string Name { get; set; }
        }
    }


}
