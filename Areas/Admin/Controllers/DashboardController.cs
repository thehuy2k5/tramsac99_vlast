using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using tramsac99.Areas.Admin.Models;
using tramsac99.Areas.Admin.Models.Dto;
using tramsac99.Areas.Admin.ViewModels;
using tramsac99.Data;

namespace tramsac99.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")] // Why changed: only admin can access admin dashboard
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);

            var stations = await _context.ChargingStations
                .Select(s => new ChargingStationDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Address = s.Address,
                    Latitude = s.Latitude,
                    Longitude = s.Longitude,
                    Status = s.Status,
                    ChargerType = s.ChargerType,
                    Power = s.Power,
                    PricePerKwh = s.PricePerKwh,
                    AverageRating = s.Reviews.Any() ? Math.Round(s.Reviews.Average(r => (double)r.Rating), 1) : 0,
                    ReviewCount = s.Reviews.Count(),
                    PoleCount = s.ChargingPoles.Count(),
                    ActivePoleCount = s.ChargingPoles.Count(p => p.Status == ChargingStatus.Active),
                    ActivePortCount = 0,
                    PortCount = 0,
                    OwnerUserId = s.OwnerUserId,
                    IsAdminManaged = !s.OwnerUserId.HasValue
                })
                .ToListAsync();

            var paidRequests = await _context.StationRegistrationRequests
                .Where(x => x.PaymentStatus == StationWorkflowStatus.Paid || x.PaidAt != null)
                .Select(x => new
                {
                    StationName = x.StationName,
                    x.FeeAmount,
                    PaidAt = x.PaidAt ?? x.CompletedAt ?? x.CreatedAt
                })
                .ToListAsync();

            var paidToday = paidRequests
                .Where(x => x.PaidAt.Date == now.Date)
                .Sum(x => x.FeeAmount);

            var paidThisMonth = paidRequests
                .Where(x => x.PaidAt.Year == monthStart.Year && x.PaidAt.Month == monthStart.Month)
                .Sum(x => x.FeeAmount);

            var revenueMonths = Enumerable.Range(0, 6)
                .Select(offset => monthStart.AddMonths(-(5 - offset)))
                .ToList();

            var revenueLabels = revenueMonths
                .Select(x => $"{x:MM/yyyy}")
                .ToList();

            var revenueValues = revenueMonths
                .Select(month => paidRequests
                    .Where(x => x.PaidAt.Year == month.Year && x.PaidAt.Month == month.Month)
                    .Sum(x => x.FeeAmount))
                .ToList();

            var topRevenueStations = paidRequests
                .GroupBy(x => string.IsNullOrWhiteSpace(x.StationName) ? "Chưa có tên trạm" : x.StationName.Trim())
                .Select(g => new DashboardRevenueStationItem
                {
                    StationName = g.Key,
                    TotalRevenue = g.Sum(x => x.FeeAmount),
                    RequestCount = g.Count()
                })
                .OrderByDescending(x => x.TotalRevenue)
                .ThenBy(x => x.StationName)
                .Take(5)
                .ToList();

            var model = new AdminDashboardViewModel
            {
                TotalStations = stations.Count,
                AdminManagedStations = stations.Count(x => x.IsAdminManaged),
                UserSubmittedStations = stations.Count(x => !x.IsAdminManaged),
                TotalReviews = stations.Sum(x => x.ReviewCount),
                RevenueToday = paidToday,
                RevenueThisMonth = paidThisMonth,
                TotalRevenue = paidRequests.Sum(x => x.FeeAmount),
                PaidRequestCount = paidRequests.Count,
                PendingPaymentCount = await _context.StationRegistrationRequests.CountAsync(x =>
                    x.ApprovalStatus == StationWorkflowStatus.AwaitingPayment ||
                    x.PaymentStatus == "Đang chờ thanh toán" ||
                    x.PaymentStatus == "Chờ thanh toán demo"),
                RevenueLabels = revenueLabels,
                RevenueValues = revenueValues,
                TopRevenueStations = topRevenueStations,
                Stations = stations
            };

            return View(model);
        }
    }
}
