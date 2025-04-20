using System;

namespace FundWatch.Models
{
    public class StockDataPoint
    {
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    public class StockSymbolData
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public bool ExactMatch { get; set; }
    }

    public class CompanyDetails
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Industry { get; set; }
        public decimal MarketCap { get; set; }
        public string Website { get; set; }
        public int Employees { get; set; }
    }
}