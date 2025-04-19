﻿using System;
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
                
                if (data.TryGetValue(symbol, out var dataPoints) && dataPoints.Any())
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
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private const int MAX_BATCH_SIZE = 5;
        private const int MAX_RETRIES = 3;
        private const int CACHE_DURATION_MINUTES = 60; // Increased from 15 minutes to 1 hour
        private readonly RateLimitingHandler _rateLimiter; // Add this field

        public StockService(
    IHttpClientFactory clientFactory,
    ILogger<StockService> logger,
    IMemoryCache cache,
    IConfiguration configuration)
        {
            _httpClient = clientFactory.CreateClient("PolygonApi");
            _logger = logger;
            _cache = cache;
            _apiKey = configuration.GetValue<string>("PolygonApi:ApiKey") ??
                      throw new ArgumentNullException("PolygonApi:ApiKey", "API key is required");
            _rateLimiter = new RateLimitingHandler(); // Initialize RateLimiter
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
            var startDate = today.AddDays(-daysBack);

            // Group symbols into manageable batches
            var batchedSymbols = stockSymbols
                .Select((symbol, index) => new { symbol, index })
                .GroupBy(x => x.index / MAX_BATCH_SIZE)
                .Select(g => g.Select(x => x.symbol).ToList())
                .ToList();

            foreach (var batch in batchedSymbols)
            {
                var tasks = batch.Select(async symbol =>
                {
                    var cacheKey = $"HistoricalData_{symbol}_{daysBack}";
                    if (_cache.TryGetValue(cacheKey, out List<StockDataPoint> cachedData))
                    {
                        result[symbol] = cachedData;
                        return;
                    }

                    try
                    {
                        // Break the time range into smaller chunks (1 year each) to handle API limitations
                        var allData = new List<StockDataPoint>();
                        var chunkSize = 365; // 1 year chunks

                        for (var chunkStart = startDate; chunkStart < today; chunkStart = chunkStart.AddDays(chunkSize))
                        {
                            var chunkEnd = chunkStart.AddDays(chunkSize);
                            if (chunkEnd > today)
                                chunkEnd = today;

                            _logger.LogInformation($"Fetching chunk for {symbol}: {chunkStart:yyyy-MM-dd} to {chunkEnd:yyyy-MM-dd}");

                            var chunkData = await FetchHistoricalDataAsync(symbol, chunkStart, chunkEnd);
                            if (chunkData != null && chunkData.Any())
                            {
                                allData.AddRange(chunkData);
                            }

                            // Add a small delay between chunks to avoid rate limiting
                            await Task.Delay(500);
                        }

                        if (allData.Any())
                        {
                            // Ensure data is sorted by date (ascending)
                            allData = allData.OrderBy(d => d.Date).ToList();
                            result[symbol] = allData;
                            _cache.Set(cacheKey, allData, TimeSpan.FromHours(24)); // Cache historical data for 24 hours

                            _logger.LogInformation($"Collected total of {allData.Count} data points for {symbol}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching historical data for {Symbol}", symbol);
                    }
                });

                await Task.WhenAll(tasks);
            }

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

                    _logger.LogInformation($"Fetching historical data for {symbol} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

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
                if (_cache.TryGetValue(cacheKey, out List<StockSymbolData> cachedStocks))
                {
                    return cachedStocks;
                }

                var allStocks = new List<StockSymbolData>();
                var uniqueStocks = new HashSet<string>();

                // Only make one request initially
                var url = "/v3/reference/tickers";
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

                    if (!string.IsNullOrEmpty(symbol) &&
                        symbol.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                        uniqueStocks.Add(symbol))
                    {
                        allStocks.Add(new StockSymbolData
                        {
                            Symbol = symbol,
                            Name = name
                        });
                    }
                }

                // Cache the results
                _cache.Set(cacheKey, allStocks, TimeSpan.FromHours(24));

                return allStocks.OrderBy(s => s.Symbol).ToList();
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

            var prices = new Dictionary<string, decimal>();
            var distinctSymbols = stockSymbols.Distinct().ToList();

            // Process one at a time but with longer cache duration since we only need daily closing prices
            foreach (var symbol in distinctSymbols)
            {
                try
                {
                    var cacheKey = $"DailyClosingPrice_{symbol}";

                    if (_cache.TryGetValue(cacheKey, out decimal cachedPrice))
                    {
                        prices[symbol] = cachedPrice;
                        continue;
                    }

                    await _rateLimiter.WaitForAvailableSlotAsync();

                    var url = $"/v2/aggs/ticker/{symbol}/prev";
                    var response = await SendApiRequestAsync(url);
                    if (response == null) continue;

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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching daily closing price for {Symbol}", symbol);
                }
            }

            return prices;
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

            var details = new Dictionary<string, CompanyDetails>();

            foreach (var symbol in symbols)
            {
                try
                {
                    var cacheKey = $"CompanyDetails_{symbol}";
                    if (_cache.TryGetValue(cacheKey, out CompanyDetails cachedDetails))
                    {
                        details[symbol] = cachedDetails;
                        continue;
                    }

                    var url = $"/v3/reference/tickers/{symbol}";
                    _logger.LogInformation("Fetching details for {Symbol} from {Url}", symbol, url);

                    var response = await SendApiRequestAsync(url);
                    if (response == null)
                    {
                        _logger.LogWarning("No response received for {Symbol}", symbol);
                        continue;
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
                            Employees = SafeGetInt(result, "total_employees")
                        };

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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching company details for {Symbol}", symbol);
                }
            }

            return details;
        }

        // Safe helper method to get string values
        private string SafeGetString(JToken token, string propertyName)
        {
            try
            {
                if (token[propertyName] == null || token[propertyName].Type == JTokenType.Null)
                    return string.Empty;

                return token[propertyName].ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting string value for {PropertyName}", propertyName);
                return string.Empty;
            }
        }

        // Safe helper method to get decimal values
        private decimal SafeGetDecimal(JToken token, string propertyName)
        {
            try
            {
                if (token[propertyName] == null || token[propertyName].Type == JTokenType.Null)
                    return 0;

                return token[propertyName].Value<decimal>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting decimal value for {PropertyName}", propertyName);
                return 0;
            }
        }

        // Safe helper method to get integer values
        private int SafeGetInt(JToken token, string propertyName)
        {
            try
            {
                if (token[propertyName] == null || token[propertyName].Type == JTokenType.Null)
                    return 0;

                return token[propertyName].Value<int>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting integer value for {PropertyName}", propertyName);
                return 0;
            }
        }


        private async Task<JObject> SendApiRequestAsync(string endpoint, Dictionary<string, string> queryParams = null)
        {
            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    queryParams ??= new Dictionary<string, string>();
                    queryParams["apiKey"] = _apiKey;

                    var queryString = string.Join("&", queryParams.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                    var fullUrl = $"{endpoint}?{queryString}";

                    var response = await _httpClient.GetAsync(fullUrl);

                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(content);
                }
                catch (Exception ex) when (retry < MAX_RETRIES - 1)
                {
                    _logger.LogWarning(ex, "Retry {Retry} for request to {Endpoint}.", retry + 1, endpoint);
                }
            }

            _logger.LogError("Failed to send API request to {Endpoint} after {MaxRetries} retries.", endpoint, MAX_RETRIES);
            return null;
        }

        private List<StockDataPoint> ProcessStockDataPoints(JArray results)
        {
            return results.Select(item => new StockDataPoint
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(item["t"].Value<long>()).UtcDateTime,
                Open = item["o"].Value<decimal>(),
                High = item["h"].Value<decimal>(),
                Low = item["l"].Value<decimal>(),
                Close = item["c"].Value<decimal>(),
                Volume = item["v"].Value<long>()
            }).ToList();
        }

        private List<StockSymbolData> ProcessStockSymbols(JArray results)
        {
            var stocks = new List<StockSymbolData>();
            if (results == null) return stocks;

            foreach (var result in results)
            {
                stocks.Add(new StockSymbolData
                {
                    Symbol = result["ticker"]?.ToString(),
                    Name = result["name"]?.ToString()
                });
            }
            return stocks;
        }
    }
}