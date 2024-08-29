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
using Microsoft.Extensions.Caching.Memory;

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
            var allStocks = await _stockService.GetAllStocksAsync(""); // Pass an empty string or a default term if needed
            ViewBag.StockSymbols = allStocks;
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
                    if (appUserStock.DatePurchased.HasValue)
                    {
                        appUserStock.DatePurchased = ConvertToUtc(appUserStock.DatePurchased.Value);
                    }
                    if (appUserStock.DateSold.HasValue)
                    {
                        appUserStock.DateSold = ConvertToUtc(appUserStock.DateSold.Value);
                    }

                    if (appUserStock.Id == 0)
                    {
                        appUserStock.UserId = userId;
                        appUserStock.StockSymbol = appUserStock.StockSymbol.ToUpper();
                        _context.Add(appUserStock);
                    }
                    else
                    {
                        var existingStock = await _context.UserStocks.FindAsync(appUserStock.Id);
                        if (existingStock == null || existingStock.UserId != userId)
                        {
                            return NotFound();
                        }

                        existingStock.StockSymbol = appUserStock.StockSymbol.ToUpper();
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

        [HttpGet]
        public async Task<IActionResult> SearchStocks(string term)
        {
            if (string.IsNullOrEmpty(term))
                return Json(new List<string>());

            var filteredStocks = await _stockService.GetAllStocksAsync(term);
            var limitedStocks = filteredStocks.Take(10).Select(s => s.Symbol).ToList();
            return Json(limitedStocks);
        }

        [HttpGet]
        public async Task<IActionResult> GetRealTimeData()
        {
            var userId = _userManager.GetUserId(User);
            var userStocks = await _context.UserStocks.Where(u => u.UserId == userId).ToListAsync();
            var stockSymbols = userStocks.Select(s => s.StockSymbol).Distinct().ToList();
            var realTimeData = await _stockService.GetRealTimeDataAsync(stockSymbols, 100);

            var formattedData = realTimeData.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Where(d => d?.Date != null).Select(d => new
                {
                    x = d.Date.ToString("yyyy-MM-ddTHH:mm:ss"), // Format date to ISO 8601
                    open = d.Open,
                    high = d.High,
                    low = d.Low,
                    close = d.Close,
                    volume = d.Volume
                }).ToList()
            );

            return Json(formattedData);
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

            var stockSymbols = userStocks.Select(s => s.StockSymbol).Distinct().ToList();
            var realTimeData = await _stockService.GetRealTimeDataAsync(stockSymbols);

            var stockSummary = new List<StockSummaryData>();
            var bubbleChartData = new List<BubbleChartData>();
            var realTimeTrendData = new List<RealTimeDataPoint>();

            foreach (var stock in realTimeData)
            {
                var userStock = userStocks.FirstOrDefault(s => s.StockSymbol == stock.Key);
                if (userStock != null)
                {
                    var latestData = stock.Value.Last();
                    userStock.CurrentPrice = latestData.Close;

                    var currentShares = userStock.NumberOfSharesPurchased - (userStock.NumberOfSharesSold ?? 0);
                    if (currentShares > 0)
                    {
                        var totalValue = currentShares * userStock.CurrentPrice;
                        var performancePercentage = ((userStock.CurrentPrice - userStock.PurchasePrice) / userStock.PurchasePrice) * 100;
                        var valueChange = (userStock.CurrentPrice - userStock.PurchasePrice) * currentShares;

                        stockSummary.Add(new StockSummaryData
                        {
                            StockSymbol = userStock.StockSymbol,
                            CurrentPrice = userStock.CurrentPrice,
                            PurchasePrice = userStock.PurchasePrice,
                            TotalShares = currentShares,
                            TotalValue = totalValue,
                            PerformancePercentage = performancePercentage,
                            ValueChange = valueChange
                        });

                        bubbleChartData.Add(new BubbleChartData
                        {
                            StockSymbol = userStock.StockSymbol,
                            CurrentPrice = userStock.CurrentPrice,
                            TotalValue = totalValue,
                            Size = currentShares
                        });

                        realTimeTrendData.AddRange(stock.Value.Select(data => new RealTimeDataPoint
                        {
                            StockSymbol = userStock.StockSymbol,
                            Date = data.Date,
                            Open = data.Open,
                            High = data.High,
                            Low = data.Low,
                            Close = data.Close,
                            Volume = data.Volume
                        }));
                    }
                }
            }

            ViewBag.RealTimeData = realTimeData;
            ViewBag.StockSummary = stockSummary.OrderBy(s => s.PerformancePercentage).ToList();
            ViewBag.BubbleChartData = bubbleChartData;
            ViewBag.RealTimeTrendData = realTimeTrendData;

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

        public class RealTimeDataPoint
        {
            public string StockSymbol { get; set; }
            public DateTime Date { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public long Volume { get; set; }
        }

        public class BubbleChartData
        {
            public string StockSymbol { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal TotalValue { get; set; }
            public int Size { get; set; }
        }
        private async Task<Dictionary<string, decimal>> GetCachedRealTimePricesAsync(List<string> stockSymbols)
        {
            var result = new Dictionary<string, decimal>();
            var symbolsToFetch = new List<string>();

            foreach (var symbol in stockSymbols)
            {
                string cacheKey = $"RealTimePrice_{symbol}";
                if (_cache.TryGetValue(cacheKey, out decimal price))
                {
                    result[symbol] = price;
                }
                else
                {
                    symbolsToFetch.Add(symbol);
                }
            }

            if (symbolsToFetch.Any())
            {
                var fetchedPrices = await _stockService.GetRealTimePricesAsync(symbolsToFetch);
                foreach (var kvp in fetchedPrices)
                {
                    result[kvp.Key] = kvp.Value;
                    _cache.Set($"RealTimePrice_{kvp.Key}", kvp.Value, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                }
            }

            return result;
        }

        private async Task<Dictionary<DateTime, decimal>> GetCachedHistoricalPricesAsync(string stockSymbol, DateTime startDate)
        {
            string cacheKey = $"HistoricalPrices_{stockSymbol}_{startDate:yyyyMMdd}";
            if (_cache.TryGetValue(cacheKey, out Dictionary<DateTime, decimal>? historicalPrices) && historicalPrices != null)
            {
                return historicalPrices;
            }

            historicalPrices = await _stockService.GetHistoricalPricesAsync(stockSymbol, startDate);

            if (historicalPrices != null)
            {
                var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromDays(1));
                _cache.Set(cacheKey, historicalPrices, cacheEntryOptions);
                return historicalPrices;
            }

            // If we couldn't get historical prices, return an empty dictionary
            return new Dictionary<DateTime, decimal>();
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
        [HttpGet]
        public async Task<IActionResult> LoadRealTimeChart()
        {
            var userId = _userManager.GetUserId(User);
            var userStocks = await _context.UserStocks.Where(u => u.UserId == userId).ToListAsync();
            var stockSymbols = userStocks.Select(s => s.StockSymbol).Distinct().ToList();

            var realTimeData = await _stockService.GetRealTimeDataCachedAsync(stockSymbols);

            var realTimeTrendData = new List<RealTimeDataPoint>();
            foreach (var stock in realTimeData)
            {
                realTimeTrendData.AddRange(stock.Value.Select(data => new RealTimeDataPoint
                {
                    StockSymbol = stock.Key,
                    Date = data.Date,
                    Open = data.Open,
                    High = data.High,
                    Low = data.Low,
                    Close = data.Close,
                    Volume = data.Volume
                }));
            }

            ViewBag.RealTimeTrendData = realTimeTrendData;
            return PartialView("_RealTimeStockChartPartial");
        }

    }

    public class StockService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private const int CACHE_DURATION_MINUTES = 15;

        private const string ApiKey = "0d6b96123dmshd1cdf284f3c5deap10eec8jsnc171c25e1d53"; // TODO: Replace with your actual RapidAPI key
        private const string BaseUrl = "https://yahoo-finance15.p.rapidapi.com/api/v1";

        public StockService(HttpClient httpClient, ILogger<StockService> logger, IMemoryCache cache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", ApiKey);
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "yahoo-finance15.p.rapidapi.com");
        }

        public async Task<Dictionary<string, List<StockDataPoint>>> GetRealTimeDataAsync(List<string> stockSymbols, int dataPoints = 100)
        {
            var result = new Dictionary<string, List<StockDataPoint>>();
            var fetchTasks = new List<Task>();

            foreach (var symbol in stockSymbols)
            {
                var task = Task.Run(async () =>
                {
                    var url = $"{BaseUrl}/markets/stock/history?symbol={symbol}&interval=1d&diffandsplits=false&limit={dataPoints}";

                    for (int i = 0; i < 3; i++) // Try up to 3 times for each symbol
                    {
                        try
                        {
                            _logger.LogInformation($"Attempt {i + 1}: Fetching real-time data for {symbol}");
                            var response = await _httpClient.GetAsync(url);
                            response.EnsureSuccessStatusCode();
                            var json = await response.Content.ReadAsStringAsync();
                            var stockData = ProcessHistoricalData(json);
                            if (stockData.Any())
                            {
                                lock (result)
                                {
                                    result[symbol] = stockData;
                                }
                                break; // Success, move to next symbol
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            _logger.LogError(ex, $"Attempt {i + 1} failed to fetch data for {symbol}. Status code: {ex.StatusCode}");
                            if (i < 2) // If it's not the last attempt
                            {
                                await Task.Delay(1000 * (int)Math.Pow(2, i)); // Exponential backoff
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Unexpected error on attempt {i + 1} while fetching data for {symbol}");
                            break; // For unexpected errors, don't retry
                        }
                    }

                    if (!result.ContainsKey(symbol))
                    {
                        result[symbol] = new List<StockDataPoint>
                        {
                            new StockDataPoint
                            {
                                Date = DateTime.UtcNow,
                                Open = 0,
                                High = 0,
                                Low = 0,
                                Close = 0,
                                Volume = 0
                            }
                        };
                    }
                });

                fetchTasks.Add(task);
            }

            await Task.WhenAll(fetchTasks);
            return result;
        }


        private List<StockDataPoint> ProcessHistoricalData(string json)
        {
            var stockData = new List<StockDataPoint>();
            var jObject = JObject.Parse(json);

            var body = jObject["body"] as JObject;
            if (body != null)
            {
                foreach (var item in body)
                {
                    if (long.TryParse(item.Key, out long timestamp))
                    {
                        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
                        var dataPoint = item.Value as JObject;
                        if (dataPoint != null)
                        {
                            stockData.Add(new StockDataPoint
                            {
                                Date = date,
                                Open = dataPoint["open"]?.Value<decimal>() ?? 0,
                                High = dataPoint["high"]?.Value<decimal>() ?? 0,
                                Low = dataPoint["low"]?.Value<decimal>() ?? 0,
                                Close = dataPoint["close"]?.Value<decimal>() ?? 0,
                                Volume = dataPoint["volume"]?.Value<long>() ?? 0
                            });
                        }
                    }
                }
            }
            return stockData.OrderBy(d => d.Date).ToList();
        }
        public async Task<Dictionary<string, decimal>> GetRealTimePricesAsync(List<string> stockSymbols)
        {
            var result = new Dictionary<string, decimal>();
            var symbolsString = string.Join(",", stockSymbols);
            var url = $"{BaseUrl}/markets/stock/quotes?ticker={symbolsString}";

            try
            {
                _logger.LogInformation($"Fetching real-time prices for {symbolsString}");
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                var data = JObject.Parse(json);
                var bodyArray = data["body"] as JArray;

                if (bodyArray != null)
                {
                    foreach (var item in bodyArray)
                    {
                        var symbol = item["symbol"]?.ToString();
                        var priceToken = item["regularMarketPrice"];
                        if (!string.IsNullOrEmpty(symbol) && priceToken != null && decimal.TryParse(priceToken.ToString(), out decimal price))
                        {
                            result[symbol] = price;
                        }
                    }
                }

                _logger.LogInformation($"Successfully fetched real-time prices for {result.Count} stocks");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to fetch real-time prices for multiple stocks");
            }

            return result;
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

        public async Task<List<StockSymbolData>> GetAllStocksAsync(string? searchTerm)
        {
            var allStocks = new List<StockSymbolData>();
            var url = $"{BaseUrl}/markets/search?search={searchTerm}";
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                var stocksArray = data["body"] as JArray;
                if (stocksArray != null)
                {
                    foreach (var stock in stocksArray)
                    {
                        allStocks.Add(new StockSymbolData
                        {
                            Symbol = stock["symbol"]?.ToString() ?? string.Empty,
                            Name = stock["name"]?.ToString() ?? string.Empty
                        });
                    }
                }
                _logger.LogInformation($"Successfully fetched {allStocks.Count} stocks");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error while fetching stocks");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while fetching stocks");
            }
            return allStocks;
        }

        public async Task<Dictionary<string, List<StockDataPoint>>> GetRealTimeDataCachedAsync(List<string> stockSymbols, int dataPoints = 100)
        {
            var cacheKey = $"RealTimeData_{string.Join("_", stockSymbols)}";

            // Try to get cached data
            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, List<StockDataPoint>>? cachedData) || cachedData == null)
            {
                // Fetch data if not in cache or if cached data is null
                cachedData = await GetRealTimeDataAsync(stockSymbols, dataPoints) ?? new Dictionary<string, List<StockDataPoint>>();
                _cache.Set(cacheKey, cachedData, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
            }

            return cachedData;
        }

        public class StockDataPoint
        {
            public DateTime Date { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public long Volume { get; set; }
        }
    }
    public class StockSymbolData
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
    }
}
