using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class PortfolioDashboardViewModel
    {
        public PortfolioDashboardViewModel()
        {
            // Initialize with default values to prevent null reference
            UserStocks = new List<AppUserStock>();
            PortfolioMetrics = new PortfolioMetrics();
            PerformanceData = new Dictionary<string, List<PerformancePoint>>();
            HistoricalData = new Dictionary<string, List<StockDataPoint>>();
            SectorDistribution = new Dictionary<string, decimal>();
            CompanyDetails = new Dictionary<string, CompanyDetails>();
            MonthlyPerformanceData = new List<MonthlyPerformanceData>();
            RollingReturnsData = new List<RollingReturnsData>();
            PortfolioGrowthData = new List<PortfolioGrowthPoint>();
            RiskMetrics = new List<RiskAnalysisData>();
            DrawdownData = new List<DrawdownPoint>();
            DiversificationData = new List<DiversificationData>();
        }

        public List<AppUserStock> UserStocks { get; set; }
        public PortfolioMetrics PortfolioMetrics { get; set; }
        public Dictionary<string, List<PerformancePoint>> PerformanceData { get; set; }
        public Dictionary<string, List<StockDataPoint>> HistoricalData { get; set; }
        public Dictionary<string, decimal> SectorDistribution { get; set; }
        public Dictionary<string, CompanyDetails> CompanyDetails { get; set; }
        
        // Real chart data properties
        public List<MonthlyPerformanceData> MonthlyPerformanceData { get; set; }
        public List<RollingReturnsData> RollingReturnsData { get; set; }
        public List<PortfolioGrowthPoint> PortfolioGrowthData { get; set; }
        public List<RiskAnalysisData> RiskMetrics { get; set; }
        public List<DrawdownPoint> DrawdownData { get; set; }
        public List<DiversificationData> DiversificationData { get; set; }
    }
}