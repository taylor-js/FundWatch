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

namespace FundWatch.Services
{
    public class StockService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private const int MAX_BATCH_SIZE = 5;
        private const int MAX_RETRIES = 3;
        private const int CACHE_DURATION_MINUTES = 15;
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
            private const int MAX_REQUESTS_PER_MINUTE = 2; // More conservative limit
            private const int DELAY_BETWEEN_REQUESTS_MS = 3000; // 1.5 seconds between requests

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

        public async Task<Dictionary<string, List<StockDataPoint>>> GetRealTimeDataAsync(List<string> stockSymbols, int daysBack = 90) // Change after Polygon.io plan upgrade
        {
            var result = new Dictionary<string, List<StockDataPoint>>();
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
                        var data = await FetchHistoricalDataAsync(symbol, startDate, today);
                        if (data != null)
                        {
                            result[symbol] = data;
                            _cache.Set(cacheKey, data, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching historical data for {Symbol}", symbol);
                    }
                });

                await Task.WhenAll(tasks);
                await Task.Delay(1000); // Add delay between batches to prevent rate limits
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
                        ["sort"] = "desc",
                        ["limit"] = "120"
                    };
                    var url = $"/v2/aggs/ticker/{WebUtility.UrlEncode(symbol)}/range/1/day/{startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}";

                    // Ensure await is used
                    var response = await SendApiRequestAsync(url, queryParams);

                    if (response != null && response["results"] is JArray results)
                    {
                        return ProcessStockDataPoints(results);
                    }
                }
                catch (HttpRequestException ex) when (retry < MAX_RETRIES - 1)
                {
                    _logger.LogWarning(ex, "Retry {Retry} for fetching historical data of {Symbol}", retry + 1, symbol);
                    await Task.Delay((int)Math.Pow(2, retry) * 1000); // Exponential backoff
                }
            }

            _logger.LogError("Failed to fetch historical data for {Symbol} after {MaxRetries} retries", symbol, MAX_RETRIES);
            return null;
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
                                .SetSlidingExpiration(TimeSpan.FromHours(8)); // Add sliding expiration
                            _cache.Set(cacheKey, price, cacheOptions);
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
                    var response = await SendApiRequestAsync(url);
                    if (response == null) continue;
                    var result = response["results"];
                    if (result != null)
                    {
                        var companyDetails = new CompanyDetails
                        {
                            Name = result["name"]?.ToString(),
                            Description = result["description"]?.ToString(),
                            Industry = result["sic_description"]?.ToString(),
                            MarketCap = result["market_cap"]?.Value<decimal>() ?? 0,
                            Website = result["homepage_url"]?.ToString(),
                            Employees = result["total_employees"]?.Value<int>() ?? 0
                        };
                        details[symbol] = companyDetails;

                        var cacheOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromHours(24));
                        _cache.Set(cacheKey, companyDetails, cacheOptions);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching company details for {Symbol}", symbol);
                }
            }
            return details;
        }

        private async Task<JObject> SendApiRequestAsync(string endpoint, Dictionary<string, string> queryParams = null)
        {
            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    await _rateLimiter.WaitForAvailableSlotAsync(); // Wait for rate limiter

                    queryParams ??= new Dictionary<string, string>();
                    queryParams["apiKey"] = _apiKey;

                    var queryString = string.Join("&", queryParams.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                    var fullUrl = $"{endpoint}?{queryString}";

                    var response = await _httpClient.GetAsync(fullUrl);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Rate limit exceeded. Retry {Retry} after delay.", retry + 1);
                        await Task.Delay(60000); // Wait 1 minute
                        continue;
                    }

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