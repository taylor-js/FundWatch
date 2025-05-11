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
                var monthlyData = new List<MonthlyPerformanceData>();

                foreach (var month in months)
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
                                            ? (decimal)stock.NumberOfSharesSold
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
                }

                // Cache the data
                _cache.Set(cacheKey, monthlyData, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return monthlyData;
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

                var rollingReturns = new List<RollingReturnsData>();

                foreach (var period in periods)
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
                                    (stock.DateSold.HasValue ? (decimal)stock.NumberOfSharesSold : 0);
                                    
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
                }

                // Cache the data
                _cache.Set(cacheKey, rollingReturns, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

                return rollingReturns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating rolling returns");
                return new List<RollingReturnsData>();
            }
        }

        /// <summary>
        /// Calculate portfolio growth data over time compared to benchmark
        /// </summary>
        public async Task<List<PortfolioGrowthPoint>> CalculatePortfolioGrowthAsync(List<AppUserStock> userStocks, int days = 180)
        {
            try
            {
                var cacheKey = $"PortfolioGrowth_{days}_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
                if (_cache.TryGetValue(cacheKey, out List<PortfolioGrowthPoint> cachedData) && cachedData != null)
                {
                    return cachedData;
                }

                var endDate = DateTime.Today;
                var startDate = endDate.AddDays(-days);

                // Create a list of all trading days in the period
                var allDates = new List<DateTime>();
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    // Skip weekends
                    if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        allDates.Add(date);
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
                                    shares -= (decimal)stock.NumberOfSharesSold;
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
        /// Calculate risk metrics for portfolio and individual stocks
        /// </summary>
        public async Task<List<RiskAnalysisData>> CalculateRiskMetricsAsync(List<AppUserStock> userStocks)
        {
            try
            {
                var cacheKey = $"RiskMetrics_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
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
                
                var riskMetrics = new List<RiskAnalysisData>();
                
                // Calculate metrics for each stock
                foreach (var stock in userStocks)
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
                }
                
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
                        
                        riskMetrics.Insert(0, new RiskAnalysisData
                        {
                            Symbol = "Portfolio",
                            Volatility = Math.Round(volatility * 100, 2),
                            Beta = Math.Round(beta, 2),
                            SharpeRatio = Math.Round(sharpeRatio, 2),
                            MaxDrawdown = Math.Round(maxDrawdown * 100, 2)
                        });
                    }
                }
                
                // Cache the data
                _cache.Set(cacheKey, riskMetrics, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                
                return riskMetrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating risk metrics");
                return new List<RiskAnalysisData>();
            }
        }
        
        /// <summary>
        /// Calculate drawdown data points for portfolio and benchmark (5 years of data)
        /// </summary>
        public async Task<List<DrawdownPoint>> CalculateDrawdownSeriesAsync(List<AppUserStock> userStocks)
        {
            try
            {
                var cacheKey = $"Drawdown_{string.Join("_", userStocks.Select(s => s.StockSymbol))}";
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
        
        // Calculate portfolio values for each day
        private Dictionary<DateTime, decimal> CalculatePortfolioDailyValues(
            List<AppUserStock> userStocks, 
            Dictionary<string, List<StockDataPoint>> historicalData)
        {
            var result = new Dictionary<DateTime, decimal>();
            
            if (!userStocks.Any() || !historicalData.Any())
                return result;
                
            // Get all unique dates from historical data
            var allDates = new HashSet<DateTime>();
            
            foreach (var stockData in historicalData.Values)
            {
                foreach (var dataPoint in stockData)
                {
                    allDates.Add(dataPoint.Date.Date);
                }
            }
            
            // For each date, calculate portfolio value
            foreach (var date in allDates.OrderBy(d => d))
            {
                decimal portfolioValue = 0;
                
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
                            .Where(dp => dp.Date.Date <= date)
                            .OrderByDescending(dp => dp.Date)
                            .FirstOrDefault();
                            
                        if (pricePoint != null)
                        {
                            // Calculate shares owned on this date
                            decimal shares = stock.NumberOfSharesPurchased;
                            
                            // If stock was sold on this date, adjust shares
                            if (stock.DateSold.HasValue && stock.DateSold.Value.Date == date)
                            {
                                shares -= (decimal)stock.NumberOfSharesSold;
                            }
                            
                            portfolioValue += pricePoint.Close * shares;
                        }
                    }
                }
                
                // Only add dates where portfolio had value
                if (portfolioValue > 0)
                {
                    result[date] = portfolioValue;
                }
            }
            
            return result;
        }
        
        #endregion
    }
}