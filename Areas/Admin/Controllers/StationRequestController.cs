using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using tramsac99.Areas.Admin.Models;
using tramsac99.Areas.Admin.ViewModels;
using tramsac99.Data;

namespace tramsac99.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class StationRequestController : Controller
    {
        private readonly AppDbContext _context;

        public StationRequestController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int registrationPage = 1, int operationPage = 1, string tab = "registration")
        {
            const int registrationPageSize = 4;
            const int operationPageSize = 4;

            var registrationQuery = _context.StationRegistrationRequests
                .Include(x => x.User)
                .OrderBy(x => x.ApprovalStatus == StationWorkflowStatus.Pending ? 0
                    : x.ApprovalStatus == StationWorkflowStatus.AwaitingPayment ? 1
                    : x.ApprovalStatus == StationWorkflowStatus.Approved ? 2
                    : x.ApprovalStatus == StationWorkflowStatus.Completed ? 3
                    : 4)
                .ThenByDescending(x => x.CreatedAt)
                .AsQueryable();

            var operationQuery = _context.StationOperationRequests
                .Include(x => x.User)
                .Include(x => x.Station)
                .OrderBy(x => x.Status == StationWorkflowStatus.Pending ? 0
                    : x.Status == StationWorkflowStatus.Completed ? 1
                    : x.Status == StationWorkflowStatus.Rejected ? 2
                    : 3)
                .ThenByDescending(x => x.CreatedAt)
                .AsQueryable();

            var registrationTotalCount = await registrationQuery.CountAsync();
            var operationTotalCount = await operationQuery.CountAsync();

            var safeRegistrationPage = Math.Max(1, registrationPage);
            var safeOperationPage = Math.Max(1, operationPage);

            var registrationTotalPages = Math.Max(1, (int)Math.Ceiling(registrationTotalCount / (double)registrationPageSize));
            var operationTotalPages = Math.Max(1, (int)Math.Ceiling(operationTotalCount / (double)operationPageSize));

            safeRegistrationPage = Math.Min(safeRegistrationPage, registrationTotalPages);
            safeOperationPage = Math.Min(safeOperationPage, operationTotalPages);

            var model = new StationRequestIndexViewModel
            {
                RegistrationItems = await registrationQuery
                    .Skip((safeRegistrationPage - 1) * registrationPageSize)
                    .Take(registrationPageSize)
                    .ToListAsync(),

                OperationItems = await operationQuery
                    .Skip((safeOperationPage - 1) * operationPageSize)
                    .Take(operationPageSize)
                    .ToListAsync(),

                RegistrationPage = safeRegistrationPage,
                RegistrationPageSize = registrationPageSize,
                RegistrationTotalCount = registrationTotalCount,

                OperationPage = safeOperationPage,
                OperationPageSize = operationPageSize,
                OperationTotalCount = operationTotalCount,

                ActiveTab = string.Equals(tab, "operation", StringComparison.OrdinalIgnoreCase) ? "operation" : "registration",

                PendingRegistrations = await _context.StationRegistrationRequests.CountAsync(x => x.ApprovalStatus == StationWorkflowStatus.Pending),
                PendingOperations = await _context.StationOperationRequests.CountAsync(x => x.Status == StationWorkflowStatus.Pending),
                ApprovedCount = await _context.StationRegistrationRequests.CountAsync(x =>
                        x.ApprovalStatus == StationWorkflowStatus.Approved ||
                        x.ApprovalStatus == StationWorkflowStatus.Completed ||
                        x.CreatedStationId.HasValue)
                    + await _context.StationOperationRequests.CountAsync(x => x.Status == StationWorkflowStatus.Completed),
                RejectedCount = await _context.StationRegistrationRequests.CountAsync(x => x.ApprovalStatus == StationWorkflowStatus.Rejected)
                    + await _context.StationOperationRequests.CountAsync(x => x.Status == StationWorkflowStatus.Rejected)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRegistration(int id, int registrationPage = 1, int operationPage = 1, string tab = "registration")
        {
            var request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (request == null)
            {
                TempData["AdminStationRequestError"] = "Không tìm thấy yêu cầu đăng ký trạm.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            if (request.ApprovalStatus != StationWorkflowStatus.Pending)
            {
                TempData["AdminStationRequestError"] = "Yêu cầu đăng ký trạm này đã được xử lý trước đó.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            request.ApprovalStatus = StationWorkflowStatus.Approved;
            request.ReviewedAt = DateTime.Now;
            request.AdminNote = $"Đã duyệt. Chờ người dùng thanh toán {request.FeeAmount:N0}đ qua PayOS.";
            await _context.SaveChangesAsync();

            TempData["AdminStationRequestSuccess"] = "Đã duyệt yêu cầu đăng ký trạm. User có thể thanh toán để tự động thêm trạm.";
            return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRegistration(int id, string? adminNote, int registrationPage = 1, int operationPage = 1, string tab = "registration")
        {
            var request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (request == null)
            {
                TempData["AdminStationRequestError"] = "Không tìm thấy yêu cầu đăng ký trạm.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            request.ApprovalStatus = StationWorkflowStatus.Rejected;
            request.ReviewedAt = DateTime.Now;
            request.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? "Yêu cầu chưa đạt điều kiện duyệt." : adminNote.Trim();
            await _context.SaveChangesAsync();

            TempData["AdminStationRequestSuccess"] = "Đã từ chối yêu cầu đăng ký trạm.";
            return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveOperation(int id, int registrationPage = 1, int operationPage = 1, string tab = "operation")
        {
            var request = await _context.StationOperationRequests
                .Include(x => x.Station)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (request == null)
            {
                TempData["AdminStationRequestError"] = "Không tìm thấy yêu cầu vận hành.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            if (request.Status != StationWorkflowStatus.Pending)
            {
                TempData["AdminStationRequestError"] = "Yêu cầu này đã được xử lý trước đó.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            if (request.Station == null)
            {
                TempData["AdminStationRequestError"] = "Không tìm thấy trạm cần cập nhật.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            if (request.RequestType == StationOperationRequestType.StatusUpdate)
            {
                var requestedStatus = (request.RequestedStationStatus ?? string.Empty).Trim();
                var allowedStatuses = new[]
                {
                    ChargingStatus.Active,
                    ChargingStatus.Inactive,
                    ChargingStatus.Maintenance,
                    ChargingStatus.Error
                };

                if (!allowedStatuses.Contains(requestedStatus))
                {
                    TempData["AdminStationRequestError"] = "Trạng thái yêu cầu không hợp lệ.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                // Why changed: apply the exact station status only after admin approval.
                request.Station.Status = requestedStatus;

                if (requestedStatus == ChargingStatus.Inactive)
                {
                    var poles = await _context.ChargingPoles
                        .Where(x => x.StationId == request.StationId)
                        .ToListAsync();

                    foreach (var pole in poles)
                    {
                        pole.Status = ChargingStatus.Inactive;
                    }
                }
            }
            else if (request.RequestType == StationOperationRequestType.AddPole)
            {
                var normalizedCode = (request.PoleCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedCode))
                {
                    TempData["AdminStationRequestError"] = "Yêu cầu thêm trụ chưa có mã trụ hợp lệ.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                var duplicatedPole = await _context.ChargingPoles
                    .AnyAsync(x => x.StationId == request.StationId && x.PoleCode == normalizedCode);

                if (duplicatedPole)
                {
                    TempData["AdminStationRequestError"] = $"Mã trụ {normalizedCode} đã tồn tại ở trạm này.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                var nextSortOrder = await _context.ChargingPoles
                    .Where(x => x.StationId == request.StationId)
                    .Select(x => (int?)x.SortOrder)
                    .MaxAsync() ?? 0;

                _context.ChargingPoles.Add(new ChargingPole
                {
                    StationId = request.StationId,
                    PoleCode = normalizedCode,
                    MaxPower = request.PoleMaxPower,
                    Status = ChargingStatus.Active,
                    Note = request.UserNote,
                    SortOrder = nextSortOrder + 1
                });
            }
            else if (request.RequestType == StationOperationRequestType.UpdatePole)
            {
                var targetPole = await _context.ChargingPoles
                    .FirstOrDefaultAsync(x => x.Id == request.PoleId && x.StationId == request.StationId);

                if (targetPole == null)
                {
                    TempData["AdminStationRequestError"] = "Không tìm thấy trụ cần cập nhật.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                var normalizedCode = (request.PoleCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedCode))
                {
                    TempData["AdminStationRequestError"] = "Yêu cầu cập nhật trụ chưa có mã trụ hợp lệ.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                var duplicatedPole = await _context.ChargingPoles
                    .AnyAsync(x => x.StationId == request.StationId && x.Id != targetPole.Id && x.PoleCode == normalizedCode);

                if (duplicatedPole)
                {
                    TempData["AdminStationRequestError"] = $"Mã trụ {normalizedCode} đã tồn tại ở trạm này.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                var requestedPoleStatus = (request.RequestedPoleStatus ?? string.Empty).Trim();
                var allowedStatuses = new[]
                {
                    ChargingStatus.Active,
                    ChargingStatus.Inactive,
                    ChargingStatus.Maintenance,
                    ChargingStatus.Error
                };

                if (!allowedStatuses.Contains(requestedPoleStatus))
                {
                    TempData["AdminStationRequestError"] = "Trạng thái trụ yêu cầu không hợp lệ.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                // Why changed: update the pole only after admin approval.
                targetPole.PoleCode = normalizedCode;
                targetPole.MaxPower = request.PoleMaxPower;
                targetPole.Status = requestedPoleStatus;
                targetPole.Note = request.UserNote;
            }
            else if (request.RequestType == StationOperationRequestType.DeletePole)
            {
                var targetPole = await _context.ChargingPoles
                    .FirstOrDefaultAsync(x => x.Id == request.PoleId && x.StationId == request.StationId);

                if (targetPole == null)
                {
                    TempData["AdminStationRequestError"] = "Không tìm thấy trụ cần xóa.";
                    return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
                }

                // Why changed: delete the pole only when the admin approves this request.
                _context.ChargingPoles.Remove(targetPole);
            }
            else
            {
                TempData["AdminStationRequestError"] = "Loại yêu cầu vận hành chưa được hỗ trợ.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            request.Status = StationWorkflowStatus.Completed;
            request.AdminNote = string.IsNullOrWhiteSpace(request.AdminNote)
                ? "Đã duyệt và áp dụng vào hệ thống."
                : request.AdminNote;
            request.ReviewedAt = DateTime.Now;
            request.CompletedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["AdminStationRequestSuccess"] = "Đã duyệt yêu cầu và cập nhật dữ liệu thành công.";
            return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectOperation(int id, string? adminNote, int registrationPage = 1, int operationPage = 1, string tab = "operation")
        {
            var request = await _context.StationOperationRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (request == null)
            {
                TempData["AdminStationRequestError"] = "Không tìm thấy yêu cầu vận hành.";
                return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
            }

            request.Status = StationWorkflowStatus.Rejected;
            request.ReviewedAt = DateTime.Now;
            request.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? "Admin đã từ chối yêu cầu." : adminNote.Trim();
            await _context.SaveChangesAsync();

            TempData["AdminStationRequestSuccess"] = "Đã từ chối yêu cầu vận hành.";
            return RedirectToAction(nameof(Index), new { registrationPage, operationPage, tab });
        }
    }
}
