using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class OptionsChartData
    {
        // Options Payoff Chart Data
        public class PayoffChartData
        {
            public List<double> PriceRange { get; set; } = new List<double>();
            public List<PayoffDataPoint> CallPayoff { get; set; } = new List<PayoffDataPoint>();
            public List<PayoffDataPoint> PutPayoff { get; set; } = new List<PayoffDataPoint>();
            public List<PayoffDataPoint> StockReturns { get; set; } = new List<PayoffDataPoint>();
            public double CurrentPrice { get; set; }
            public double StrikePrice { get; set; }
            public double CallPremium { get; set; }
            public double PutPremium { get; set; }
        }

        public class PayoffDataPoint
        {
            public double X { get; set; } // Price
            public double Y { get; set; } // Profit/Loss
        }

        // Greeks Radar Chart Data
        public class GreeksChartData
        {
            public List<string> Categories { get; set; } = new List<string> { "Delta", "Gamma", "Theta", "Vega", "Rho" };
            public List<double> CallGreeks { get; set; } = new List<double>();
            public List<double> PutGreeks { get; set; } = new List<double>();
        }

        // Volatility Smile Chart Data
        public class VolatilitySmileData
        {
            public List<SmileDataPoint> DataPoints { get; set; } = new List<SmileDataPoint>();
            public double CurrentPrice { get; set; }
        }

        public class SmileDataPoint
        {
            public double Strike { get; set; }
            public double ImpliedVolatility { get; set; }
        }

        // Main properties
        public PayoffChartData PayoffData { get; set; }
        public GreeksChartData GreeksData { get; set; }
        public VolatilitySmileData SmileData { get; set; }
        
        // Pre-serialized JSON for direct use in JavaScript
        public string PayoffDataJson { get; set; }
        public string GreeksDataJson { get; set; }
        public string SmileDataJson { get; set; }
    }
}