using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using FundWatch.Models;
using System.Net;
using System.Threading.RateLimiting;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis.Elfie.Model;
using System.Collections.Concurrent;

namespace FundWatch.Services
{
    public class StockService
    {
        // Method to get historical price for a specific date (used by form)
        public async Task<decimal> GetHistoricalPriceAsync(string symbol, DateTime date)
        {
            try
            {
                var data = await GetRealTimeDataAsync(new List<string> { symbol }, 1825);
                
                if (data.TryGetValue(symbol, out var dataPoints) && dataPoints != null && dataPoints.Any())
                {
                    var pricePoint = dataPoints.FirstOrDefault(dp => dp.Date.Date == date.Date);
                    if (pricePoint != null && pricePoint.Close > 0)
                    {
                        return pricePoint.Close;
                    }
                    
                    // If exact date not found, find closest preceding date
                    var closestPoint = dataPoints
                        .Where(dp => dp.Date.Date <= date.Date && dp.Close > 0)
                        .OrderByDescending(dp => dp.Date)
                        .FirstOrDefault();
                        
                    if (closestPoint != null)
                    {
                        return closestPoint.Close;
                    }
                }
                
                // Fallback to current price if historical not available
                var prices = await GetRealTimePricesAsync(new List<string> { symbol });
                if (prices.TryGetValue(symbol, out decimal price) && price > 0)
                {
                    return price;
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        // Method to get the earliest available date with valid price for a stock
        public async Task<(DateTime date, decimal price)?> GetEarliestAvailableDateAsync(string symbol)
        {
            try
            {
                var data = await GetRealTimeDataAsync(new List<string> { symbol }, 1825);
                
                if (data.TryGetValue(symbol, out var dataPoints) && dataPoints != null && dataPoints.Any())
                {
                    // Find the earliest date with a valid price
                    var earliestPoint = dataPoints
                        .Where(dp => dp.Close > 0)
                        .OrderBy(dp => dp.Date)
                        .FirstOrDefault();
                    
                    if (earliestPoint != null)
                    {
                        return (earliestPoint.Date, earliestPoint.Close);
                    }
                }
                
                // If no historical data, try to get current price
                var prices = await GetRealTimePricesAsync(new List<string> { symbol });
                if (prices.TryGetValue(symbol, out decimal price) && price > 0)
                {
                    // Return today with current price as fallback
                    return (DateTime.UtcNow.Date, price);
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting earliest available date for symbol: {Symbol}", symbol);
                return null;
            }
        }
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private const int MAX_BATCH_SIZE = 5;
        private const int MAX_RETRIES = 3;
        private const int CACHE_DURATION_MINUTES = 60; // Increased from 15 minutes to 1 hour
        private readonly RateLimitingHandler _rateLimiter; // Add this field
        private readonly SemaphoreSlim _apiSemaphore; // For concurrent API calls
        private readonly int _maxConcurrentApiCalls = 3; // Configurable concurrent calls

        public StockService(
    IHttpClientFactory clientFactory,
    ILogger<StockService> logger,
    IMemoryCache cache,
    IConfiguration configuration)
        {
            _httpClient = clientFactory.CreateClient("PolygonApi");
            _logger = logger;
            _cache = cache;
            
            // Try to get API key from configuration with fallback to environment variable
            string? configApiKey = configuration.GetValue<string>("PolygonApi:ApiKey");
            string? envApiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY");
            _apiKey = configApiKey ?? envApiKey ?? "MISSING_API_KEY";
            
            if (_apiKey == "MISSING_API_KEY")
            {
                _logger.LogError("Polygon API key is missing. Chart data will not be available.");
            }
            
            _rateLimiter = new RateLimitingHandler(); // Initialize RateLimiter
            _apiSemaphore = new SemaphoreSlim(_maxConcurrentApiCalls, _maxConcurrentApiCalls);
        }

        public class RateLimitingHandler
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly Queue<DateTime> _requestTimestamps;
            private const int MAX_REQUESTS_PER_MINUTE = 5; // Increased from 2
            private const int DELAY_BETWEEN_REQUESTS_MS = 500; // Reduced from 3000

            public RateLimitingHandler()
            {
                _semaphore = new SemaphoreSlim(1, 1);
                _requestTimestamps = new Queue<DateTime>();
            }

            public async Task WaitForAvailableSlotAsync()
            {
                await _semaphore.WaitAsync();
                try
                {
                    var now = DateTime.UtcNow;

                    // Remove timestamps older than 1 minute
                    while (_requestTimestamps.Count > 0 && (now - _requestTimestamps.Peek()).TotalMinutes >= 1)
                    {
                        _requestTimestamps.Dequeue();
                    }

                    // If we've reached the limit, wait
                    if (_requestTimestamps.Count >= MAX_REQUESTS_PER_MINUTE)
                    {
                        var oldestRequest = _requestTimestamps.Peek();
                        var waitTime = oldestRequest.AddMinutes(1) - now;
                        if (waitTime > TimeSpan.Zero)
                        {
                            await Task.Delay(waitTime);
                        }
                        _requestTimestamps.Dequeue();
                    }

                    // Always wait at least 1 second between requests
                    await Task.Delay(DELAY_BETWEEN_REQUESTS_MS);

                    _requestTimestamps.Enqueue(now);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        public async Task<ConcurrentDictionary<string, List<StockDataPoint>>> GetRealTimeDataAsync(List<string> stockSymbols, int daysBack = 1825) // 5 years
        {
            var result = new ConcurrentDictionary<string, List<StockDataPoint>>();
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1); // Include tomorrow to ensure today's data is included
            var startDate = today.AddDays(-daysBack);

            // Process all symbols concurrently with semaphore throttling
            var tasks = stockSymbols.Select(async symbol =>
                {
                    var cacheKey = $"HistoricalData_{symbol}_{daysBack}";
                    if (_cache.TryGetValue(cacheKey, out List<StockDataPoint>? cachedData) && cachedData != null)
                    {
                        result[symbol] = cachedData;
                        return;
                    }

                    try
                    {
                        // Break the time range into smaller chunks (1 year each) to handle API limitations
                        var allData = new List<StockDataPoint>();
                        var chunkSize = 365; // 1 year chunks

                        for (var chunkStart = startDate; chunkStart < tomorrow; chunkStart = chunkStart.AddDays(chunkSize))
                        {
                            var chunkEnd = chunkStart.AddDays(chunkSize);
                            if (chunkEnd > tomorrow)
                                chunkEnd = tomorrow;

                            _logger.LogInformation($"Fetching chunk for {symbol}: {chunkStart:yyyy-MM-dd} to {chunkEnd:yyyy-MM-dd}");

                            await _apiSemaphore.WaitAsync();
                            try
                            {
                                var chunkData = await FetchHistoricalDataAsync(symbol, chunkStart, chunkEnd);
                                if (chunkData != null && chunkData.Any())
                                {
                                    allData.AddRange(chunkData);
                                }
                            }
                            finally
                            {
                                _apiSemaphore.Release();
                            }
                        }

                        if (allData.Any())
                        {
                            // Ensure data is sorted by date (ascending)
                            allData = allData.OrderBy(d => d.Date).ToList();
                            result[symbol] = allData;
                            _cache.Set(cacheKey, allData, TimeSpan.FromHours(8)); // Cache historical data for 8 hours to ensure fresher data

                            _logger.LogInformation($"Collected total of {allData.Count} data points for {symbol}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching historical data for {Symbol}", symbol);
                    }
                });

            await Task.WhenAll(tasks);

            return result;
        }


        private async Task<List<StockDataPoint>> FetchHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    var queryParams = new Dictionary<string, string>
                    {
                        ["adjusted"] = "true",
                        ["sort"] = "asc",  // Changed from "desc" to "asc" to get oldest first
                        ["limit"] = "5000" // API limit for maximum points
                    };
                    var url = $"/v2/aggs/ticker/{WebUtility.UrlEncode(symbol)}/range/1/day/{startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}";

                    _logger.LogInformation("Fetching historical data for {Symbol} from {StartDate} to {EndDate}", symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                    // Ensure await is used
                    var response = await SendApiRequestAsync(url, queryParams);

                    if (response != null && response["results"] is JArray results)
                    {
                        var dataPoints = ProcessStockDataPoints(results);
                        _logger.LogInformation($"Received {dataPoints.Count} data points for {symbol}");
                        return dataPoints;
                    }
                }
                catch (HttpRequestException ex) when (retry < MAX_RETRIES - 1)
                {
                    _logger.LogWarning(ex, "Retry {Retry} for fetching historical data of {Symbol}", retry + 1, symbol);
                    await Task.Delay((int)Math.Pow(2, retry) * 1000); // Exponential backoff
                }
            }

            _logger.LogError("Failed to fetch historical data for {Symbol} after {MaxRetries} retries", symbol, MAX_RETRIES);
            return new List<StockDataPoint>(); // Return an empty list instead of null
        }

        public async Task<StockSymbolData?> GetExactStockAsync(string symbol)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return null;
                }
                
                // First check cache for an exact match
                var cacheKey = $"ExactStock_{symbol.Trim().ToUpper()}";
                if (_cache.TryGetValue(cacheKey, out StockSymbolData? cachedStock) && cachedStock != null)
                {
                    return cachedStock;
                }
                
                // Use the search endpoint with the exact symbol
                var stocks = await GetAllStocksAsync(symbol);
                
                // Find exact match
                var exactMatch = stocks.FirstOrDefault(s => 
                    string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                    
                if (exactMatch != null)
                {
                    // Set exact match flag
                    exactMatch.ExactMatch = true;
                    
                    // Cache the exact match
                    _cache.Set(cacheKey, exactMatch, TimeSpan.FromHours(24));
                    return exactMatch;
                }
                
                // If no exact match, we'll try to get any stock with this symbol
                var queryParams = new Dictionary<string, string>
                {
                    ["ticker"] = symbol,
                    ["active"] = "true"
                };
                
                var response = await SendApiRequestAsync("/v3/reference/tickers", queryParams);
                
                if (response != null && response["results"] is JArray results && results.Any())
                {
                    var result = results.First();
                    var stock = new StockSymbolData
                    {
                        Symbol = result["ticker"]?.ToString() ?? string.Empty,
                        Name = result["name"]?.ToString() ?? string.Empty,
                        ExactMatch = true
                    };
                    
                    _cache.Set(cacheKey, stock, TimeSpan.FromHours(24));
                    return stock;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching exact stock for symbol: {Symbol}", symbol);
                return null;
            }
        }
        
        public async Task<List<StockSymbolData>> GetAllStocksAsync(string searchTerm)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return new List<StockSymbolData>();
                }

                // Check cache first
                var cacheKey = $"StockSymbols_{searchTerm}";
                if (_cache.TryGetValue(cacheKey, out List<StockSymbolData>? cachedStocks) && cachedStocks != null)
                {
                    return cachedStocks;
                }

                var allStocks = new List<StockSymbolData>();
                var uniqueStocks = new HashSet<string>();

                // Define API endpoint for stock tickers
                var queryParams = new Dictionary<string, string>
                {
                    ["search"] = searchTerm,
                    ["market"] = "stocks",
                    ["active"] = "true",
                    ["sort"] = "ticker",
                    ["limit"] = "100"
                };
                var response = await SendApiRequestAsync("/v3/reference/tickers", queryParams);

                if (response == null) return allStocks;

                var results = response["results"] as JArray;
                if (results == null || !results.Any()) return allStocks;

                foreach (var result in results)
                {
                    var symbol = result["ticker"]?.ToString();
                    var name = result["name"]?.ToString();

                    bool matchesSearch = false;
                    
                    if (string.Equals(symbol, searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        // Exact symbol match gets highest priority
                        matchesSearch = true;
                    }
                    else if (!string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(name) &&
                             (symbol.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) || 
                              name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Partial match in symbol or name
                        matchesSearch = true;
                    }
                    
                    if (matchesSearch && !string.IsNullOrEmpty(symbol) && uniqueStocks.Add(symbol))
                    {
                        allStocks.Add(new StockSymbolData
                        {
                            Symbol = symbol,
                            Name = name ?? string.Empty,
                            // Track if this was an exact match
                            ExactMatch = string.Equals(symbol, searchTerm, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }

                // Cache the results
                _cache.Set(cacheKey, allStocks, TimeSpan.FromHours(24));

                // Sort exact matches first, then alphabetically
                return allStocks.OrderByDescending(s => s.ExactMatch).ThenBy(s => s.Symbol).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching stock symbols for search term: {SearchTerm}", searchTerm);
                return new List<StockSymbolData>();
            }
        }

        public async Task<Dictionary<string, decimal>> GetRealTimePricesAsync(List<string> stockSymbols)
        {
            if (stockSymbols == null || !stockSymbols.Any())
            {
                return new Dictionary<string, decimal>();
            }

            var prices = new ConcurrentDictionary<string, decimal>();
            var distinctSymbols = stockSymbols.Distinct().ToList();

            // Process symbols concurrently
            var tasks = distinctSymbols.Select(async symbol =>
            {
                try
                {
                    var cacheKey = $"DailyClosingPrice_{symbol}";

                    if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
                    {
                        prices[symbol] = cachedPrice;
                        return;
                    }

                    await _apiSemaphore.WaitAsync();
                    try
                    {
                        await _rateLimiter.WaitForAvailableSlotAsync();

                        var url = $"/v2/aggs/ticker/{symbol}/prev";
                        var response = await SendApiRequestAsync(url);
                        if (response == null) return;

                        var result = response["results"]?.FirstOrDefault();
                        if (result != null)
                        {
                            var price = result["c"]?.Value<decimal>() ?? 0;
                            if (price > 0)
                            {
                                prices[symbol] = price;

                                // Cache until next trading day (roughly)
                                var cacheOptions = new MemoryCacheEntryOptions()
                                    .SetAbsoluteExpiration(GetNextTradingDay())
                                    .SetSlidingExpiration(TimeSpan.FromHours(12)); // Increased from 8 hours
                                _cache.Set(cacheKey, price, cacheOptions);
                                // Cache with a consistent key format across the app
                                _cache.Set($"Price_{symbol}", price, cacheOptions);
                            }
                        }
                    }
                    finally
                    {
                        _apiSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching daily closing price for {Symbol}", symbol);
                }
            });

            await Task.WhenAll(tasks);

            return new Dictionary<string, decimal>(prices);
        }

        // Method to preload data for a specific user
        public async Task PreloadUserDataAsync(string userId, List<string> symbols)
        {
            if (string.IsNullOrEmpty(userId) || symbols == null || !symbols.Any())
                return;

            _logger.LogInformation("Preloading stock data for user {UserId} with {SymbolCount} symbols", userId, symbols.Count);

            // Load prices and historical data in parallel
            var pricesTask = GetRealTimePricesAsync(symbols);
            var historicalTask = GetRealTimeDataAsync(symbols, 365); // 1 year of data for quick load
            var detailsTask = GetCompanyDetailsAsync(symbols);

            await Task.WhenAll(pricesTask, historicalTask, detailsTask);

            _logger.LogInformation("Completed preloading data for user {UserId}", userId);
        }

        private DateTime GetNextTradingDay()
        {
            var tomorrow = DateTime.UtcNow.AddDays(1).Date;

            // If tomorrow is weekend, skip to Monday
            while (tomorrow.DayOfWeek == DayOfWeek.Saturday || tomorrow.DayOfWeek == DayOfWeek.Sunday)
            {
                tomorrow = tomorrow.AddDays(1);
            }

            // Set to 9:30 AM Eastern Time (market open)
            return tomorrow.AddHours(13).AddMinutes(30);
        }

        public async Task<Dictionary<string, CompanyDetails>> GetCompanyDetailsAsync(List<string> symbols)
        {
            if (symbols == null || !symbols.Any())
            {
                return new Dictionary<string, CompanyDetails>();
            }

            var details = new ConcurrentDictionary<string, CompanyDetails>();

            // Process symbols concurrently
            var tasks = symbols.Select(async symbol =>
            {
                try
                {
                    var cacheKey = $"CompanyDetails_{symbol}";
                    if (_cache.TryGetValue(cacheKey, out CompanyDetails? cachedDetails) && cachedDetails != null)
                    {
                        details[symbol] = cachedDetails;
                        return;
                    }

                    await _apiSemaphore.WaitAsync();
                    try
                    {
                        var url = $"/v3/reference/tickers/{symbol}";
                        _logger.LogInformation("Fetching details for {Symbol} from {Url}", symbol, url);

                        await _rateLimiter.WaitForAvailableSlotAsync();
                        var response = await SendApiRequestAsync(url);
                    if (response == null)
                    {
                        _logger.LogWarning("No response received for {Symbol}", symbol);
                        return;
                    }

                    // Debug log the raw JSON
                    _logger.LogDebug("Raw JSON for {Symbol}: {Json}", symbol, response.ToString());

                    var result = response["results"];
                    if (result != null)
                    {
                        // Create a new CompanyDetails with safe null handling
                        var companyDetails = new CompanyDetails
                        {
                            Name = SafeGetString(result, "name"),
                            Description = SafeGetString(result, "description"),
                            Industry = SafeGetString(result, "sic_description"),
                            MarketCap = SafeGetDecimal(result, "market_cap"),
                            Website = SafeGetString(result, "homepage_url"),
                            Employees = SafeGetInt(result, "total_employees"),
                            DailyChange = 0, // Will be populated from snapshot endpoint
                            DailyChangePercent = 0, // Will be populated from snapshot endpoint
                            Extended = new ExtendedCompanyDetails
                            {
                                // Using the correct field names as per Polygon.io API docs
                                StockType = SafeGetString(result, "type"),
                                Exchange = SafeGetString(result, "primary_exchange"),
                                Currency = SafeGetString(result, "currency_name"),
                                Sector = SafeGetString(result, "sector"),
                                IndustryGroup = SafeGetString(result, "industry"),
                                Country = SafeGetString(result, "locale")
                            }
                        };

                        // Try to fetch recent news and daily change in parallel
                        var newsTask = FetchCompanyNewsAsync(symbol, companyDetails);
                        var changeTask = FetchDailyChangeAsync(symbol, companyDetails);
                        await Task.WhenAll(newsTask, changeTask);

                        _logger.LogInformation("Fetched company details for {Symbol}: {@CompanyDetails}",
                            symbol, new
                            {
                                companyDetails.Name,
                                HasDescription = !string.IsNullOrEmpty(companyDetails.Description),
                                HasIndustry = !string.IsNullOrEmpty(companyDetails.Industry),
                                HasMarketCap = companyDetails.MarketCap > 0,
                                HasWebsite = !string.IsNullOrEmpty(companyDetails.Website),
                                HasEmployees = companyDetails.Employees > 0
                            });

                        details[symbol] = companyDetails;

                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromHours(24));
                        _cache.Set(cacheKey, companyDetails, cacheOptions);
                    }
                    else
                    {
                        _logger.LogWarning("No results found in response for {Symbol}", symbol);
                    }
                    }
                    finally
                    {
                        _apiSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching company details for {Symbol}", symbol);
                }
            });

            await Task.WhenAll(tasks);

            return new Dictionary<string, CompanyDetails>(details);
        }

        // Safe helper method to get string values
        private string SafeGetString(JToken? token, string propertyName)
        {
            try
            {
                if (token == null || token[propertyName] == null || token[propertyName]?.Type == JTokenType.Null)
                    return string.Empty;

                return token[propertyName]?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting string value for {PropertyName}", propertyName);
                return string.Empty;
            }
        }

        // Safe helper method to get decimal values
        private decimal SafeGetDecimal(JToken? token, string propertyName)
        {
            try
            {
                if (token == null || token[propertyName] == null || token[propertyName]?.Type == JTokenType.Null)
                    return 0;

                return token[propertyName]?.Value<decimal>() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting decimal value for {PropertyName}", propertyName);
                return 0;
            }
        }

        // Safe helper method to get integer values
        private int SafeGetInt(JToken? token, string propertyName)
        {
            try
            {
                if (token == null || token[propertyName] == null || token[propertyName]?.Type == JTokenType.Null)
                    return 0;

                return token[propertyName]?.Value<int>() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting integer value for {PropertyName}", propertyName);
                return 0;
            }
        }


        private async Task<JObject?> SendApiRequestAsync(string endpoint, Dictionary<string, string>? queryParams = null)
        {
            // Check if API key is invalid/missing before making the request
            if (string.IsNullOrEmpty(_apiKey) || _apiKey == "MISSING_API_KEY")
            {
                _logger.LogError("API key is missing or invalid. Please configure a valid Polygon.io API key.");
                return null;
            }

            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    queryParams ??= new Dictionary<string, string>();
                    queryParams["apiKey"] = _apiKey;

                    var queryString = string.Join("&", queryParams.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                    var fullUrl = $"{endpoint}?{queryString}";

                    var response = await _httpClient.GetAsync(fullUrl);

                    if (response.StatusCode == HttpStatusCode.Forbidden) // 403 error
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("API key authentication failed (403 Forbidden). Please check your Polygon.io API key. Response: {Content}", errorContent);
                        
                        // Don't retry on auth failures - it won't help
                        return null;
                    }

                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(content);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogError(ex, "API key authentication failed (403 Forbidden). Please check your Polygon.io API key.");
                    // Don't retry auth failures
                    return null;
                }
                catch (Exception ex) when (retry < MAX_RETRIES - 1)
                {
                    _logger.LogWarning(ex, "Retry {Retry} for request to {Endpoint}.", retry + 1, endpoint);
                    await Task.Delay((int)Math.Pow(2, retry) * 1000); // Exponential backoff
                }
            }

            _logger.LogError("Failed to send API request to {Endpoint} after {MaxRetries} retries.", endpoint, MAX_RETRIES);
            return null;
        }

        private List<StockDataPoint> ProcessStockDataPoints(JArray results)
        {
            return results.Select(item => {
                if (item == null) return null;
                return new StockDataPoint
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(item["t"]?.Value<long>() ?? 0).UtcDateTime,
                    Open = item["o"]?.Value<decimal>() ?? 0,
                    High = item["h"]?.Value<decimal>() ?? 0,
                    Low = item["l"]?.Value<decimal>() ?? 0,
                    Close = item["c"]?.Value<decimal>() ?? 0,
                    Volume = item["v"]?.Value<long>() ?? 0
                };
            }).Where(item => item != null).ToList()!;
        }

        private List<StockSymbolData> ProcessStockSymbols(JArray results)
        {
            var stocks = new List<StockSymbolData>();
            if (results == null) return stocks;

            foreach (var result in results)
            {
                var symbol = result["ticker"]?.ToString();
                var name = result["name"]?.ToString();
                if (symbol != null) {
                    stocks.Add(new StockSymbolData
                    {
                        Symbol = symbol,
                        Name = name ?? string.Empty
                    });
                }
            }
            return stocks;
        }

        private async Task FetchCompanyNewsAsync(string symbol, CompanyDetails companyDetails)
        {
            try
            {
                var cacheKey = $"CompanyNews_{symbol}";

                // Check cache first
                if (_cache.TryGetValue(cacheKey, out List<CompanyNewsItem>? cachedNews) && cachedNews != null && cachedNews.Count > 0)
                {
                    companyDetails.Extended.RecentNews = cachedNews;
                    return;
                }

                // Calculate a range of the past 30 days for news
                var endDate = DateTime.Now;
                var startDate = endDate.AddDays(-30);

                var queryParams = new Dictionary<string, string>
                {
                    ["limit"] = "10",
                    ["order"] = "desc",
                    ["sort"] = "published_utc"
                };

                // Use the correct news API endpoint
                var url = $"/v2/reference/news";
                queryParams["ticker"] = symbol;
                await _rateLimiter.WaitForAvailableSlotAsync();
                var response = await SendApiRequestAsync(url, queryParams);

                if (response == null || response["results"] == null)
                {
                    _logger.LogWarning("No news data returned for {Symbol}", symbol);
                    return;
                }

                var newsItems = new List<CompanyNewsItem>();
                var results = response["results"] as JArray;

                if (results != null && results.Count > 0)
                {
                    foreach (var item in results.Take(3)) // Limit to 3 news items
                    {
                        var publishedUtc = SafeGetString(item, "published_utc");
                        if (DateTime.TryParse(publishedUtc, out var publishDate))
                        {
                            var title = SafeGetString(item, "title");
                            var description = SafeGetString(item, "description");

                            // Use the description if available, otherwise use the title
                            var newsContent = !string.IsNullOrEmpty(description) ? description : title;

                            newsItems.Add(new CompanyNewsItem
                            {
                                Date = publishDate,
                                Title = newsContent
                            });
                        }
                    }

                    // Cache the news for 12 hours
                    _cache.Set(cacheKey, newsItems, TimeSpan.FromHours(12));

                    // Update the company details object
                    companyDetails.Extended.RecentNews = newsItems;

                    _logger.LogInformation("Fetched {Count} news items for {Symbol}", newsItems.Count, symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching company news for {Symbol}", symbol);
            }
        }

        private async Task FetchDailyChangeAsync(string symbol, CompanyDetails companyDetails)
        {
            try
            {
                // Get the previous day's close data
                var url = $"/v2/aggs/ticker/{symbol}/prev";
                await _rateLimiter.WaitForAvailableSlotAsync();
                var response = await SendApiRequestAsync(url);
                
                if (response != null && response["results"] != null)
                {
                    var results = response["results"].First();
                    if (results != null)
                    {
                        var prevClose = SafeGetDecimal(results, "c");
                        var currentClose = SafeGetDecimal(results, "c");
                        var openPrice = SafeGetDecimal(results, "o");
                        
                        // Get current price from real-time data
                        var priceData = await GetRealTimePricesAsync(new List<string> { symbol });
                        if (priceData.TryGetValue(symbol, out decimal currentPrice) && currentPrice > 0 && prevClose > 0)
                        {
                            // Calculate daily change
                            companyDetails.DailyChange = currentPrice - prevClose;
                            companyDetails.DailyChangePercent = prevClose != 0 ? ((currentPrice - prevClose) / prevClose) * 100 : 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching daily change for {Symbol}", symbol);
                // Don't throw - just leave the values as 0
            }
        }
    }
}