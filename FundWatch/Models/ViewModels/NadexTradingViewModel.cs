namespace FundWatch.Models.ViewModels
{
    public class NadexTradingViewModel
    {
        public string[] PopularSymbols { get; set; }
        public TimeFrameOption[] TimeFrames { get; set; }
        public string SelectedTimeFrame { get; set; }
    }

    public class TimeFrameOption
    {
        public string Value { get; set; }
        public string Display { get; set; }
    }
}