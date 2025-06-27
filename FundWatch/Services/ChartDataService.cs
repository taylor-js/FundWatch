using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FundWatch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace FundWatch.Services
{
    public class ChartDataService
    {
        private readonly StockService _stockService;
        private readonly ILogger<ChartDataService> _logger;
        private readonly IMemoryCache _cache;
        private const int CACHE_DURATION_MINUTES = 60;

        public ChartDataService(
            StockService stockService,
            ILogger<ChartDataService> logger,
            IMemoryCache cache)
        {
            _stockService = stockService;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Calculate monthly performance data for user stocks compared to benchmark (SPY)
        /// </summary>
        public async Task<List<MonthlyPerformanceData>> CalculateMonthlyPerformanceAsync(List<AppUserStock> userStocks)
        {
            try
            {
                var cacheKey = $"MonthlyPerformance_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<MonthlyPerformanceData> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                // Get last 6 months from today
                var currentDate = DateTime.Today;
                var months = Enumerable.Range(0, 6)
                    .Select(i => currentDate.AddMonths(-i))
                    .OrderBy(d => d)
                    .ToList();

                // Get historical data for all stocks and benchmark
                var symbols = userStocks.Select(s => s.StockSymbol).ToList();
                symbols.Add("SPY"); // Add S&P 500 ETF as benchmark

                var historicalData = await _stockService.GetRealTimeDataAsync(symbols, 200); // ~6 months of trading days

                // Calculate monthly performance data
                var monthlyData = new ConcurrentBag<MonthlyPerformanceData>();

                // Process months in parallel
                Parallel.ForEach(months, month =>
                {
                    var firstDayOfMonth = new DateTime(month.Year, month.Month, 1);
                    var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                    // Calculate portfolio performance for this month
                    decimal portfolioStartValue = 0;
                    decimal portfolioEndValue = 0;

                    foreach (var stock in userStocks)
                    {
                        if (historicalData.TryGetValue(stock.StockSymbol, out var dataPoints) && dataPoints.Any())
                        {
                            // Find closing prices closest to start and end of month
                            var startPoint = dataPoints
                                .Where(dp => dp.Date <= firstDayOfMonth)
                                .OrderByDescending(dp => dp.Date)
                                .FirstOrDefault();

                            var endPoint = dataPoints
                                .Where(dp => dp.Date <= lastDayOfMonth)
                                .OrderByDescending(dp => dp.Date)
                                .FirstOrDefault();

                            if (startPoint != null && endPoint != null)
                            {
                                decimal startPrice = startPoint.Close;
                                decimal endPrice = endPoint.Close;
                                
                                // Only include if stock was owned at that time
                                if (stock.DatePurchased <= lastDayOfMonth && 
                                    (!stock.DateSold.HasValue || stock.DateSold >= firstDayOfMonth))
                                {
                                    decimal shares = stock.NumberOfSharesPurchased - 
                                        (stock.DateSold.HasValue && stock.DateSold <= lastDayOfMonth 
                                            ? (decimal)(stock.NumberOfSharesSold ?? 0)
                                            : 0);
                                            
                                    portfolioStartValue += startPrice * shares;
                                    portfolioEndValue += endPrice * shares;
                                }
                            }
                        }
                    }

                    // Calculate benchmark (SPY) performance for this month
                    decimal benchmarkStartValue = 0;
                    decimal benchmarkEndValue = 0;

                    if (historicalData.TryGetValue("SPY", out var spyDataPoints) && spyDataPoints.Any())
                    {
                        var startPoint = spyDataPoints
                            .Where(dp => dp.Date <= firstDayOfMonth)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();

                        var endPoint = spyDataPoints
                            .Where(dp => dp.Date <= lastDayOfMonth)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();

                        if (startPoint != null && endPoint != null)
                        {
                            benchmarkStartValue = startPoint.Close;
                            benchmarkEndValue = endPoint.Close;
                        }
                    }

                    // Calculate percentage changes
                    decimal portfolioPerformance = portfolioStartValue > 0 
                        ? Math.Round(((portfolioEndValue / portfolioStartValue) - 1) * 100, 2) 
                        : 0;
                        
                    decimal benchmarkPerformance = benchmarkStartValue > 0 
                        ? Math.Round(((benchmarkEndValue / benchmarkStartValue) - 1) * 100, 2) 
                        : 0;

                    monthlyData.Add(new MonthlyPerformanceData
                    {
                        Month = month.ToString("MMM"),
                        PortfolioPerformance = portfolioPerformance,
                        BenchmarkPerformance = benchmarkPerformance
                    });
                });

                // Sort the data by month order
                var sortedMonthlyData = monthlyData.OrderBy(m => months.FindIndex(d => d.ToString("MMM") == m.Month)).ToList();

                // Cache the data
                _cache.Set(cacheKey, sortedMonthlyData, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return sortedMonthlyData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating monthly performance data");
                return new List<MonthlyPerformanceData>();
            }
        }

        /// <summary>
        /// Calculate rolling returns for different time periods (1M, 3M, 6M, 1Y, 3Y)
        /// </summary>
        public async Task<List<RollingReturnsData>> CalculateRollingReturnsAsync(List<AppUserStock> userStocks)
        {
            try
            {
                var cacheKey = $"RollingReturns_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<RollingReturnsData> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                // Define time periods for rolling returns
                var periods = new Dictionary<string, int>
                {
                    { "1M", 30 },
                    { "3M", 90 },
                    { "6M", 180 },
                    { "1Y", 365 },
                    { "3Y", 1095 }
                };

                var currentDate = DateTime.Today;
                var symbols = userStocks.Select(s => s.StockSymbol).ToList();
                symbols.Add("SPY"); // Add benchmark

                // Fetch historical data for all stocks
                var historicalData = await _stockService.GetRealTimeDataAsync(symbols, 1095); // 3 years

                var rollingReturns = new ConcurrentBag<RollingReturnsData>();

                // Process periods in parallel
                Parallel.ForEach(periods, period =>
                {
                    var startDate = currentDate.AddDays(-period.Value);
                    
                    // Calculate portfolio return for this period
                    decimal portfolioStartValue = 0;
                    decimal portfolioCurrentValue = 0;

                    foreach (var stock in userStocks)
                    {
                        // Skip stocks purchased after the start date of the period
                        if (stock.DatePurchased > startDate)
                            continue;
                            
                        if (historicalData.TryGetValue(stock.StockSymbol, out var dataPoints) && dataPoints.Any())
                        {
                            // Find price at start of period (or purchase date if later)
                            var effectiveStartDate = stock.DatePurchased > startDate ? stock.DatePurchased : startDate;
                            
                            var startPoint = dataPoints
                                .Where(dp => dp.Date <= effectiveStartDate)
                                .OrderByDescending(dp => dp.Date)
                                .FirstOrDefault();

                            // Find latest price (or sale price if sold)
                            var endDate = stock.DateSold ?? currentDate;
                            var endPoint = dataPoints
                                .Where(dp => dp.Date <= endDate)
                                .OrderByDescending(dp => dp.Date)
                                .FirstOrDefault();

                            if (startPoint != null && endPoint != null)
                            {
                                decimal shares = stock.NumberOfSharesPurchased - 
                                    (stock.DateSold.HasValue ? (decimal)(stock.NumberOfSharesSold ?? 0) : 0);
                                    
                                portfolioStartValue += startPoint.Close * shares;
                                portfolioCurrentValue += endPoint.Close * shares;
                            }
                        }
                    }

                    // Calculate benchmark (SPY) return
                    decimal benchmarkReturn = 0;
                    
                    if (historicalData.TryGetValue("SPY", out var spyDataPoints) && spyDataPoints.Any())
                    {
                        var startPoint = spyDataPoints
                            .Where(dp => dp.Date <= startDate)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();

                        var endPoint = spyDataPoints
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();

                        if (startPoint != null && endPoint != null)
                        {
                            benchmarkReturn = Math.Round(((endPoint.Close / startPoint.Close) - 1) * 100, 2);
                        }
                    }

                    // Calculate portfolio return
                    decimal portfolioReturn = portfolioStartValue > 0 
                        ? Math.Round(((portfolioCurrentValue / portfolioStartValue) - 1) * 100, 2) 
                        : 0;

                    rollingReturns.Add(new RollingReturnsData
                    {
                        TimePeriod = period.Key,
                        PortfolioReturn = portfolioReturn,
                        BenchmarkReturn = benchmarkReturn
                    });
                });

                // Sort by predefined period order
                var periodOrder = new[] { "1M", "3M", "6M", "1Y", "3Y" };
                var sortedRollingReturns = rollingReturns
                    .OrderBy(r => Array.IndexOf(periodOrder, r.TimePeriod))
                    .ToList();

                // Cache the data
                _cache.Set(cacheKey, sortedRollingReturns, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return sortedRollingReturns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating rolling returns");
                return new List<RollingReturnsData>();
            }
        }

        /// <summary>
        /// Calculate portfolio growth data over time compared to benchmark using a sliding window.
        /// Implements date-based caching to ensure the growth chart maintains a proper
        /// time-based sliding window that updates as days progress.
        /// </summary>
        public async Task<List<PortfolioGrowthPoint>> CalculatePortfolioGrowthAsync(List<AppUserStock> userStocks, int days = 1825)
        {
            try
            {
                // TIME-AWARE CACHE KEY: Include today's date to ensure cache invalidation
                // when the sliding window should move forward. This prevents growth charts
                // from showing outdated date ranges.
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var cacheKey = $"PortfolioGrowth_{today}_{days}_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<PortfolioGrowthPoint> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                var endDate = DateTime.Today;
                var startDate = endDate.AddDays(-days);

                // Create a list of all trading days in the period
                // For 5 years of data, use monthly intervals to reduce data points
                var allDates = new List<DateTime>();
                
                if (days > 365) 
                {
                    // For multi-year views, use monthly intervals
                    for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                    {
                        allDates.Add(date);
                    }
                }
                else 
                {
                    // For less than a year, use daily intervals
                    for (var date = startDate; date <= endDate; date = date.AddDays(1))
                    {
                        // Skip weekends
                        if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                        {
                            allDates.Add(date);
                        }
                    }
                }

                // Get all stock symbols including benchmark
                var symbols = userStocks.Select(s => s.StockSymbol).ToList();
                symbols.Add("SPY"); // Benchmark

                var historicalData = await _stockService.GetRealTimeDataAsync(symbols, days);

                // Normalize benchmark to start at 100
                decimal benchmarkStartValue = 0;
                if (historicalData.TryGetValue("SPY", out var spyData) && spyData.Any())
                {
                    var firstPoint = spyData.OrderBy(dp => dp.Date).FirstOrDefault();
                    if (firstPoint != null)
                    {
                        benchmarkStartValue = firstPoint.Close;
                    }
                }

                // Calculate portfolio value for each day
                var growthData = new List<PortfolioGrowthPoint>();
                
                // Track initial portfolio value for normalization
                decimal initialPortfolioValue = 0;
                bool firstPointSet = false;

                foreach (var date in allDates)
                {
                    decimal portfolioValue = 0;
                    decimal benchmarkValue = 0;

                    // Calculate portfolio value on this date
                    foreach (var stock in userStocks)
                    {
                        // Skip if stock was purchased after this date
                        if (stock.DatePurchased > date)
                            continue;
                            
                        // Skip if stock was sold before this date
                        if (stock.DateSold.HasValue && stock.DateSold < date)
                            continue;
                            
                        if (historicalData.TryGetValue(stock.StockSymbol, out var stockData) && stockData.Any())
                        {
                            // Find the closest price before or on this date
                            var pricePoint = stockData
                                .Where(dp => dp.Date <= date)
                                .OrderByDescending(dp => dp.Date)
                                .FirstOrDefault();
                                
                            if (pricePoint != null)
                            {
                                // Calculate shares owned on this date
                                decimal shares = stock.NumberOfSharesPurchased;
                                
                                // If stock was sold on this date, adjust shares
                                if (stock.DateSold.HasValue && stock.DateSold.Value.Date == date.Date)
                                {
                                    shares -= (decimal)(stock.NumberOfSharesSold ?? 0);
                                }
                                
                                portfolioValue += pricePoint.Close * shares;
                            }
                        }
                    }

                    // Get benchmark value for this date
                    if (historicalData.TryGetValue("SPY", out var benchmarkData) && benchmarkData.Any())
                    {
                        var pricePoint = benchmarkData
                            .Where(dp => dp.Date <= date)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();
                            
                        if (pricePoint != null && benchmarkStartValue > 0)
                        {
                            // Normalize to percentage growth from start
                            benchmarkValue = 100 * (pricePoint.Close / benchmarkStartValue);
                        }
                    }

                    // Save initial portfolio value for normalization
                    if (!firstPointSet && portfolioValue > 0)
                    {
                        initialPortfolioValue = portfolioValue;
                        firstPointSet = true;
                    }

                    // Only add points where we have actual data
                    if (portfolioValue > 0 && benchmarkValue > 0)
                    {
                        // Normalize portfolio value to start at 100 (same scale as benchmark)
                        decimal normalizedPortfolioValue = initialPortfolioValue > 0 
                            ? 100 * (portfolioValue / initialPortfolioValue)
                            : 0;
                            
                        growthData.Add(new PortfolioGrowthPoint
                        {
                            Date = date,
                            PortfolioValue = normalizedPortfolioValue,
                            BenchmarkValue = benchmarkValue
                        });
                    }
                }

                // Cache the data
                _cache.Set(cacheKey, growthData, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return growthData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating portfolio growth data");
                return new List<PortfolioGrowthPoint>();
            }
        }

        /// <summary>
        /// Calculate risk metrics for portfolio and individual stocks using a sliding window approach.
        /// Implements date-based cache invalidation to ensure metrics update as the 5-year window moves.
        /// </summary>
        public async Task<List<RiskAnalysisData>> CalculateRiskMetricsAsync(List<AppUserStock> userStocks)
        {
            try
            {
                // DATE-BASED CACHE KEY: Include today's date to ensure cache invalidation
                // when the sliding window moves forward. This prevents stale risk metrics
                // from being returned when the 5-year calculation window should update.
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var cacheKey = $"RiskMetrics_{today}_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<RiskAnalysisData> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                // Get 1 year of daily data
                var symbols = userStocks.Select(s => s.StockSymbol).ToList();
                symbols.Add("SPY"); // Benchmark for beta calculation
                
                var historicalDataConcurrent = await _stockService.GetRealTimeDataAsync(symbols, 365);
                
                // Convert to Dictionary for backward compatibility with our helper methods
                var historicalData = new Dictionary<string, List<StockDataPoint>>();
                foreach (var pair in historicalDataConcurrent)
                {
                    historicalData[pair.Key] = pair.Value;
                }
                
                var riskMetrics = new ConcurrentBag<RiskAnalysisData>();
                
                // Calculate metrics for each stock in parallel
                Parallel.ForEach(userStocks, stock =>
                {
                    if (historicalData.TryGetValue(stock.StockSymbol, out var stockData) && 
                        historicalData.TryGetValue("SPY", out var benchmarkData) &&
                        stockData.Count >= 30 && benchmarkData.Count >= 30)
                    {
                        // Calculate daily returns for stock and benchmark
                        var stockReturns = CalculateDailyReturns(stockData);
                        var benchmarkReturns = CalculateDailyReturns(benchmarkData);
                        
                        // Calculate volatility (annualized standard deviation of returns)
                        decimal volatility = CalculateVolatility(stockReturns);
                        
                        // Calculate beta (correlation with market * stock volatility / market volatility)
                        decimal beta = CalculateBeta(stockReturns, benchmarkReturns);
                        
                        // Calculate Sharpe ratio (return / risk, using average return - risk-free rate)
                        decimal averageReturn = stockReturns.Count > 0 ? stockReturns.Average() : 0;
                        decimal riskFreeRate = 0.03m / 252; // Assuming 3% annual risk-free rate
                        decimal sharpeRatio = volatility > 0 
                            ? (averageReturn - riskFreeRate) / volatility * (decimal)Math.Sqrt(252) 
                            : 0;
                            
                        // Calculate maximum drawdown
                        decimal maxDrawdown = CalculateMaxDrawdown(stockData);
                        
                        riskMetrics.Add(new RiskAnalysisData
                        {
                            Symbol = stock.StockSymbol,
                            Volatility = Math.Round(volatility * 100, 2), // Convert to percentage
                            Beta = Math.Round(beta, 2),
                            SharpeRatio = Math.Round(sharpeRatio, 2),
                            MaxDrawdown = Math.Round(maxDrawdown * 100, 2) // Convert to percentage
                        });
                    }
                });
                
                // Also calculate portfolio-level risk metrics
                if (userStocks.Any() && historicalData.TryGetValue("SPY", out var spyData))
                {
                    // Dictionary type is already correct for this method
                    var portfolioValues = CalculatePortfolioDailyValues(userStocks, historicalData);
                    
                    if (portfolioValues.Count >= 30)
                    {
                        var portfolioReturns = CalculateDailyReturnsFromValues(portfolioValues);
                        var benchmarkReturns = CalculateDailyReturns(spyData);
                        
                        decimal volatility = CalculateVolatility(portfolioReturns);
                        decimal beta = CalculateBeta(portfolioReturns, benchmarkReturns);
                        
                        decimal averageReturn = portfolioReturns.Count > 0 ? portfolioReturns.Average() : 0;
                        decimal riskFreeRate = 0.03m / 252; // Assuming 3% annual risk-free rate
                        decimal sharpeRatio = volatility > 0 
                            ? (averageReturn - riskFreeRate) / volatility * (decimal)Math.Sqrt(252) 
                            : 0;
                            
                        decimal maxDrawdown = CalculateMaxDrawdownFromValues(portfolioValues);
                        
                        // Convert to list and add portfolio at the beginning
                        var riskMetricsList = riskMetrics.ToList();
                        riskMetricsList.Insert(0, new RiskAnalysisData
                        {
                            Symbol = "Portfolio",
                            Volatility = Math.Round(volatility * 100, 2),
                            Beta = Math.Round(beta, 2),
                            SharpeRatio = Math.Round(sharpeRatio, 2),
                            MaxDrawdown = Math.Round(maxDrawdown * 100, 2)
                        });
                        
                        // Cache the data
                        _cache.Set(cacheKey, riskMetricsList, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                        
                        return riskMetricsList;
                    }
                }
                
                // If no portfolio-level metrics, return the individual stock metrics
                var sortedRiskMetrics = riskMetrics.OrderBy(r => r.Symbol).ToList();
                _cache.Set(cacheKey, sortedRiskMetrics, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                return sortedRiskMetrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating risk metrics");
                return new List<RiskAnalysisData>();
            }
        }
        

        /// Calculate diversification data for portfolio  
        /// </summary>  
        public async Task<List<DiversificationData>> CalculateDiversificationAsync(List<AppUserStock> userStocks)
        {
            try
            {
                var cacheKey = $"Diversification_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<DiversificationData>? cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                // Get company details for each stock to determine industries (fixing the Sector issue)  
                var symbols = userStocks.Select(s => s.StockSymbol).Distinct().ToList();
                var companyDetailsTask = _stockService.GetCompanyDetailsAsync(symbols);
                var pricesTask = _stockService.GetRealTimePricesAsync(symbols);

                await Task.WhenAll(companyDetailsTask, pricesTask);

                var companyDetails = await companyDetailsTask;
                var prices = await pricesTask;

                // Group by industry and calculate total value using PLINQ
                var industryGroups = userStocks
                    .AsParallel()
                    .Where(stock =>
                    {
                        var sharesOwned = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);
                        return sharesOwned > 0 && companyDetails.ContainsKey(stock.StockSymbol);
                    })
                    .GroupBy(stock =>
                    {
                        var detail = companyDetails[stock.StockSymbol];
                        return !string.IsNullOrWhiteSpace(detail.Industry) ? detail.Industry : "Unknown";
                    })
                    .Select(group =>
                    {
                        decimal totalValue = group.Sum(stock =>
                        {
                            var sharesOwned = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);
                            var currentPrice = prices.ContainsKey(stock.StockSymbol)
                                ? prices[stock.StockSymbol]
                                : stock.CurrentPrice;
                            return sharesOwned * currentPrice;
                        });

                        return new DiversificationData
                        {
                            Name = group.Key,
                            Y = Math.Round(totalValue, 2)
                        };
                    })
                    .OrderByDescending(x => x.Y)
                    .ToList();

                // Cache the data  
                _cache.Set(cacheKey, industryGroups, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return industryGroups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating diversification data");
                return new List<DiversificationData>();
            }
        }

        /// <summary>
        /// Calculate drawdown data points for portfolio and benchmark using a 5-year sliding window.
        /// This method implements proper cache invalidation to ensure drawdown charts update
        /// correctly as time progresses and prevents data from disappearing.
        /// </summary>
        public async Task<List<DrawdownPoint>> CalculateDrawdownSeriesAsync(List<AppUserStock> userStocks)
        {
            try
            {
                // SLIDING WINDOW CACHE KEY: Include today's date in the cache key to ensure
                // that cached drawdown data becomes invalid when the date changes.
                // This is critical for maintaining the 5-year sliding window behavior.
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var cacheKey = $"Drawdown_{today}_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<DrawdownPoint> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                // Get 5 years of data (1825 days) instead of just 1 year
                var symbols = userStocks.Select(s => s.StockSymbol).ToList();
                symbols.Add("SPY"); // Benchmark

                var historicalDataConcurrent = await _stockService.GetRealTimeDataAsync(symbols, 1825);

                // Convert to Dictionary for backward compatibility with our helper methods
                var historicalData = new Dictionary<string, List<StockDataPoint>>();
                foreach (var pair in historicalDataConcurrent)
                {
                    historicalData[pair.Key] = pair.Value;
                }
                
                // Calculate portfolio values for each day
                var portfolioValues = CalculatePortfolioDailyValues(userStocks, historicalData);
                
                // Calculate drawdown series
                var drawdownSeries = new List<DrawdownPoint>();
                
                if (portfolioValues.Count > 0 && historicalData.TryGetValue("SPY", out var benchmarkData))
                {
                    // Get ordered list of dates from portfolio values
                    var dates = portfolioValues.Keys.OrderBy(d => d).ToList();
                    
                    decimal portfolioPeak = decimal.MinValue;
                    decimal benchmarkPeak = decimal.MinValue;
                    
                    foreach (var date in dates)
                    {
                        // Get portfolio value for this date
                        decimal portfolioValue = portfolioValues[date];
                        
                        // Update portfolio peak if new high
                        if (portfolioValue > portfolioPeak)
                        {
                            portfolioPeak = portfolioValue;
                        }
                        
                        // Calculate portfolio drawdown
                        decimal portfolioDrawdown = portfolioPeak > 0 
                            ? (portfolioValue / portfolioPeak) - 1 
                            : 0;
                        
                        // Get benchmark value for this date
                        var benchmarkPoint = benchmarkData
                            .Where(dp => dp.Date.Date <= date)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();
                            
                        decimal benchmarkDrawdown = 0;
                        
                        if (benchmarkPoint != null)
                        {
                            decimal benchmarkValue = benchmarkPoint.Close;
                            
                            // Update benchmark peak if new high
                            if (benchmarkValue > benchmarkPeak)
                            {
                                benchmarkPeak = benchmarkValue;
                            }
                            
                            // Calculate benchmark drawdown
                            benchmarkDrawdown = benchmarkPeak > 0 
                                ? (benchmarkValue / benchmarkPeak) - 1 
                                : 0;
                        }
                        
                        // Add data point
                        drawdownSeries.Add(new DrawdownPoint
                        {
                            Date = date,
                            PortfolioDrawdown = Math.Round(portfolioDrawdown * 100, 2), // Convert to percentage
                            BenchmarkDrawdown = Math.Round(benchmarkDrawdown * 100, 2)  // Convert to percentage
                        });
                    }
                }
                
                // If we don't have sufficient data, log an error and return empty dataset
                if (drawdownSeries.Count == 0)
                {
                    _logger.LogWarning("No drawdown data could be calculated for user portfolio");
                }
                
                // Cache the data
                _cache.Set(cacheKey, drawdownSeries, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                return drawdownSeries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating drawdown series");
                return new List<DrawdownPoint>();
            }
        }

        #region Helper Methods
        
        // Calculate daily returns from OHLC data
        private List<decimal> CalculateDailyReturns(List<StockDataPoint> dataPoints)
        {
            var returns = new List<decimal>();
            
            if (dataPoints.Count <= 1)
                return returns;
                
            var orderedData = dataPoints.OrderBy(dp => dp.Date).ToList();
            
            for (int i = 1; i < orderedData.Count; i++)
            {
                decimal previousClose = orderedData[i - 1].Close;
                decimal currentClose = orderedData[i].Close;
                
                if (previousClose > 0)
                {
                    decimal dailyReturn = (currentClose / previousClose) - 1;
                    returns.Add(dailyReturn);
                }
            }
            
            return returns;
        }
        
        // Calculate daily returns from a dictionary of date/value pairs
        private List<decimal> CalculateDailyReturnsFromValues(Dictionary<DateTime, decimal> values)
        {
            var returns = new List<decimal>();
            
            if (values.Count <= 1)
                return returns;
                
            var orderedDates = values.Keys.OrderBy(d => d).ToList();
            
            for (int i = 1; i < orderedDates.Count; i++)
            {
                decimal previousValue = values[orderedDates[i - 1]];
                decimal currentValue = values[orderedDates[i]];
                
                if (previousValue > 0)
                {
                    decimal dailyReturn = (currentValue / previousValue) - 1;
                    returns.Add(dailyReturn);
                }
            }
            
            return returns;
        }
        
        // Calculate volatility (annualized standard deviation)
        private decimal CalculateVolatility(List<decimal> returns)
        {
            if (returns.Count <= 1)
                return 0;
                
            decimal mean = returns.Average();
            decimal sum = returns.Sum(r => (r - mean) * (r - mean));
            decimal variance = sum / (returns.Count - 1);
            decimal stdDev = (decimal)Math.Sqrt((double)variance);
            
            // Annualize (assuming daily returns, multiply by sqrt(252))
            return stdDev * (decimal)Math.Sqrt(252);
        }
        
        // Calculate beta (correlation with market * volatility ratio)
        private decimal CalculateBeta(List<decimal> stockReturns, List<decimal> marketReturns)
        {
            // Ensure we have matching data points
            int n = Math.Min(stockReturns.Count, marketReturns.Count);
            
            if (n <= 1)
                return 1.0m; // Default to market beta
                
            stockReturns = stockReturns.Take(n).ToList();
            marketReturns = marketReturns.Take(n).ToList();
            
            decimal stockMean = stockReturns.Average();
            decimal marketMean = marketReturns.Average();
            
            decimal covariance = 0;
            decimal marketVariance = 0;
            
            for (int i = 0; i < n; i++)
            {
                covariance += (stockReturns[i] - stockMean) * (marketReturns[i] - marketMean);
                marketVariance += (marketReturns[i] - marketMean) * (marketReturns[i] - marketMean);
            }
            
            covariance /= (n - 1);
            marketVariance /= (n - 1);
            
            if (marketVariance == 0)
                return 1.0m; // Avoid division by zero
                
            return covariance / marketVariance;
        }
        
        // Calculate maximum drawdown from OHLC data
        private decimal CalculateMaxDrawdown(List<StockDataPoint> dataPoints)
        {
            if (dataPoints.Count <= 1)
                return 0;
                
            var orderedData = dataPoints.OrderBy(dp => dp.Date).ToList();
            
            decimal peak = orderedData[0].Close;
            decimal maxDrawdown = 0;
            
            foreach (var point in orderedData)
            {
                if (point.Close > peak)
                {
                    peak = point.Close;
                }
                
                if (peak > 0)
                {
                    decimal drawdown = (point.Close / peak) - 1;
                    
                    // Drawdown is negative, so take the minimum (largest absolute drawdown)
                    if (drawdown < maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                    }
                }
            }
            
            return maxDrawdown;
        }
        
        // Calculate maximum drawdown from value series
        private decimal CalculateMaxDrawdownFromValues(Dictionary<DateTime, decimal> values)
        {
            if (values.Count <= 1)
                return 0;
                
            var orderedDates = values.Keys.OrderBy(d => d).ToList();
            
            decimal peak = values[orderedDates[0]];
            decimal maxDrawdown = 0;
            
            foreach (var date in orderedDates)
            {
                decimal value = values[date];
                
                if (value > peak)
                {
                    peak = value;
                }
                
                if (peak > 0)
                {
                    decimal drawdown = (value / peak) - 1;
                    
                    // Drawdown is negative, so take the minimum
                    if (drawdown < maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                    }
                }
            }
            
            return maxDrawdown;
        }
        
        /// <summary>
        /// Calculate portfolio values for each day using a 5-year sliding window approach.
        /// This method implements a proper sliding window that automatically moves forward with time,
        /// ensuring charts always show the most recent 5 years of data and preventing data from
        /// disappearing when new stocks are added to the portfolio.
        /// </summary>
        /// <param name="userStocks">List of user's stock holdings</param>
        /// <param name="historicalData">Historical price data for all stocks</param>
        /// <returns>Dictionary mapping dates to portfolio values</returns>
        private Dictionary<DateTime, decimal> CalculatePortfolioDailyValues(
            List<AppUserStock> userStocks, 
            Dictionary<string, List<StockDataPoint>> historicalData)
        {
            var result = new Dictionary<DateTime, decimal>();
            
            if (!userStocks.Any() || !historicalData.Any())
                return result;
                
            // SLIDING WINDOW IMPLEMENTATION:
            // Define a proper 5-year sliding window that moves forward with time.
            // This ensures that as days pass, the window automatically includes new days
            // and excludes old days beyond the 5-year threshold, maintaining a consistent
            // 5-year view for risk analysis and drawdown calculations.
            var endDate = DateTime.Today;
            var startDate = endDate.AddYears(-5);
            
            // OPTIMIZATION: Only collect dates that fall within our sliding window.
            // This prevents processing of irrelevant historical data and ensures
            // the chart data remains consistent regardless of when stocks were added.
            var allDates = new HashSet<DateTime>();
            
            foreach (var stockData in historicalData.Values)
            {
                foreach (var dataPoint in stockData)
                {
                    var date = dataPoint.Date.Date;
                    // FILTERING: Only include dates within our 5-year sliding window
                    // This is crucial for preventing stale data from appearing in charts
                    if (date >= startDate && date <= endDate)
                    {
                        allDates.Add(date);
                    }
                }
            }
            
            // PORTFOLIO VALUE CALCULATION:
            // For each trading day in our sliding window, calculate the total portfolio value
            // by summing the value of all stocks owned on that date.
            foreach (var date in allDates.OrderBy(d => d))
            {
                decimal portfolioValue = 0;
                bool hasAnyStock = false; // Track if any stocks exist on this date
                
                foreach (var stock in userStocks)
                {
                    // OWNERSHIP TIMELINE: Only include stocks that were owned on this specific date
                    // Skip if stock was purchased after this date (future purchase)
                    if (stock.DatePurchased.Date > date)
                        continue;
                        
                    // Skip if stock was sold before this date (already sold)
                    if (stock.DateSold.HasValue && stock.DateSold.Value.Date < date)
                        continue;
                        
                    // CONTINUITY FLAG: Mark that at least one stock existed on this date
                    // This ensures we maintain data continuity even if portfolio value is zero
                    hasAnyStock = true;
                        
                    if (historicalData.TryGetValue(stock.StockSymbol, out var stockData) && stockData.Any())
                    {
                        // PRICE LOOKUP: Find the most recent price at or before this date
                        // This handles cases where market data might be missing for specific days
                        var pricePoint = stockData
                            .Where(dp => dp.Date.Date <= date)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();
                            
                        if (pricePoint != null)
                        {
                            // SHARE CALCULATION: Determine how many shares were owned on this date
                            decimal shares = stock.NumberOfSharesPurchased;
                            
                            // SALE ADJUSTMENT: If stock was sold on this exact date, adjust share count
                            if (stock.DateSold.HasValue && stock.DateSold.Value.Date == date)
                            {
                                shares -= (decimal)(stock.NumberOfSharesSold ?? 0);
                            }
                            
                            // VALUE ACCUMULATION: Add this stock's value to total portfolio value
                            portfolioValue += pricePoint.Close * shares;
                        }
                    }
                }
                
                // DATA CONTINUITY: Include all dates where stocks existed to prevent chart gaps.
                // This is critical for proper chart rendering - missing dates can cause
                // charts to appear empty or display incorrectly.
                if (hasAnyStock)
                {
                    // MINIMUM VALUE: Use a tiny minimum value to prevent log scale chart issues
                    // while maintaining the actual portfolio value when it's positive
                    result[date] = Math.Max(portfolioValue, 0.01m);
                }
            }
            
            return result;
        }
        
        #endregion
    }
}