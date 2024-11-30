using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class PortfolioDashboardViewModel
    {
        public PortfolioMetrics PortfolioMetrics { get; set; }
        public List<AppUserStock> UserStocks { get; set; }
        public Dictionary<string, decimal> SectorDistribution { get; set; }
        public Dictionary<string, List<PerformancePoint>> PerformanceData { get; set; }
        public Dictionary<string, CompanyDetails> CompanyDetails { get; set; }
    }
}