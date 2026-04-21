using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using tramsac99.Areas.Admin.Models;
using tramsac99.Areas.User.ViewModels;
using tramsac99.Data;
using tramsac99.Services;

namespace tramsac99.Areas.User.Controllers
{
    [Area("User")]
    [Authorize]
    public class StationRegistrationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PayOsCheckoutService _payOsCheckoutService;

        public StationRegistrationController(
            AppDbContext context,
            IWebHostEnvironment environment,
            PayOsCheckoutService payOsCheckoutService)
        {
            _context = context;
            _environment = environment;
            _payOsCheckoutService = payOsCheckoutService;
        }

        [HttpGet]
        public IActionResult Register()
        {
            var model = new RegisterStationRequestViewModel
            {
                ContactEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                OperatorName = User.Identity?.Name ?? string.Empty
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterStationRequestViewModel model)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            if (string.IsNullOrWhiteSpace(model.Address))
            {
                ModelState.AddModelError(nameof(model.Address), "Vui lòng chọn địa chỉ từ bản đồ hoặc ô gợi ý.");
            }

            if (!ChargerTypeCatalog.IsValid(model.InitialPoleChargerType))
            {
                ModelState.AddModelError(nameof(model.InitialPoleChargerType), "Loại sạc không hợp lệ. Vui lòng chọn lại từ danh sách.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var imageUrl = await SaveRequestImageAsync(model.ImageFile);

            var request = new StationRegistrationRequest
            {
                UserId = user.Id,
                StationName = model.StationName.Trim(),
                OperatorName = model.OperatorName.Trim(),
                ContactEmail = model.ContactEmail.Trim(),
                ContactPhone = model.ContactPhone.Trim(),
                Address = model.Address.Trim(),
                Latitude = model.Latitude,
                Longitude = model.Longitude,
                Description = model.Description?.Trim(),
                ImageUrl = imageUrl,
                InitialPoleCount = model.InitialPoleCount,
                InitialPoleChargerType = ChargerTypeCatalog.Normalize(model.InitialPoleChargerType),
                InitialPoleMaxPower = ChargingStatus.NormalizeKw(model.InitialPoleMaxPower),
                InitialPoleNote = model.InitialPoleNote?.Trim(),
                ApprovalStatus = StationWorkflowStatus.Pending,
                PaymentStatus = "Chưa thanh toán",
                FeeAmount = Math.Max(1, model.InitialPoleCount) * 5000m,
                CreatedAt = DateTime.Now
            };

            _context.StationRegistrationRequests.Add(request);
            await _context.SaveChangesAsync();

            TempData["StationRequestSuccess"] = $"Đã gửi yêu cầu đăng ký trạm. Admin sẽ duyệt trước khi bạn thanh toán {request.FeeAmount:N0}đ.";
            return RedirectToAction(nameof(MyStations));
        }

        [HttpGet]
        public async Task<IActionResult> MyStations()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var model = await BuildMyStationDashboardAsync(currentUserId.Value);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> MyRegistrations()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var model = await BuildMyStationDashboardAsync(currentUserId.Value);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> MyHistory()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var model = await BuildMyStationDashboardAsync(currentUserId.Value);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            // Why changed: keep history and pole list paged on the server to reduce page height.
            var model = await BuildManageStationDetailsAsync(id, currentUserId.Value, historyPage, polePage);
            if (model == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trạm bạn cần xem chi tiết.";
                return RedirectToAction(nameof(MyStations));
            }

            return View(model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartPayment(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUserId.Value);
            if (request == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy yêu cầu đăng ký trạm.";
                return RedirectToAction(nameof(MyStations));
            }

            if (request.ApprovalStatus != StationWorkflowStatus.Approved && request.ApprovalStatus != StationWorkflowStatus.AwaitingPayment)
            {
                TempData["StationRequestError"] = "Yêu cầu này chưa được admin duyệt để thanh toán.";
                return RedirectToAction(nameof(MyStations));
            }

            if (request.CreatedStationId.HasValue)
            {
                TempData["StationRequestSuccess"] = "Yêu cầu này đã hoàn tất.";
                return RedirectToAction(nameof(MyStations));
            }

            var returnUrl = Url.Action(nameof(PaymentReturn), "StationRegistration", new { area = "User", requestId = request.Id }, Request.Scheme) ?? string.Empty;
            var cancelUrl = Url.Action(nameof(PaymentReturn), "StationRegistration", new { area = "User", requestId = request.Id }, Request.Scheme) ?? string.Empty;
            var fallbackUrl = Url.Action(nameof(DemoComplete), "StationRegistration", new { area = "User", id = request.Id }, Request.Scheme) ?? string.Empty;

            var payment = await _payOsCheckoutService.CreateStationPaymentAsync(request, returnUrl, cancelUrl, fallbackUrl);
            request.PayOsOrderCode = payment.orderCode;
            request.PayOsCheckoutUrl = payment.checkoutUrl;
            request.ApprovalStatus = StationWorkflowStatus.AwaitingPayment;
            request.PaymentStatus = payment.isFallback ? "Chờ thanh toán demo" : "Đang chờ thanh toán";
            await _context.SaveChangesAsync();

            return Redirect(payment.checkoutUrl);
        }

        [HttpGet]
        public async Task<IActionResult> PaymentReturn(int? requestId, long? orderCode, string? status, string? code, bool? cancel)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            StationRegistrationRequest? request = null;

            if (requestId.HasValue)
            {
                request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.Id == requestId.Value && x.UserId == currentUserId.Value);
            }

            if (request == null && orderCode.HasValue)
            {
                request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.PayOsOrderCode == orderCode.Value && x.UserId == currentUserId.Value);
            }

