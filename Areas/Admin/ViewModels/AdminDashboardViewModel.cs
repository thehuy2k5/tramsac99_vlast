using tramsac99.Areas.Admin.Models.Dto;

namespace tramsac99.Areas.Admin.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalStations { get; set; }
        public int AdminManagedStations { get; set; }
        public int UserSubmittedStations { get; set; }
        public int TotalReviews { get; set; }
        public decimal RevenueToday { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public decimal TotalRevenue { get; set; }
        public int PaidRequestCount { get; set; }
        public int PendingPaymentCount { get; set; }
        public List<string> RevenueLabels { get; set; } = new();
        public List<decimal> RevenueValues { get; set; } = new();
        public List<DashboardRevenueStationItem> TopRevenueStations { get; set; } = new();
        public List<ChargingStationDto> Stations { get; set; } = new();
    }

    public class DashboardRevenueStationItem
    {
        public string StationName { get; set; } = string.Empty;
        public decimal TotalRevenue { get; set; }
        public int RequestCount { get; set; }
    }
}
