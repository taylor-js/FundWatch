using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class PerformancePoint
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public decimal PercentageChange { get; set; }
    }
}