using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using tramsac99.Areas.Admin.Models;
using tramsac99.Areas.Admin.Models.Dto;
using tramsac99.Data;
using tramsac99.Services;

namespace tramsac99.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/api/stations")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class StationApiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ChargingHierarchyService _hierarchyService;

        public StationApiController(AppDbContext context, ChargingHierarchyService hierarchyService)
        {
            _context = context;
            _hierarchyService = hierarchyService;
        }

        [HttpGet]
        public async Task<IActionResult> GetStations()
        {
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

            return Ok(stations);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetStationById(int id)
        {
            var station = await _context.ChargingStations
                .Where(s => s.Id == id)
                .Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    address = s.Address,
                    latitude = s.Latitude,
                    longitude = s.Longitude,
                    status = s.Status,
                    ownerUserId = s.OwnerUserId,
                    isAdminManaged = !s.OwnerUserId.HasValue
                })
                .FirstOrDefaultAsync();

            if (station == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            return Ok(station);
        }

        [HttpPost]
        public async Task<IActionResult> CreateStation([FromBody] ChargingStation newStation)
        {
            if (newStation == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            try
            {
                newStation.Name = newStation.Name?.Trim();
                newStation.Address = newStation.Address?.Trim();
                newStation.Status = NormalizeStationStatus4(newStation.Status);

                // Why changed: admin-created stations stay unmanaged by any user account.
                newStation.OwnerUserId = null;
                newStation.ChargerType = null;
                newStation.Power = null;
                newStation.PricePerKwh = 0;

                if (string.IsNullOrWhiteSpace(newStation.Name) || string.IsNullOrWhiteSpace(newStation.Address))
                    return BadRequest(new { message = "Vui lòng nhập tên trạm và địa chỉ." });

                if (double.IsNaN(newStation.Latitude) || double.IsNaN(newStation.Longitude))
                    return BadRequest(new { message = "Vĩ độ hoặc kinh độ không hợp lệ." });

                _context.ChargingStations.Add(newStation);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Thêm trạm thành công." });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new { message = $"Lỗi lưu dữ liệu trạm: {GetInnermostMessage(ex)}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Lỗi thêm trạm: {GetInnermostMessage(ex)}" });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateStation(int id, [FromBody] ChargingStation updatedStation)
        {
            if (updatedStation == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            try
            {
                var station = await _context.ChargingStations
                    .Include(x => x.ChargingPoles)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (station == null)
                    return NotFound(new { message = "Không tìm thấy trạm." });

                var name = updatedStation.Name?.Trim();
                var address = updatedStation.Address?.Trim();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
                    return BadRequest(new { message = "Vui lòng nhập tên trạm và địa chỉ." });

                station.Name = name;
                station.Address = address;
                station.Latitude = updatedStation.Latitude;
                station.Longitude = updatedStation.Longitude;
                station.Status = NormalizeStationStatus4(updatedStation.Status);

                if (station.Status == ChargingStatus.Inactive ||
                    station.Status == ChargingStatus.Maintenance ||
                    station.Status == ChargingStatus.Error)
                {
                    foreach (var pole in station.ChargingPoles)
                    {
                        pole.Status = station.Status;
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "Cập nhật trạm thành công." });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new { message = $"Lỗi cập nhật dữ liệu trạm: {GetInnermostMessage(ex)}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Lỗi cập nhật trạm: {GetInnermostMessage(ex)}" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteStation(int id)
        {
            var station = await _context.ChargingStations
                .Include(x => x.OwnerUser)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (station == null)
                return NotFound(new { message = "Không tìm thấy trạm." });

            var now = DateTime.Now;
            var stationName = station.Name?.Trim() ?? $"Trạm #{station.Id}";
            var isUserSubmitted = station.OwnerUserId.HasValue;

            if (isUserSubmitted)
            {
                var owner = station.OwnerUser;
                if (owner != null)
                {
                    _context.SupportRequests.Add(new SupportRequest
                    {
                        SenderUserId = owner.Id,
                        SenderUserName = owner.Username,
                        FullName = string.IsNullOrWhiteSpace(owner.FullName) ? owner.Username : owner.FullName.Trim(),
                        Email = owner.Email,
                        PhoneNumber = null,
                        Subject = "Thông báo xóa trạm sạc từ admin",
                        Message = $"Admin đã xóa trạm sạc '{stationName}' khỏi hệ thống. Nếu bạn cần hỗ trợ hoặc muốn gửi lại hồ sơ, vui lòng vào mục Liên hệ để trao đổi với admin.",
                        Status = "Đã xử lý",
                        IsRead = true,
                        CreatedAt = now,
                        ReadAt = now,
                        ResolvedAt = now,
                        AdminReply = $"Trạm '{stationName}' đã bị xóa khỏi hệ thống bởi admin.",
                        LastStatusChangedAt = now,
                        IsUserSeen = false,
                        UserSeenAt = null
                    });
                }

                var relatedRequests = await _context.StationRegistrationRequests
                    .Where(x => x.CreatedStationId == station.Id)
                    .ToListAsync();

                foreach (var request in relatedRequests)
                {
                    request.AdminNote = $"Admin đã xóa trạm '{stationName}' khỏi hệ thống vào {now:dd/MM/yyyy HH:mm}.";
                }
            }

            _context.ChargingStations.Remove(station);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = isUserSubmitted
                    ? "Đã xóa trạm và gửi thông báo về user trong mục Liên hệ."
                    : "Xóa trạm thành công."
            });
        }

        private static string NormalizeStationStatus4(string? value)
        {
            var input = (value ?? string.Empty).Trim();

            var allowedStatuses = new[]
            {
                ChargingStatus.Active,
                ChargingStatus.Inactive,
                ChargingStatus.Maintenance,
                ChargingStatus.Error
            };

            return allowedStatuses.Contains(input) ? input : ChargingStatus.Active;
        }

        private static string GetInnermostMessage(Exception ex)
        {
            var current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
        }
    }
}
