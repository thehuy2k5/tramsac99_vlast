using PayOS;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;
using tramsac99.Areas.Admin.Models;

namespace tramsac99.Services
{
    public class PayOsCheckoutService
    {
        private readonly IConfiguration _configuration;

        public PayOsCheckoutService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool IsConfigured()
        {
            var clientId = _configuration["PayOS:ClientId"];
            var apiKey = _configuration["PayOS:ApiKey"];
            var checksumKey = _configuration["PayOS:ChecksumKey"];

            // Why changed: read config once and avoid nullable warnings.
            return !string.IsNullOrWhiteSpace(clientId)
                && !string.IsNullOrWhiteSpace(apiKey)
                && !string.IsNullOrWhiteSpace(checksumKey);
        }

        public async Task<(long orderCode, string checkoutUrl, bool isFallback)> CreateStationPaymentAsync(
            StationRegistrationRequest request,
            string returnUrl,
            string cancelUrl,
            string fallbackUrl)
        {
            var orderCode = request.PayOsOrderCode ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var clientId = _configuration["PayOS:ClientId"];
            var apiKey = _configuration["PayOS:ApiKey"];
            var checksumKey = _configuration["PayOS:ChecksumKey"];

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(checksumKey))
            {
                // Why changed: keep payment flow testable before real PayOS keys are added.
                return (orderCode, fallbackUrl, true);
            }

            // Why changed: create client only after config values are confirmed non-null.
            var client = new PayOSClient(clientId, apiKey, checksumKey);

            // Why changed: this request type matches the official payOS .NET SDK v2 usage.
            var paymentRequest = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = (int)request.FeeAmount,
                Description = $"Dang ky tram #{request.Id}",
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl
            };

            var paymentLink = await client.PaymentRequests.CreateAsync(paymentRequest);

            // Why changed: return official checkout URL from payOS.
            return (orderCode, paymentLink.CheckoutUrl, false);
        }
    }
}