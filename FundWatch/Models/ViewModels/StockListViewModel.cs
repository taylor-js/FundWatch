using System;
using System.Collections.Generic;

namespace FundWatch.Models.ViewModels
{
    public class StockListViewModel
    {
        public List<AppUserStock> Stocks { get; set; }
        public Dictionary<string, CompanyDetails> CompanyDetails { get; set; }
    }
}