            if (request == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy giao dịch thanh toán.";
                return RedirectToAction(nameof(MyStations));
            }

            var normalizedStatus = (status ?? string.Empty).Trim().ToUpperInvariant();
            var isPaid = normalizedStatus == "PAID" && cancel != true && string.Equals(code, "00", StringComparison.OrdinalIgnoreCase);
            var isCancelled = cancel == true || normalizedStatus == "CANCELLED";

            if (isPaid)
            {
                await CompletePaidStationRequestAsync(request);
                TempData["StationRequestSuccess"] = "Thanh toán thành công. Trạm đã được thêm tự động vào mục Trạm của tôi.";
            }
            else if (isCancelled)
            {
                request.PaymentStatus = "Đã hủy thanh toán";
                request.ApprovalStatus = StationWorkflowStatus.AwaitingPayment;
                await _context.SaveChangesAsync();
                TempData["StationRequestError"] = "Bạn đã hủy thanh toán PayOS. Có thể thanh toán lại bất kỳ lúc nào.";
            }
            else
            {
                TempData["StationRequestError"] = "Chưa ghi nhận thanh toán thành công. Vui lòng kiểm tra lại giao dịch.";
            }

            return RedirectToAction(nameof(MyStations));
        }

        [HttpGet]
        public async Task<IActionResult> DemoComplete(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var request = await _context.StationRegistrationRequests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == currentUserId.Value);
            if (request == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy yêu cầu cần thanh toán demo.";
                return RedirectToAction(nameof(MyStations));
            }

            await CompletePaidStationRequestAsync(request);
            TempData["StationRequestSuccess"] = "Đã chạy luồng thanh toán demo. Trạm đã được thêm vào danh sách của bạn.";
            return RedirectToAction(nameof(MyStations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStatusRequest(StationStatusRequestViewModel model, bool returnToDetails = false, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var redirectAction = ResolveStationRedirect(returnToDetails, model.StationId, historyPage, polePage);

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == model.StationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Bạn không có quyền gửi yêu cầu cho trạm này.";
                return redirectAction;
            }

            if (!ModelState.IsValid)
            {
                TempData["StationRequestError"] = "Dữ liệu yêu cầu cập nhật trạng thái chưa hợp lệ.";
                return redirectAction;
            }

            var requestedStatus = (model.RequestedStatus ?? string.Empty).Trim();

            // Why changed: save exactly the status selected by user instead of forcing it through old normalization logic.
            var allowedStatuses = new[]
            {
        ChargingStatus.Active,
        ChargingStatus.Inactive,
        ChargingStatus.Maintenance,
        ChargingStatus.Error
    };

            if (!allowedStatuses.Contains(requestedStatus))
            {
                TempData["StationRequestError"] = "Trạng thái yêu cầu không hợp lệ.";
                return redirectAction;
            }

            _context.StationOperationRequests.Add(new StationOperationRequest
            {
                StationId = station.Id,
                UserId = currentUserId.Value,
                RequestType = StationOperationRequestType.StatusUpdate,
                RequestedStationStatus = requestedStatus,
                UserNote = model.Note?.Trim(),
                Status = StationWorkflowStatus.Pending,
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["StationRequestSuccess"] = "Đã gửi yêu cầu cập nhật trạng thái. Admin sẽ duyệt trước khi áp dụng.";
            return redirectAction;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAddPoleRequest(AddPoleRequestViewModel model, bool returnToDetails = false, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var redirectAction = ResolveStationRedirect(returnToDetails, model.StationId, historyPage, polePage);

            var station = await _context.ChargingStations.FirstOrDefaultAsync(x => x.Id == model.StationId && x.OwnerUserId == currentUserId.Value);
            if (station == null)
            {
                TempData["StationRequestError"] = "Bạn không có quyền gửi yêu cầu cho trạm này.";
                return redirectAction;
            }

            if (!ModelState.IsValid)
            {
                TempData["StationRequestError"] = "Dữ liệu yêu cầu thêm trụ chưa hợp lệ.";
                return redirectAction;
            }

            _context.StationOperationRequests.Add(new StationOperationRequest
            {
                StationId = station.Id,
                UserId = currentUserId.Value,
                RequestType = StationOperationRequestType.AddPole,
                PoleCode = model.PoleCode.Trim(),
                PoleMaxPower = ChargingStatus.NormalizeKw(model.MaxPower),
                UserNote = model.Note?.Trim(),
                Status = StationWorkflowStatus.Pending,
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["StationRequestSuccess"] = "Đã gửi yêu cầu thêm trụ. Admin sẽ duyệt trước khi thêm vào hệ thống.";
            return redirectAction;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAddPoleBatchRequest(BulkAddPoleRequestViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == model.StationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Bạn không có quyền gửi yêu cầu cho trạm này.";
                return RedirectToAction(nameof(MyStations));
            }

            if (model.Poles == null || !model.Poles.Any())
            {
                TempData["StationRequestError"] = "Vui lòng thêm ít nhất 1 trụ trước khi gửi yêu cầu.";
                return RedirectToAction(nameof(MyStations));
            }

            var validPoles = model.Poles
                .Where(x => !string.IsNullOrWhiteSpace(x.PoleCode))
                .Select(x => new
                {
                    PoleCode = x.PoleCode!.Trim(),
                    MaxPower = ChargingStatus.NormalizeKw(x.MaxPower),
                    Note = x.Note?.Trim()
                })
                .ToList();

            if (!validPoles.Any())
            {
                TempData["StationRequestError"] = "Danh sách trụ chưa hợp lệ. Mỗi trụ cần có mã trụ.";
                return RedirectToAction(nameof(MyStations));
            }

            // Why changed: support staging many pole requests from one table UI.
            foreach (var pole in validPoles)
            {
                _context.StationOperationRequests.Add(new StationOperationRequest
                {
                    StationId = station.Id,
                    UserId = currentUserId.Value,
                    RequestType = StationOperationRequestType.AddPole,
                    PoleCode = pole.PoleCode,
                    PoleMaxPower = pole.MaxPower,
                    UserNote = pole.Note,
                    Status = StationWorkflowStatus.Pending,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            TempData["StationRequestSuccess"] = $"Đã gửi {validPoles.Count} yêu cầu thêm trụ. Admin sẽ duyệt trước khi thêm vào hệ thống.";
            return RedirectToAction(nameof(MyStations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUpdatePoleRequest(UpdatePoleRequestViewModel model, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var redirectAction = RedirectToAction(nameof(Details), new { id = model.StationId, historyPage, polePage });

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == model.StationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Bạn không có quyền cập nhật trụ của trạm này.";
                return redirectAction;
            }

            var pole = await _context.ChargingPoles
                .FirstOrDefaultAsync(x => x.Id == model.PoleId && x.StationId == model.StationId);

            if (pole == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trụ cần cập nhật.";
                return redirectAction;
            }

            if (!ModelState.IsValid)
            {
                TempData["StationRequestError"] = "Dữ liệu yêu cầu cập nhật trụ chưa hợp lệ.";
                return redirectAction;
            }

            var allowedStatuses = new[]
            {
                ChargingStatus.Active,
                ChargingStatus.Inactive,
                ChargingStatus.Maintenance,
                ChargingStatus.Error
            };

            if (!allowedStatuses.Contains(model.RequestedStatus))
            {
                TempData["StationRequestError"] = "Trạng thái trụ yêu cầu không hợp lệ.";
                return redirectAction;
            }

            _context.StationOperationRequests.Add(new StationOperationRequest
            {
                StationId = station.Id,
                UserId = currentUserId.Value,
                RequestType = StationOperationRequestType.UpdatePole,
                PoleId = pole.Id,
                PoleCode = model.PoleCode.Trim(),
                PoleMaxPower = ChargingStatus.NormalizeKw(model.MaxPower),
                RequestedPoleStatus = model.RequestedStatus,
                UserNote = model.Note?.Trim(),
                Status = StationWorkflowStatus.Pending,
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["StationRequestSuccess"] = "Đã gửi yêu cầu cập nhật trụ. Admin sẽ duyệt trước khi áp dụng.";
            return redirectAction;
        }

        [HttpGet]
        public async Task<IActionResult> RequestAddPole(int stationId, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var station = await _context.ChargingStations
                .Include(x => x.ChargingPoles)
                .FirstOrDefaultAsync(x => x.Id == stationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trạm hoặc bạn không có quyền thêm trụ cho trạm này.";
                return RedirectToAction(nameof(MyStations));
            }

            var model = new RequestAddPolePageViewModel
            {
                StationId = station.Id,
                StationName = station.Name ?? string.Empty,
                StationAddress = station.Address ?? string.Empty,
                StationStatus = station.Status ?? ChargingStatus.Active,
                ExistingPoleCount = station.ChargingPoles.Count,
                HistoryPage = Math.Max(1, historyPage),
                PolePage = Math.Max(1, polePage)
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> RequestUpdatePole(int stationId, int poleId, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == stationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trạm hoặc bạn không có quyền cập nhật trụ của trạm này.";
                return RedirectToAction(nameof(MyStations));
            }

            var pole = await _context.ChargingPoles
                .FirstOrDefaultAsync(x => x.Id == poleId && x.StationId == stationId);

            if (pole == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trụ cần cập nhật.";
                return RedirectToAction(nameof(Details), new { id = stationId, historyPage, polePage });
            }

            var model = new RequestUpdatePolePageViewModel
            {
                StationId = station.Id,
                PoleId = pole.Id,
                StationName = station.Name ?? string.Empty,
                StationAddress = station.Address ?? string.Empty,
                StationStatus = station.Status ?? ChargingStatus.Active,
                PoleCode = pole.PoleCode,
                MaxPower = pole.MaxPower,
                RequestedStatus = pole.Status,
                CurrentPoleStatus = pole.Status,
                CurrentPolePower = pole.MaxPower,
                CurrentPoleNote = pole.Note,
                Note = pole.Note,
                HistoryPage = Math.Max(1, historyPage),
                PolePage = Math.Max(1, polePage)
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> RequestDeletePole(int stationId, int poleId, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == stationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trạm hoặc bạn không có quyền xóa trụ của trạm này.";
                return RedirectToAction(nameof(MyStations));
            }

            var pole = await _context.ChargingPoles
                .FirstOrDefaultAsync(x => x.Id == poleId && x.StationId == stationId);

            if (pole == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trụ cần xóa.";
                return RedirectToAction(nameof(Details), new { id = stationId, historyPage, polePage });
            }

            var model = new RequestDeletePolePageViewModel
            {
                StationId = station.Id,
                PoleId = pole.Id,
                StationName = station.Name ?? string.Empty,
                PoleCode = pole.PoleCode,
                PoleStatus = pole.Status,
                PoleMaxPower = pole.MaxPower,
                PoleNote = pole.Note,
                HistoryPage = Math.Max(1, historyPage),
                PolePage = Math.Max(1, polePage)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDeletePoleRequest(DeletePoleRequestViewModel model, int historyPage = 1, int polePage = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return RedirectToAction("Login", "Account", new { area = "User" });
            }

            var redirectAction = RedirectToAction(nameof(Details), new { id = model.StationId, historyPage, polePage });

            var station = await _context.ChargingStations
                .FirstOrDefaultAsync(x => x.Id == model.StationId && x.OwnerUserId == currentUserId.Value);

            if (station == null)
            {
                TempData["StationRequestError"] = "Bạn không có quyền xóa trụ của trạm này.";
                return redirectAction;
            }

            var pole = await _context.ChargingPoles
                .FirstOrDefaultAsync(x => x.Id == model.PoleId && x.StationId == model.StationId);

            if (pole == null)
            {
                TempData["StationRequestError"] = "Không tìm thấy trụ cần xóa.";
                return redirectAction;
            }

            _context.StationOperationRequests.Add(new StationOperationRequest
            {
                StationId = station.Id,
                UserId = currentUserId.Value,
                RequestType = StationOperationRequestType.DeletePole,
                PoleId = pole.Id,
                PoleCode = pole.PoleCode,
                PoleMaxPower = pole.MaxPower,
                RequestedPoleStatus = pole.Status,
                UserNote = model.Note?.Trim(),
                Status = StationWorkflowStatus.Pending,
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["StationRequestSuccess"] = "Đã gửi yêu cầu xóa trụ. Admin sẽ duyệt trước khi xóa khỏi hệ thống.";
            return redirectAction;
        }

        private async Task CompletePaidStationRequestAsync(StationRegistrationRequest request)
        {
            if (request.CreatedStationId.HasValue)
            {
                return;
            }

            var station = new ChargingStation
            {
                Name = request.StationName,
                Address = request.Address,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Status = ChargingStatus.Active,
                OwnerUserId = request.UserId,
                ChargerType = request.InitialPoleChargerType,
                Power = request.InitialPoleMaxPower,
                PricePerKwh = 0
            };

            _context.ChargingStations.Add(station);
            await _context.SaveChangesAsync();

            if (request.InitialPoleCount > 0)
            {
                for (var index = 1; index <= request.InitialPoleCount; index++)
                {
                    _context.ChargingPoles.Add(new ChargingPole
                    {
                        StationId = station.Id,
                        PoleCode = $"TRU-{index:00}",
                        ChargerType = request.InitialPoleChargerType,
                        MaxPower = request.InitialPoleMaxPower,
                        Status = ChargingStatus.Active,
                        Note = request.InitialPoleNote,
                        SortOrder = index
                    });
                }

                await _context.SaveChangesAsync();
            }

            request.PaymentStatus = "Đã thanh toán";
            request.ApprovalStatus = StationWorkflowStatus.Completed;
            request.PaidAt = DateTime.Now;
            request.CompletedAt = DateTime.Now;
            request.CreatedStationId = station.Id;

            await _context.SaveChangesAsync();
        }

        private async Task<string?> SaveRequestImageAsync(IFormFile? imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return null;
            }

            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowedExtensions.Contains(extension))
            {
                return null;
            }

            var folder = Path.Combine(_environment.WebRootPath, "uploads", "station-requests");
            Directory.CreateDirectory(folder);

            // Why changed: store request evidence images in a dedicated folder.
            var fileName = $"station-request-{Guid.NewGuid():N}{extension}";
            var savePath = Path.Combine(folder, fileName);

            await using var stream = new FileStream(savePath, FileMode.Create);
            await imageFile.CopyToAsync(stream);

            return $"/uploads/station-requests/{fileName}";
        }

        private async Task<MyStationDashboardViewModel> BuildMyStationDashboardAsync(int currentUserId)
        {
            return new MyStationDashboardViewModel
            {
                Stations = await _context.ChargingStations
                    .Where(x => x.OwnerUserId == currentUserId)
                    .Include(x => x.ChargingPoles)
                    .OrderByDescending(x => x.Id)
                    .ToListAsync(),

                RegistrationRequests = await _context.StationRegistrationRequests
                    .Where(x => x.UserId == currentUserId)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync(),

                OperationRequests = await _context.StationOperationRequests
                    .Where(x => x.UserId == currentUserId)
                    .Include(x => x.Station)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync()
            };
        }


        private async Task<ManageStationDetailsViewModel?> BuildManageStationDetailsAsync(int stationId, int currentUserId, int historyPage = 1, int polePage = 1)
        {
            const int historyPageSize = 4;
            const int polePageSize = 4;

            var station = await _context.ChargingStations
                .Where(x => x.Id == stationId && x.OwnerUserId == currentUserId)
                .Include(x => x.ChargingPoles)
                .FirstOrDefaultAsync();

            if (station == null)
            {
                return null;
            }

            var latestPhone = await _context.StationRegistrationRequests
                .Where(x => x.CreatedStationId == station.Id)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.ContactPhone)
                .FirstOrDefaultAsync();

            var operationRequests = await _context.StationOperationRequests
                .Where(x => x.StationId == station.Id && x.UserId == currentUserId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            var allPoles = station.ChargingPoles
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new PoleItemViewModel
                {
                    Id = x.Id,
                    PoleCode = x.PoleCode,
                    MaxPower = x.MaxPower,
                    Status = x.Status,
                    Note = x.Note
                })
                .ToList();

            var safeHistoryPage = Math.Max(1, historyPage);
            var safePolePage = Math.Max(1, polePage);
            var historyTotalPages = Math.Max(1, (int)Math.Ceiling(operationRequests.Count / (double)historyPageSize));
            var poleTotalPages = Math.Max(1, (int)Math.Ceiling(allPoles.Count / (double)polePageSize));

            safeHistoryPage = Math.Min(safeHistoryPage, historyTotalPages);
            safePolePage = Math.Min(safePolePage, poleTotalPages);

            return new ManageStationDetailsViewModel
            {
                StationId = station.Id,
                Name = station.Name ?? string.Empty,
                Address = station.Address ?? string.Empty,
                Status = station.Status ?? ChargingStatus.Active,
                Latitude = station.Latitude,
                Longitude = station.Longitude,
                PhoneNumber = string.IsNullOrWhiteSpace(latestPhone) ? "-" : latestPhone,
                TotalPoleCount = station.ChargingPoles.Count,
                LatestOperationAt = operationRequests.FirstOrDefault()?.CreatedAt,
                Poles = allPoles,
                OperationRequests = operationRequests,

                HistoryItems = operationRequests
                    .Skip((safeHistoryPage - 1) * historyPageSize)
                    .Take(historyPageSize)
                    .ToList(),
                HistoryPage = safeHistoryPage,
                HistoryPageSize = historyPageSize,
                HistoryTotalCount = operationRequests.Count,

                PoleItems = allPoles
                    .Skip((safePolePage - 1) * polePageSize)
                    .Take(polePageSize)
                    .ToList(),
                PolePage = safePolePage,
                PolePageSize = polePageSize,
                PoleTotalCount = allPoles.Count
            };
        }

        private IActionResult ResolveStationRedirect(bool returnToDetails, int stationId, int historyPage = 1, int polePage = 1)
        {
            // Why changed: keep the user on the same detail page and preserve pagination after submitting actions.
            return returnToDetails
                ? RedirectToAction(nameof(Details), new { id = stationId, historyPage, polePage })
                : RedirectToAction(nameof(MyStations));
        }

        private async Task<AppUser?> GetCurrentUserAsync()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
            {
                return null;
            }

            return await _context.AppUsers.FirstOrDefaultAsync(x => x.Id == currentUserId.Value);
        }

        private int? GetCurrentUserId()
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(rawUserId, out var userId))
            {
                return userId;
            }

            return null;
        }
    }
}
