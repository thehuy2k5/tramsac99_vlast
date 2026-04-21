using tramsac99.Areas.Admin.Models;

namespace tramsac99.Areas.Admin.ViewModels
{
    public class StationRequestIndexViewModel
    {
        public List<StationRegistrationRequest> RegistrationItems { get; set; } = new();
        public List<StationOperationRequest> OperationItems { get; set; } = new();

        public int RegistrationPage { get; set; } = 1;
        public int RegistrationPageSize { get; set; } = 4;
        public int RegistrationTotalCount { get; set; }
        public int RegistrationTotalPages => Math.Max(1, (int)Math.Ceiling(RegistrationTotalCount / (double)Math.Max(1, RegistrationPageSize)));

        public int OperationPage { get; set; } = 1;
        public int OperationPageSize { get; set; } = 4;
        public int OperationTotalCount { get; set; }
        public int OperationTotalPages => Math.Max(1, (int)Math.Ceiling(OperationTotalCount / (double)Math.Max(1, OperationPageSize)));

        public string ActiveTab { get; set; } = "registration";

        public int PendingRegistrations { get; set; }
        public int PendingOperations { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
    }
}
