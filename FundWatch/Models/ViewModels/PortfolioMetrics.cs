using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class PortfolioMetrics
    {
        public decimal TotalValue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalGain { get; set; }
        public decimal TotalPerformance { get; set; }
        public int TotalStocks { get; set; }
        public int UniqueSectors { get; set; }
        public string BestPerformingStock { get; set; } = "N/A";
        public string WorstPerformingStock { get; set; } = "N/A";
        public decimal BestPerformingStockReturn { get; set; }
        public decimal WorstPerformingStockReturn { get; set; }
    }
}