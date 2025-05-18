using System;
using System.Collections.Generic;

namespace FundWatch.Models
{
    // Model for Monthly Performance data points
    public class MonthlyPerformanceData
    {
        public string Month { get; set; } = string.Empty;
        public decimal PortfolioPerformance { get; set; }
        public decimal BenchmarkPerformance { get; set; }
    }

    // Model for Rolling Returns data at different time periods
    public class RollingReturnsData
    {
        public string TimePeriod { get; set; } = string.Empty;
        public decimal PortfolioReturn { get; set; }
        public decimal BenchmarkReturn { get; set; }
    }

    // Model for Portfolio Growth data points
    public class PortfolioGrowthPoint
    {
        public DateTime Date { get; set; }
        public decimal PortfolioValue { get; set; }
        public decimal BenchmarkValue { get; set; }
    }

    // Model for Risk Analysis data for individual stocks or portfolio
    public class RiskAnalysisData
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Volatility { get; set; }
        public decimal Beta { get; set; } 
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }
    }

    // Model for Drawdown Analysis data points
    public class DrawdownPoint
    {
        public DateTime Date { get; set; }
        public decimal PortfolioDrawdown { get; set; }
        public decimal BenchmarkDrawdown { get; set; }
    }

    // Model for Technical Indicator data
    public class TechnicalIndicatorData
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public string IndicatorType { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
    }

    // Enhanced model for sector distribution data
    public class SectorDistributionData
    {
        public string Sector { get; set; } = string.Empty;
        public decimal Percentage { get; set; }
        public decimal Value { get; set; }
        public int NumberOfHoldings { get; set; }
        public decimal SectorPerformance { get; set; }
    }
    
    // Model for Diversification Chart data (Pie chart)
    public class DiversificationData
    {
        public string Name { get; set; } = string.Empty;
        public decimal Y { get; set; } // Using Y property for Highcharts compatibility
    }
}