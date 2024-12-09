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
        }

        public List<AppUserStock> UserStocks { get; set; }
        public PortfolioMetrics PortfolioMetrics { get; set; }
        public Dictionary<string, List<PerformancePoint>> PerformanceData { get; set; }
        public Dictionary<string, List<StockDataPoint>> HistoricalData { get; set; } // Add this property
        public Dictionary<string, decimal> SectorDistribution { get; set; }
        public Dictionary<string, CompanyDetails> CompanyDetails { get; set; }
    }
}