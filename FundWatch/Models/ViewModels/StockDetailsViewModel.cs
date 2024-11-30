using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class StockDetailsViewModel
    {
        public AppUserStock Stock { get; set; }
        public CompanyDetails CompanyDetails { get; set; }
        public List<StockDataPoint> HistoricalData { get; set; }
    }
}