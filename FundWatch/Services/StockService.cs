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

namespace FundWatch.Services
{
    public class StockService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private readonly RateLimitingHandler _rateLimiter;
        private const int CACHE_DURATION_MINUTES = 15;
        private const int MAX_BATCH_SIZE = 5;
        private const int MAX_RETRIES = 3;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

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
            _rateLimiter = new RateLimitingHandler(5);
        }
        public class RateLimitingHandler
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly Queue<DateTime> _requestTimestamps;
            private readonly int _maxRequestsPerMinute;
            private readonly object _lockObject = new object();
            private DateTime _nextAllowedRequestTime = DateTime.MinValue;

            public RateLimitingHandler(int maxRequestsPerMinute = 5)
            {
                _maxRequestsPerMinute = Math.Max(1, maxRequestsPerMinute);
                _semaphore = new SemaphoreSlim(1, 1);
                _requestTimestamps = new Queue<DateTime>();
            }

            public async Task WaitForAvailableSlotAsync(CancellationToken cancellationToken = default)
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    var now = DateTime.UtcNow;

                    // First check if we need to wait based on previous rate limit response
                    var delayForRateLimit = _nextAllowedRequestTime - now;
                    if (delayForRateLimit > TimeSpan.Zero)
                    {
                        _semaphore.Release();
                        await Task.Delay(delayForRateLimit, cancellationToken);
                        await _semaphore.WaitAsync(cancellationToken);
                    }

                    lock (_lockObject)
                    {
                        // Remove timestamps older than 1 minute
                        while (_requestTimestamps.Count > 0 &&
                               (now - _requestTimestamps.Peek()).TotalMinutes >= 1)
                        {
                            _requestTimestamps.Dequeue();
                        }

                        // If we've hit the limit, calculate wait time
                        if (_requestTimestamps.Count >= _maxRequestsPerMinute)
                        {
                            var oldestRequest = _requestTimestamps.Peek();
                            var waitTime = oldestRequest.AddMinutes(1) - now;
                            if (waitTime > TimeSpan.Zero)
                            {
                                _semaphore.Release();
                                Task.Delay(waitTime, cancellationToken).Wait(cancellationToken);
                                _semaphore.WaitAsync(cancellationToken).Wait(cancellationToken);

                                // After waiting, remove old timestamp
                                _requestTimestamps.Dequeue();
                            }
                        }

                        _requestTimestamps.Enqueue(now);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            public void HandleRateLimitResponse(HttpResponseMessage response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (response.Headers.TryGetValues("Retry-After", out var values) &&
                        int.TryParse(values.FirstOrDefault(), out int retryAfterSeconds))
                    {
                        _nextAllowedRequestTime = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
                    }
                    else
                    {
                        // Default to waiting 60 seconds if no Retry-After header
                        _nextAllowedRequestTime = DateTime.UtcNow.AddSeconds(60);
                    }
                }
            }
        }
        public async Task<Dictionary<string, List<StockDataPoint>>> GetRealTimeDataAsync(
        List<string> stockSymbols,
        int daysBack = 365)
        {
            if (stockSymbols == null || !stockSymbols.Any())
            {
                _logger.LogWarning("GetRealTimeDataAsync called with empty or null stock symbols");
                return new Dictionary<string, List<StockDataPoint>>();
            }

            var result = new Dictionary<string, List<StockDataPoint>>();
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(-Math.Min(daysBack, 1825)); // Limit to 5 years

            // Process stocks in batches
            var batches = stockSymbols
                .Distinct()
                .Select((symbol, index) => new { Symbol = symbol.Trim().ToUpper(), Index = index })
                .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                .GroupBy(x => x.Index / MAX_BATCH_SIZE)
                .Select(g => g.Select(x => x.Symbol).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                await _semaphore.WaitAsync();
                try
                {
                    foreach (var symbol in batch)
                    {
                        var cacheKey = $"HistoricalData_{symbol}_{daysBack}";
                        if (_cache.TryGetValue(cacheKey, out List<StockDataPoint> cachedData))
                        {
                            result[symbol] = cachedData;
                            continue;
                        }

                        var retryCount = 0;
                        while (retryCount < MAX_RETRIES)
                        {
                            try
                            {
                                var url = $"/v2/aggs/ticker/{WebUtility.UrlEncode(symbol)}/range/1/day/{startDate:yyyy-MM-dd}/{today:yyyy-MM-dd}";
                                var queryParams = new Dictionary<string, string>
                                {
                                    ["adjusted"] = "true",
                                    ["sort"] = "asc",
                                    ["limit"] = "50000"
                                };

                                var response = await SendApiRequestAsync(url, queryParams);
                                if (response == null) break;

                                var results = response["results"] as JArray;
                                if (results != null && results.Any())
                                {
                                    var dataPoints = ProcessStockDataPoints(results);
                                    result[symbol] = dataPoints;

                                    // Only cache if we have valid data
                                    if (dataPoints.Any())
                                    {
                                        var cacheOptions = new MemoryCacheEntryOptions()
                                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES))
                                            .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                                        _cache.Set(cacheKey, dataPoints, cacheOptions);
                                    }
                                    break;
                                }

                                retryCount++;
                                if (retryCount < MAX_RETRIES)
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount))); // Exponential backoff
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error fetching data for {Symbol} on attempt {Attempt}", symbol, retryCount + 1);
                                retryCount++;
                                if (retryCount >= MAX_RETRIES) throw;
                                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                            }
                        }

                        // Add delay between requests in the same batch
                        await Task.Delay(200);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            return result;
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

                var response = await SendApiRequestAsync(url, queryParams);
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
                _cache.Set(cacheKey, allStocks, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
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

            // Process in smaller batches
            var batches = distinctSymbols
                .Select((symbol, index) => new { Symbol = symbol, Index = index })
                .GroupBy(x => x.Index / MAX_BATCH_SIZE)
                .Select(g => g.Select(x => x.Symbol).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                try
                {
                    await _rateLimiter.WaitForAvailableSlotAsync();

                    var symbols = string.Join(",", batch);
                    var cacheKey = $"RealTimePrices_{symbols}";

                    // Check cache first
                    if (_cache.TryGetValue(cacheKey, out Dictionary<string, decimal> cachedPrices))
                    {
                        foreach (var kvp in cachedPrices)
                        {
                            prices[kvp.Key] = kvp.Value;
                        }
                        continue;
                    }

                    var url = "/v2/snapshot/locale/us/markets/stocks/tickers";
                    var queryParams = new Dictionary<string, string>
                    {
                        ["tickers"] = symbols
                    };

                    var response = await SendApiRequestAsync(url, queryParams);
                    if (response == null) continue;

                    var tickers = response["tickers"] as JArray;
                    if (tickers != null)
                    {
                        var batchPrices = new Dictionary<string, decimal>();
                        foreach (var ticker in tickers)
                        {
                            var symbol = ticker["ticker"].ToString();
                            if (ticker["day"] != null && ticker["day"]["c"] != null)
                            {
                                var price = ticker["day"]["c"].Value<decimal>();
                                if (price > 0)
                                {
                                    prices[symbol] = price;
                                    batchPrices[symbol] = price;
                                    _logger.LogInformation($"Retrieved price for {symbol}: {price}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Received zero price for {symbol}");
                                }
                            }
                        }

                        // Cache batch results
                        if (batchPrices.Any())
                        {
                            _cache.Set(cacheKey, batchPrices, TimeSpan.FromMinutes(1));
                        }
                    }

                    // Add delay between batches
                    if (batches.Count > 1)
                    {
                        await Task.Delay(200);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching real-time prices for batch {Symbols}", string.Join(", ", batch));
                }
            }

            return prices;
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
                        _cache.Set(cacheKey, companyDetails, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
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
            var retryCount = 0;
            while (retryCount < MAX_RETRIES)
            {
                try
                {
                    await _rateLimiter.WaitForAvailableSlotAsync();

                    queryParams ??= new Dictionary<string, string>();
                    queryParams["apiKey"] = _apiKey;

                    var query = string.Join("&", queryParams.Select(p =>
                        $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                    var url = $"{endpoint}?{query}";

                    using var response = await _httpClient.GetAsync(url);

                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        _logger.LogWarning("Rate limit exceeded for endpoint: {Endpoint}", endpoint);
                        var retryAfterSeconds = 60;

                        if (response.Headers.TryGetValues("Retry-After", out var values))
                        {
                            if (int.TryParse(values.FirstOrDefault(), out int headerValue))
                            {
                                retryAfterSeconds = headerValue;
                            }
                        }

                        await Task.Delay(retryAfterSeconds * 1000);
                        retryCount++;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.LogError("API authentication failed for endpoint: {Endpoint}", endpoint);
                        throw new UnauthorizedAccessException("API authentication failed");
                    }

                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();

                    try
                    {
                        return JObject.Parse(content);
                    }
                    catch (JsonReaderException ex)
                    {
                        _logger.LogError(ex, "Invalid JSON response from API: {Content}", content);
                        throw;
                    }
                }
                catch (Exception ex) when (ex is not UnauthorizedAccessException)
                {
                    _logger.LogError(ex, "Error in SendApiRequestAsync for endpoint: {Endpoint}, attempt: {Attempt}",
                        endpoint, retryCount + 1);

                    retryCount++;
                    if (retryCount >= MAX_RETRIES) throw;

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                }
            }

            throw new Exception($"Failed to get response after {MAX_RETRIES} attempts");
        }


        private List<StockDataPoint> ProcessStockDataPoints(JArray results)
        {
            var stockData = new List<StockDataPoint>();
            foreach (var item in results)
            {
                try
                {
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                        item["t"]?.Value<long>() ?? 0).UtcDateTime;

                    var dataPoint = new StockDataPoint
                    {
                        Date = timestamp,
                        Open = item["o"]?.Value<decimal>() ?? 0,
                        High = item["h"]?.Value<decimal>() ?? 0,
                        Low = item["l"]?.Value<decimal>() ?? 0,
                        Close = item["c"]?.Value<decimal>() ?? 0,
                        Volume = item["v"]?.Value<long>() ?? 0
                    };

                    // Validate data point
                    if (dataPoint.Open > 0 && dataPoint.High > 0 && dataPoint.Low > 0 &&
                        dataPoint.Close > 0 && dataPoint.High >= dataPoint.Low)
                    {
                        stockData.Add(dataPoint);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid stock data point detected: {DataPoint}",
                            JsonConvert.SerializeObject(dataPoint));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing stock data point: {Point}",
                        item?.ToString() ?? "null");
                }
            }
            return stockData;
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