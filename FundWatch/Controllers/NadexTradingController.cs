using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FundWatch.Services;
using FundWatch.Models;
using FundWatch.Models.ViewModels;

namespace FundWatch.Controllers
{
    [Authorize]
    public class NadexTradingController : Controller
    {
        private readonly ILogger<NadexTradingController> _logger;
        private readonly StockService _stockService;

        public NadexTradingController(
            ILogger<NadexTradingController> logger,
            StockService stockService)
        {
            _logger = logger;
            _stockService = stockService;
        }

        public IActionResult Index()
        {
            var model = new NadexTradingViewModel
            {
                // Initialize with popular trading symbols
                PopularSymbols = new[] { "SPY", "QQQ", "GLD", "EUR/USD", "USD/JPY", "BTC-USD" },
                TimeFrames = new[] 
                { 
                    new TimeFrameOption { Value = "5min", Display = "5 Minutes" },
                    new TimeFrameOption { Value = "20min", Display = "20 Minutes" },
                    new TimeFrameOption { Value = "1hour", Display = "1 Hour" },
                    new TimeFrameOption { Value = "1day", Display = "Daily" }
                },
                SelectedTimeFrame = "5min"
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> GetTradingSignals([FromBody] TradingSignalRequest request)
        {
            try
            {
                // Get stock data from Polygon.io
                var daysBack = request.TimeFrame switch
                {
                    "5min" => 1,
                    "20min" => 1,
                    "1hour" => 3,
                    "1day" => 30,
                    _ => 1
                };

                var stockDataDict = await _stockService.GetRealTimeDataAsync(
                    new List<string> { request.Symbol }, 
                    daysBack
                );

                if (!stockDataDict.TryGetValue(request.Symbol, out var stockData) || stockData == null || !stockData.Any())
                {
                    return Json(new { success = false, message = "No data available for this symbol" });
                }

                // Calculate technical indicators
                var signals = CalculateTradingSignals(stockData, request.TimeFrame);
                
                return Json(new { success = true, signals });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading signals for {Symbol}", request.Symbol);
                return Json(new { success = false, message = "Error analyzing market data" });
            }
        }

        private TradingSignals CalculateTradingSignals(List<StockDataPoint> data, string timeFrame)
        {
            var signals = new TradingSignals
            {
                Timestamp = DateTime.UtcNow,
                CurrentPrice = (double)data.Last().Close
            };

            // Calculate Simple Moving Averages
            var prices = data.Select(d => (double)d.Close).ToList();
            signals.SMA20 = CalculateSMA(prices, 20);
            signals.SMA50 = CalculateSMA(prices, Math.Min(50, prices.Count));

            // Calculate RSI
            signals.RSI = CalculateRSI(prices, 14);

            // Calculate MACD
            var macdResult = CalculateMACD(prices);
            signals.MACD = macdResult.MACD;
            signals.MACDSignal = macdResult.Signal;
            signals.MACDHistogram = macdResult.Histogram;

            // Calculate Bollinger Bands
            var bb = CalculateBollingerBands(prices, 20);
            signals.BollingerUpper = bb.Upper;
            signals.BollingerMiddle = bb.Middle;
            signals.BollingerLower = bb.Lower;

            // Calculate Support and Resistance
            signals.Support = CalculateSupport(data);
            signals.Resistance = CalculateResistance(data);

            // Generate trading signal
            signals.Signal = GenerateTradingSignal(signals);
            signals.Confidence = CalculateConfidence(signals);
            
            // Calculate risk/reward for binary options
            var binaryOptions = CalculateBinaryOptionsStrategy(signals, timeFrame);
            signals.CallStrike = binaryOptions.CallStrike;
            signals.PutStrike = binaryOptions.PutStrike;
            signals.ExpectedPayout = binaryOptions.ExpectedPayout;

            return signals;
        }

        private double CalculateSMA(List<double> prices, int period)
        {
            if (prices.Count < period) return prices.Average();
            return prices.Skip(prices.Count - period).Average();
        }

        private double CalculateRSI(List<double> prices, int period)
        {
            if (prices.Count < period + 1) return 50;

            var gains = new List<double>();
            var losses = new List<double>();

            for (int i = 1; i < prices.Count; i++)
            {
                var change = prices[i] - prices[i - 1];
                if (change > 0)
                {
                    gains.Add(change);
                    losses.Add(0);
                }
                else
                {
                    gains.Add(0);
                    losses.Add(Math.Abs(change));
                }
            }

            var avgGain = gains.Skip(gains.Count - period).Average();
            var avgLoss = losses.Skip(losses.Count - period).Average();

            if (avgLoss == 0) return 100;

            var rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        private (double MACD, double Signal, double Histogram) CalculateMACD(List<double> prices)
        {
            if (prices.Count < 26) return (0, 0, 0);

            var ema12 = CalculateEMA(prices, 12);
            var ema26 = CalculateEMA(prices, 26);
            var macd = ema12 - ema26;

            // For simplicity, using SMA for signal line
            var signal = macd;
            var histogram = macd - signal;

            return (macd, signal, histogram);
        }

        private double CalculateEMA(List<double> prices, int period)
        {
            if (prices.Count < period) return prices.Average();

            var multiplier = 2.0 / (period + 1);
            var ema = prices.Take(period).Average();

            for (int i = period; i < prices.Count; i++)
            {
                ema = (prices[i] - ema) * multiplier + ema;
            }

            return ema;
        }

        private (double Upper, double Middle, double Lower) CalculateBollingerBands(List<double> prices, int period)
        {
            var sma = CalculateSMA(prices, period);
            var relevantPrices = prices.Skip(Math.Max(0, prices.Count - period)).ToList();
            var stdDev = CalculateStandardDeviation(relevantPrices, sma);

            return (sma + 2 * stdDev, sma, sma - 2 * stdDev);
        }

        private double CalculateStandardDeviation(List<double> values, double mean)
        {
            var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
            return Math.Sqrt(variance);
        }

        private double CalculateSupport(List<StockDataPoint> data)
        {
            var recentData = data.Skip(Math.Max(0, data.Count - 20)).ToList();
            return (double)recentData.Min(d => d.Low);
        }

        private double CalculateResistance(List<StockDataPoint> data)
        {
            var recentData = data.Skip(Math.Max(0, data.Count - 20)).ToList();
            return (double)recentData.Max(d => d.High);
        }

        private string GenerateTradingSignal(TradingSignals signals)
        {
            var bullishSignals = 0;
            var bearishSignals = 0;

            // RSI signals
            if (signals.RSI < 30) bullishSignals += 2; // Oversold
            else if (signals.RSI > 70) bearishSignals += 2; // Overbought

            // Moving average signals
            if (signals.CurrentPrice > signals.SMA20 && signals.SMA20 > signals.SMA50) bullishSignals += 2;
            else if (signals.CurrentPrice < signals.SMA20 && signals.SMA20 < signals.SMA50) bearishSignals += 2;

            // Bollinger Bands signals
            if (signals.CurrentPrice < signals.BollingerLower) bullishSignals += 1;
            else if (signals.CurrentPrice > signals.BollingerUpper) bearishSignals += 1;

            // MACD signals
            if (signals.MACD > signals.MACDSignal) bullishSignals += 1;
            else bearishSignals += 1;

            // Support/Resistance signals
            var supportDistance = (signals.CurrentPrice - signals.Support) / signals.CurrentPrice;
            var resistanceDistance = (signals.Resistance - signals.CurrentPrice) / signals.CurrentPrice;

            if (supportDistance < 0.02) bullishSignals += 1; // Near support
            if (resistanceDistance < 0.02) bearishSignals += 1; // Near resistance

            // Determine overall signal
            if (bullishSignals > bearishSignals + 2) return "STRONG BUY";
            if (bullishSignals > bearishSignals) return "BUY";
            if (bearishSignals > bullishSignals + 2) return "STRONG SELL";
            if (bearishSignals > bullishSignals) return "SELL";
            return "NEUTRAL";
        }

        private double CalculateConfidence(TradingSignals signals)
        {
            var confidence = 50.0;

            // RSI confidence
            if (signals.RSI < 20 || signals.RSI > 80) confidence += 15;
            else if (signals.RSI < 30 || signals.RSI > 70) confidence += 10;

            // Trend alignment
            if ((signals.CurrentPrice > signals.SMA20 && signals.SMA20 > signals.SMA50) ||
                (signals.CurrentPrice < signals.SMA20 && signals.SMA20 < signals.SMA50))
                confidence += 15;

            // Bollinger Band position
            if (signals.CurrentPrice < signals.BollingerLower || signals.CurrentPrice > signals.BollingerUpper)
                confidence += 10;

            // MACD alignment
            if (Math.Abs(signals.MACD - signals.MACDSignal) > 0.5) confidence += 10;

            return Math.Min(95, Math.Max(5, confidence));
        }

        private (double CallStrike, double PutStrike, double ExpectedPayout) CalculateBinaryOptionsStrategy(
            TradingSignals signals, string timeFrame)
        {
            var priceMovement = timeFrame switch
            {
                "5min" => 0.001,
                "20min" => 0.002,
                "1hour" => 0.005,
                "1day" => 0.01,
                _ => 0.001
            };

            var callStrike = signals.CurrentPrice * (1 + priceMovement);
            var putStrike = signals.CurrentPrice * (1 - priceMovement);

            // Calculate expected payout based on confidence
            var winProbability = signals.Confidence / 100.0;
            var typicalPayout = 0.80; // 80% payout on win
            var expectedPayout = (winProbability * typicalPayout) - ((1 - winProbability) * 1);

            return (callStrike, putStrike, expectedPayout);
        }
    }

    public class TradingSignalRequest
    {
        public string Symbol { get; set; }
        public string TimeFrame { get; set; }
    }

    public class TradingSignals
    {
        public DateTime Timestamp { get; set; }
        public double CurrentPrice { get; set; }
        public double SMA20 { get; set; }
        public double SMA50 { get; set; }
        public double RSI { get; set; }
        public double MACD { get; set; }
        public double MACDSignal { get; set; }
        public double MACDHistogram { get; set; }
        public double BollingerUpper { get; set; }
        public double BollingerMiddle { get; set; }
        public double BollingerLower { get; set; }
        public double Support { get; set; }
        public double Resistance { get; set; }
        public string Signal { get; set; }
        public double Confidence { get; set; }
        public double CallStrike { get; set; }
        public double PutStrike { get; set; }
        public double ExpectedPayout { get; set; }
    }
}