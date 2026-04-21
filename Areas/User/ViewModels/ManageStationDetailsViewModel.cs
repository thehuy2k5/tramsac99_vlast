using tramsac99.Areas.Admin.Models;

namespace tramsac99.Areas.User.ViewModels
{
    public class ManageStationDetailsViewModel
    {
        public int StationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = ChargingStatus.Active;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string PhoneNumber { get; set; } = "-";
        public int TotalPoleCount { get; set; }
        public DateTime? LatestOperationAt { get; set; }
        public List<PoleItemViewModel> Poles { get; set; } = new();
        public List<StationOperationRequest> OperationRequests { get; set; } = new();

        // Why changed: add server-side paging for request history so the right panel does not become too long.
        public List<StationOperationRequest> HistoryItems { get; set; } = new();
        public int HistoryPage { get; set; } = 1;
        public int HistoryPageSize { get; set; } = 4;
        public int HistoryTotalCount { get; set; }
        public int HistoryTotalPages => Math.Max(1, (int)Math.Ceiling(HistoryTotalCount / (double)Math.Max(1, HistoryPageSize)));

        // Why changed: add server-side paging for poles so the pole list remains compact.
        public List<PoleItemViewModel> PoleItems { get; set; } = new();
        public int PolePage { get; set; } = 1;
        public int PolePageSize { get; set; } = 4;
        public int PoleTotalCount { get; set; }
        public int PoleTotalPages => Math.Max(1, (int)Math.Ceiling(PoleTotalCount / (double)Math.Max(1, PolePageSize)));
    }
}

