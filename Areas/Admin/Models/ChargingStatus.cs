namespace tramsac99.Areas.Admin.Models
{
    public static class ChargingStatus
    {
        
        public const string Active = "Hoạt động";
        public const string Inactive = "Không hoạt động";
        public const string Maintenance = "Bảo trì";
        public const string Error = "Lỗi";
        

        public const string PortAvailable = "Còn chỗ sạc";
        public const string PortBusy = "Đang sử dụng";
        public const string PortInactive = "Không hoạt động";

        // Why changed: station and pole now use only 2 statuses
        public static string NormalizeNodeStatus(string? rawStatus)
        {
            return rawStatus == Inactive ? Inactive : Active;
        }

        // Why changed: port now uses only 3 statuses
        public static string NormalizePortStatus(string? rawStatus)
        {
            return rawStatus switch
            {
                PortBusy => PortBusy,
                PortInactive => PortInactive,
                _ => PortAvailable
            };
        }

        // Why changed: use in memory logic for parent-child sync
        public static bool IsNodeOperational(string? status)
        {
            return status == Active;
        }

        // Why changed: a port is usable when available or busy
        public static bool IsPortOperational(string? status)
        {
            return status == PortAvailable || status == PortBusy;
        }

        // Why changed: admin types only number, backend appends kW
        public static string NormalizeKw(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return string.Empty;

            var normalized = rawValue.Trim()
                .Replace("kw", "", StringComparison.OrdinalIgnoreCase)
                .Replace("kW", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            return $"{normalized} kW";
        }
    }
}