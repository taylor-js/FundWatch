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

namespace FundWatch.Services
{
    public class StockService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<StockService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private const int CACHE_DURATION_MINUTES = 15;
        private const int MAX_BATCH_SIZE = 5;

        public StockService(
            IHttpClientFactory clientFactory,
            ILogger<StockService> logger,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _httpClient = clientFactory.CreateClient("PolygonApi");
            _logger = logger;
            _cache = cache;
            _apiKey = configuration["PolygonApi:ApiKey"];
        }

        public async Task<Dictionary<string, List<StockDataPoint>>> GetRealTimeDataAsync(List<string> stockSymbols, int daysBack = 365)
        {
            if (stockSymbols == null || !stockSymbols.Any())
            {
                _logger.LogWarning("GetRealTimeDataAsync called with empty or null stock symbols");
                return new Dictionary<string, List<StockDataPoint>>();
            }

            var cacheKey = $"HistoricalData_{string.Join("_", stockSymbols)}_{daysBack}";
            if (_cache.TryGetValue(cacheKey, out Dictionary<string, List<StockDataPoint>> cachedData))
            {
                return cachedData;
            }

            var result = new Dictionary<string, List<StockDataPoint>>();
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(-daysBack);

            foreach (var symbol in stockSymbols)
            {
                try
                {
                    var url = $"/v2/aggs/ticker/{symbol}/range/1/day/{startDate:yyyy-MM-dd}/{today:yyyy-MM-dd}";
                    var queryParams = new Dictionary<string, string>
                    {
                        ["adjusted"] = "true",
                        ["sort"] = "asc"
                    };

                    var response = await SendApiRequestAsync(url, queryParams);
                    if (response == null) continue;

                    var results = response["results"] as JArray;
                    if (results != null)
                    {
                        result[symbol] = ProcessStockDataPoints(results);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching real-time data for {Symbol}", symbol);
                }
            }

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
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

            // Check cache first
            var cacheKey = $"RealTimePrices_{string.Join("_", distinctSymbols)}";
            if (_cache.TryGetValue(cacheKey, out Dictionary<string, decimal> cachedPrices))
            {
                return cachedPrices;
            }

            var batches = distinctSymbols
                .Select((symbol, index) => new { Symbol = symbol, Index = index })
                .GroupBy(x => x.Index / MAX_BATCH_SIZE)
                .Select(g => g.Select(x => x.Symbol).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                try
                {
                    var symbols = string.Join(",", batch);
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
                        foreach (var ticker in tickers)
                        {
                            var symbol = ticker["ticker"].ToString();
                            if (ticker["day"] != null && ticker["day"]["c"] != null)
                            {
                                var price = ticker["day"]["c"].Value<decimal>();
                                if (price > 0)
                                {
                                    prices[symbol] = price;
                                    _logger.LogInformation($"Retrieved price for {symbol}: {price}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Received zero price for {symbol}");
                                }
                            }
                        }
                    }

                    // Add small delay between batches to avoid rate limiting
                    if (batches.Count > 1)
                    {
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching real-time prices for batch {Symbols}", string.Join(", ", batch));
                }
            }

            if (prices.Any())
            {
                // Cache the results
                _cache.Set(cacheKey, prices, TimeSpan.FromMinutes(1)); // Short cache duration for real-time prices
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
            try
            {
                var query = queryParams != null ?
                    "?" + string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")) :
                    string.Empty;

                var response = await _httpClient.GetAsync(endpoint + query);

                // Handle rate limiting
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    _logger.LogWarning("Rate limit exceeded for endpoint: {Endpoint}", endpoint);
                    // Optionally wait and retry
                    if (response.Headers.TryGetValues("Retry-After", out var values))
                    {
                        if (int.TryParse(values.FirstOrDefault(), out int retryAfterSeconds))
                        {
                            _logger.LogInformation("Retrying after {Seconds} seconds", retryAfterSeconds);
                            await Task.Delay(retryAfterSeconds * 1000);
                            response = await _httpClient.GetAsync(endpoint + query);
                        }
                    }
                    else
                    {
                        // Default delay if 'Retry-After' is not provided
                        await Task.Delay(1000);
                        response = await _httpClient.GetAsync(endpoint + query);
                    }
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JObject.Parse(json);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed for endpoint: {Endpoint}", endpoint);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error for endpoint: {Endpoint}", endpoint);
                return null;
            }
        }


        private List<StockDataPoint> ProcessStockDataPoints(JArray results)
        {
            var stockData = new List<StockDataPoint>();
            foreach (var item in results)
            {
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(item["t"].Value<long>()).UtcDateTime;
                stockData.Add(new StockDataPoint
                {
                    Date = timestamp,
                    Open = item["o"]?.Value<decimal>() ?? 0,
                    High = item["h"]?.Value<decimal>() ?? 0,
                    Low = item["l"]?.Value<decimal>() ?? 0,
                    Close = item["c"]?.Value<decimal>() ?? 0,
                    Volume = item["v"]?.Value<long>() ?? 0
                });
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