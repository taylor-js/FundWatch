using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FundWatch.Models;
using FundWatch.Models.QuantitativeModels;
using FundWatch.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FundWatch.Services
{
    public class QuantitativeAnalysisService
    {
        private readonly ApplicationDbContext _context;
        private readonly StockService _stockService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<QuantitativeAnalysisService> _logger;
        
        // Treasury yield for risk-free rate (can be updated from external source)
        private const double DEFAULT_RISK_FREE_RATE = 0.045; // 4.5% annual

        public QuantitativeAnalysisService(ApplicationDbContext context, StockService stockService, IMemoryCache cache, ILogger<QuantitativeAnalysisService> logger)
        {
            _context = context;
            _stockService = stockService;
            _cache = cache;
            _logger = logger;
        }

        public class OptionsAnalysisResult
        {
            public string Symbol { get; set; }
            public string CompanyName { get; set; }
            public double CurrentPrice { get; set; }
            public double HistoricalVolatility { get; set; }
            public double DividendYield { get; set; }
            public List<OptionChainItem> OptionChain { get; set; }
            public List<(double Strike, double ImpliedVol)> VolatilitySmile { get; set; }
            public VolatilitySurface VolSurface { get; set; }
            public string MarketOutlook { get; set; } // Bullish, Bearish, Neutral based on options data
            public OptionsChartData ChartData { get; set; } // Pre-calculated chart data
        }

        public class OptionChainItem
        {
            public double StrikePrice { get; set; }
            public double CallPrice { get; set; }
            public double PutPrice { get; set; }
            public BlackScholesModel.OptionGreeks CallGreeks { get; set; }
            public BlackScholesModel.OptionGreeks PutGreeks { get; set; }
            public double CallImpliedVol { get; set; }
            public double PutImpliedVol { get; set; }
            public string Moneyness { get; set; }
            public double TimeToExpiry { get; set; }
            public double BreakEvenPrice { get; set; }
            public double MaxProfit { get; set; }
            public double MaxLoss { get; set; }
        }

        public class VolatilitySurface
        {
            public List<double> Strikes { get; set; }
            public List<double> Expirations { get; set; }
            public double[,] ImpliedVols { get; set; }
        }

        public async Task<OptionsAnalysisResult> AnalyzeStockOptions(string symbol, string userId)
        {
            var cacheKey = $"options_analysis_{symbol}_{DateTime.UtcNow:yyyyMMdd}";
            
            if (_cache.TryGetValue(cacheKey, out OptionsAnalysisResult cachedResult))
            {
                return cachedResult;
            }

            // Get current stock data
            var priceData = await _stockService.GetRealTimePricesAsync(new List<string> { symbol });
            if (!priceData.TryGetValue(symbol, out decimal currentPrice) || currentPrice == 0) return null;

            // Get company details
            var companyData = await _stockService.GetCompanyDetailsAsync(new List<string> { symbol });
            var companyName = companyData.ContainsKey(symbol) ? companyData[symbol].Name : symbol;

            // Calculate historical volatility from past data
            var historicalData = await _stockService.GetRealTimeDataAsync(new List<string> { symbol }, 365);
            List<StockDataPoint> dataPoints = null;
            double volatility;
            if (historicalData.TryGetValue(symbol, out dataPoints) && dataPoints != null)
            {
                volatility = CalculateHistoricalVolatility(dataPoints);
            }
            else
            {
                volatility = 0.25; // Default 25% volatility
            }
            
            // Get dividend yield (simplified - in production would fetch from API)
            var dividendYield = 0.02; // 2% default

            var result = new OptionsAnalysisResult
            {
                Symbol = symbol,
                CompanyName = companyName,
                CurrentPrice = (double)currentPrice,
                HistoricalVolatility = volatility,
                DividendYield = dividendYield,
                OptionChain = new List<OptionChainItem>(),
                VolatilitySmile = new List<(double, double)>()
            };

            // Generate option chain for multiple expiration dates
            var expirationDates = new[] { 30, 60, 90, 120, 180, 365 }; // Days to expiration
            var strikeMultipliers = new[] { 0.85, 0.90, 0.95, 0.97, 0.99, 1.00, 1.01, 1.03, 1.05, 1.10, 1.15 };

            foreach (var daysToExpiry in expirationDates.Take(3)) // Focus on near-term for overview
            {
                var timeToExpiry = daysToExpiry / 365.0;
                
                foreach (var multiplier in strikeMultipliers)
                {
                    var strike = Math.Round((double)currentPrice * multiplier, 2);
                    
                    var parameters = new BlackScholesModel.OptionParameters
                    {
                        StockPrice = (double)currentPrice,
                        StrikePrice = strike,
                        TimeToExpiry = timeToExpiry,
                        RiskFreeRate = DEFAULT_RISK_FREE_RATE,
                        Volatility = volatility,
                        DividendYield = dividendYield
                    };

                    var optionResult = BlackScholesModel.CalculateOptionPrice(parameters);
                    
                    // Add some market-like adjustments for realism
                    var skewAdjustment = Math.Pow(multiplier - 1, 2) * 0.3;
                    var adjustedCallVol = volatility * (1 + skewAdjustment);
                    var adjustedPutVol = volatility * (1 + skewAdjustment * 1.2); // Put skew typically higher

                    var chainItem = new OptionChainItem
                    {
                        StrikePrice = strike,
                        CallPrice = Math.Round(optionResult.CallPrice, 2),
                        PutPrice = Math.Round(optionResult.PutPrice, 2),
                        CallGreeks = optionResult.Greeks,
                        PutGreeks = CalculatePutGreeks(parameters, optionResult.Greeks),
                        CallImpliedVol = adjustedCallVol,
                        PutImpliedVol = adjustedPutVol,
                        Moneyness = optionResult.ProfitabilityStatus,
                        TimeToExpiry = timeToExpiry,
                        BreakEvenPrice = strike + optionResult.CallPrice, // For call buyer
                        MaxProfit = double.PositiveInfinity, // Unlimited for long call
                        MaxLoss = optionResult.CallPrice // Premium paid
                    };

                    if (daysToExpiry == 30) // Only add to main chain for 30-day options
                    {
                        result.OptionChain.Add(chainItem);
                    }
                }
            }

            // Calculate volatility smile
            result.VolatilitySmile = BlackScholesModel.CalculateVolatilitySmile(
                (double)currentPrice, 
                30.0 / 365.0, 
                DEFAULT_RISK_FREE_RATE, 
                volatility
            );

            // Generate volatility surface
            result.VolSurface = GenerateVolatilitySurface((double)currentPrice, volatility, expirationDates, strikeMultipliers);

            // Determine market outlook based on put/call skew
            result.MarketOutlook = DetermineMarketOutlook(result.OptionChain);

            // Generate chart data
            result.ChartData = GenerateOptionsChartData(result);

            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            return result;
        }

        private double CalculateHistoricalVolatility(List<StockDataPoint> historicalData)
        {
            if (historicalData == null || historicalData.Count < 2)
                return 0.25; // Default 25% volatility

            var returns = new List<double>();
            for (int i = 1; i < historicalData.Count; i++)
            {
                var dailyReturn = Math.Log((double)historicalData[i].Close / (double)historicalData[i - 1].Close);
                returns.Add(dailyReturn);
            }

            var avgReturn = returns.Average();
            var variance = returns.Select(r => Math.Pow(r - avgReturn, 2)).Average();
            var dailyVol = Math.Sqrt(variance);
            
            // Annualize volatility (252 trading days)
            return dailyVol * Math.Sqrt(252);
        }

        private BlackScholesModel.OptionGreeks CalculatePutGreeks(BlackScholesModel.OptionParameters parameters, BlackScholesModel.OptionGreeks callGreeks)
        {
            // Put-Call parity relationships for Greeks
            return new BlackScholesModel.OptionGreeks
            {
                Delta = callGreeks.Delta - Math.Exp(-parameters.DividendYield * parameters.TimeToExpiry),
                Gamma = callGreeks.Gamma, // Same for puts and calls
                Theta = callGreeks.Theta + parameters.RiskFreeRate * parameters.StrikePrice * Math.Exp(-parameters.RiskFreeRate * parameters.TimeToExpiry),
                Vega = callGreeks.Vega, // Same for puts and calls
                Rho = -parameters.StrikePrice * parameters.TimeToExpiry * Math.Exp(-parameters.RiskFreeRate * parameters.TimeToExpiry) / 100
            };
        }

        private VolatilitySurface GenerateVolatilitySurface(double stockPrice, double baseVol, int[] expirationDays, double[] strikeMultipliers)
        {
            var surface = new VolatilitySurface
            {
                Strikes = strikeMultipliers.Select(m => Math.Round(stockPrice * m, 2)).ToList(),
                Expirations = expirationDays.Select(d => d / 365.0).ToList(),
                ImpliedVols = new double[strikeMultipliers.Length, expirationDays.Length]
            };

            for (int i = 0; i < strikeMultipliers.Length; i++)
            {
                for (int j = 0; j < expirationDays.Length; j++)
                {
                    // Generate realistic volatility surface with term structure and smile
                    var moneyness = strikeMultipliers[i];
                    var timeToExpiry = expirationDays[j] / 365.0;
                    
                    // Volatility smile effect
                    var smile = Math.Pow(moneyness - 1, 2) * 0.4;
                    
                    // Term structure effect (volatility typically decreases with time)
                    var termStructure = 1 - (0.1 * Math.Sqrt(timeToExpiry));
                    
                    surface.ImpliedVols[i, j] = baseVol * termStructure * (1 + smile);
                }
            }

            return surface;
        }

        private string DetermineMarketOutlook(List<OptionChainItem> optionChain)
        {
            if (!optionChain.Any()) return "Neutral";

            // Analyze put/call implied volatility skew
            var atmOptions = optionChain.Where(o => o.Moneyness == "At-the-Money").ToList();
            if (!atmOptions.Any()) return "Neutral";

            var avgPutCallSkew = atmOptions.Average(o => o.PutImpliedVol - o.CallImpliedVol);
            
            // Higher put IV suggests bearish sentiment
            if (avgPutCallSkew > 0.03) return "Bearish - Put protection in demand";
            if (avgPutCallSkew < -0.02) return "Bullish - Call speculation active";
            
            return "Neutral - Balanced options activity";
        }

        private OptionsChartData GenerateOptionsChartData(OptionsAnalysisResult analysisResult)
        {
            var chartData = new OptionsChartData();
            
            // Get ATM option for payoff calculation
            var atmOption = analysisResult.OptionChain.FirstOrDefault(o => o.Moneyness == "At-the-Money") 
                ?? analysisResult.OptionChain[analysisResult.OptionChain.Count / 2];

            // 1. Generate Payoff Chart Data
            chartData.PayoffData = new OptionsChartData.PayoffChartData
            {
                CurrentPrice = analysisResult.CurrentPrice,
                StrikePrice = atmOption.StrikePrice,
                CallPremium = atmOption.CallPrice,
                PutPremium = atmOption.PutPrice
            };

            // Calculate payoff for price range from 70% to 130% of current price
            var minPrice = analysisResult.CurrentPrice * 0.7;
            var maxPrice = analysisResult.CurrentPrice * 1.3;
            var priceStep = analysisResult.CurrentPrice * 0.01;

            for (var price = minPrice; price <= maxPrice; price += priceStep)
            {
                chartData.PayoffData.PriceRange.Add(Math.Round(price, 2));
                
                // Long call payoff
                var callProfit = Math.Max(0, price - atmOption.StrikePrice) - atmOption.CallPrice;
                chartData.PayoffData.CallPayoff.Add(new OptionsChartData.PayoffDataPoint { X = price, Y = callProfit });
                
                // Long put payoff
                var putProfit = Math.Max(0, atmOption.StrikePrice - price) - atmOption.PutPrice;
                chartData.PayoffData.PutPayoff.Add(new OptionsChartData.PayoffDataPoint { X = price, Y = putProfit });
                
                // Stock returns for comparison
                var stockReturn = price - analysisResult.CurrentPrice;
                chartData.PayoffData.StockReturns.Add(new OptionsChartData.PayoffDataPoint { X = price, Y = stockReturn });
            }

            // 2. Generate Greeks Chart Data
            chartData.GreeksData = new OptionsChartData.GreeksChartData
            {
                CallGreeks = new List<double>
                {
                    atmOption.CallGreeks.Delta,
                    atmOption.CallGreeks.Gamma,
                    -atmOption.CallGreeks.Theta / 365, // Normalize theta to daily
                    atmOption.CallGreeks.Vega,
                    atmOption.CallGreeks.Rho
                },
                PutGreeks = new List<double>
                {
                    atmOption.PutGreeks.Delta,
                    atmOption.PutGreeks.Gamma,
                    -atmOption.PutGreeks.Theta / 365, // Normalize theta to daily
                    atmOption.PutGreeks.Vega,
                    atmOption.PutGreeks.Rho
                }
            };

            // 3. Generate Volatility Smile Data
            chartData.SmileData = new OptionsChartData.VolatilitySmileData
            {
                CurrentPrice = analysisResult.CurrentPrice,
                DataPoints = analysisResult.VolatilitySmile.Select(point => 
                    new OptionsChartData.SmileDataPoint 
                    { 
                        Strike = point.Strike, 
                        ImpliedVolatility = point.ImpliedVol * 100 // Convert to percentage
                    }).ToList()
            };

            // Pre-serialize the data for JavaScript
            var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            
            chartData.PayoffDataJson = Newtonsoft.Json.JsonConvert.SerializeObject(chartData.PayoffData, jsonSettings);
            chartData.GreeksDataJson = Newtonsoft.Json.JsonConvert.SerializeObject(chartData.GreeksData, jsonSettings);
            chartData.SmileDataJson = Newtonsoft.Json.JsonConvert.SerializeObject(chartData.SmileData, jsonSettings);

            return chartData;
        }

        public async Task<List<OptionsAnalysisResult>> AnalyzePortfolioOptions(string userId)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var userStocks = await _context.UserStocks
                .Where(us => us.UserId == userId)
                .ToListAsync();

            if (!userStocks.Any())
            {
                return new List<OptionsAnalysisResult>();
            }

            // Process options analysis in parallel for better performance
            var tasks = userStocks.Select(async stock =>
            {
                try
                {
                    return await AnalyzeStockOptions(stock.StockSymbol, userId);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the entire operation
                    _logger.LogError(ex, "Error analyzing options for {Symbol}", stock.StockSymbol);
                    return null;
                }
            });

            var analysisResults = await Task.WhenAll(tasks);
            var results = analysisResults.Where(r => r != null).ToList();

            sw.Stop();
            _logger.LogInformation($"AnalyzePortfolioOptions completed in {sw.ElapsedMilliseconds}ms for {userStocks.Count} stocks");

            return results;
        }

        public async Task<PortfolioOptimizationModel.OptimizationResult> OptimizePortfolio(string userId)
        {
            var cacheKey = $"portfolio_optimization_{userId}_{DateTime.UtcNow:yyyyMMdd}";
            
            if (_cache.TryGetValue(cacheKey, out PortfolioOptimizationModel.OptimizationResult cachedResult))
            {
                return cachedResult;
            }

            // Get user's stocks
            var userStocks = await _context.UserStocks
                .Where(us => us.UserId == userId)
                .ToListAsync();

            if (!userStocks.Any())
                return null;

            // Prepare asset data
            var assets = new List<PortfolioOptimizationModel.AssetData>();
            var currentWeights = new Dictionary<string, double>();
            double totalValue = (double)userStocks.Sum(s => s.TotalValue);

            foreach (var stock in userStocks)
            {
                // Get historical data for returns calculation
                var historicalData = await _stockService.GetRealTimeDataAsync(
                    new List<string> { stock.StockSymbol }, 
                    730 // 2 years of data
                );

                if (historicalData.TryGetValue(stock.StockSymbol, out var dataPoints) && dataPoints != null && dataPoints.Count() > 20)
                {
                    var returns = CalculateDailyReturns(dataPoints);
                    var annualizedReturn = CalculateAnnualizedReturn(returns);
                    var annualizedVolatility = CalculateAnnualizedVolatility(returns);

                    assets.Add(new PortfolioOptimizationModel.AssetData
                    {
                        Symbol = stock.StockSymbol,
                        Returns = returns,
                        ExpectedReturn = annualizedReturn,
                        Volatility = annualizedVolatility
                    });

                    currentWeights[stock.StockSymbol] = (double)stock.TotalValue / totalValue;
                }
            }

            if (!assets.Any())
                return null;

            // Run optimization
            var result = PortfolioOptimizationModel.OptimizePortfolio(assets, currentWeights);

            _cache.Set(cacheKey, result, TimeSpan.FromHours(6));
            return result;
        }

        private List<double> CalculateDailyReturns(IEnumerable<StockDataPoint> data)
        {
            var dataList = data.ToList();
            var returns = new List<double>();
            for (int i = 1; i < dataList.Count; i++)
            {
                var dailyReturn = (double)((dataList[i].Close - dataList[i - 1].Close) / dataList[i - 1].Close);
                returns.Add(dailyReturn);
            }
            return returns;
        }

        private double CalculateAnnualizedReturn(List<double> dailyReturns)
        {
            if (!dailyReturns.Any()) return 0;
            var avgDailyReturn = dailyReturns.Average();
            return Math.Pow(1 + avgDailyReturn, 252) - 1; // 252 trading days
        }

        private double CalculateAnnualizedVolatility(List<double> dailyReturns)
        {
            if (!dailyReturns.Any()) return 0.25; // Default 25%
            var avgReturn = dailyReturns.Average();
            var variance = dailyReturns.Select(r => Math.Pow(r - avgReturn, 2)).Average();
            return Math.Sqrt(variance) * Math.Sqrt(252);
        }

        public async Task<FourierAnalysisModel.FourierAnalysisResult> AnalyzeMarketCycles(string userId)
        {
            var cacheKey = $"fourier_analysis_{userId}_{DateTime.UtcNow:yyyyMMdd}";
            
            if (_cache.TryGetValue(cacheKey, out FourierAnalysisModel.FourierAnalysisResult cachedResult))
            {
                return cachedResult;
            }

            // Get user's portfolio value history
            var userStocks = await _context.UserStocks
                .Where(us => us.UserId == userId)
                .ToListAsync();

            if (!userStocks.Any())
                return null;

            // Calculate portfolio value time series
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddYears(-2);
            var portfolioValues = new List<double>();
            var dates = new List<DateTime>();

            // Get historical data for all stocks
            var allHistoricalData = new Dictionary<string, List<StockDataPoint>>();
            var symbols = userStocks.Select(s => s.StockSymbol).ToList();
            var historicalDataResult = await _stockService.GetRealTimeDataAsync(symbols, 730); // 2 years
            
            foreach (var kvp in historicalDataResult)
            {
                if (kvp.Value != null && kvp.Value.Any())
                {
                    allHistoricalData[kvp.Key] = kvp.Value.ToList();
                }
            }

            // Calculate daily portfolio values
            var allDates = allHistoricalData.Values
                .SelectMany(d => d.Select(p => p.Date))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var date in allDates)
            {
                double portfolioValue = 0;
                bool hasData = true;

                foreach (var stock in userStocks)
                {
                    if (allHistoricalData.ContainsKey(stock.StockSymbol))
                    {
                        var dataPoint = allHistoricalData[stock.StockSymbol]
                            .FirstOrDefault(d => d.Date.Date == date.Date);
                        
                        if (dataPoint != null)
                        {
                            var shares = stock.NumberOfSharesPurchased - (stock.NumberOfSharesSold ?? 0);
                            portfolioValue += (double)dataPoint.Close * shares;
                        }
                        else
                        {
                            hasData = false;
                            break;
                        }
                    }
                }

                if (hasData && portfolioValue > 0)
                {
                    portfolioValues.Add(portfolioValue);
                    dates.Add(date);
                }
            }

            if (portfolioValues.Count < 50) // Need sufficient data for FFT
                return null;

            // Perform Fourier analysis
            var result = FourierAnalysisModel.AnalyzePriceCycles(portfolioValues, dates);

            // Add correlation analysis if multiple stocks
            if (userStocks.Count > 1)
            {
                var stockReturns = new Dictionary<string, List<double>>();
                
                foreach (var kvp in allHistoricalData)
                {
                    var returns = CalculateDailyReturns(kvp.Value);
                    if (returns.Count > 20)
                    {
                        stockReturns[kvp.Key] = returns;
                    }
                }

                if (stockReturns.Count > 1)
                {
                    result.CrossCorrelations = FourierAnalysisModel.AnalyzePortfolioCorrelations(stockReturns);
                }
            }

            _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
            return result;
        }
    }
}