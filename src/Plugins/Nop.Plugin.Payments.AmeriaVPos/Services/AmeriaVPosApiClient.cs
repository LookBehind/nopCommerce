using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Nop.Core;

namespace Nop.Plugin.Payments.AmeriaVPos.Services
{
    /// <summary>
    /// Represents the HTTP client to request AmeriaBank vPOS 3.1 REST services
    /// </summary>
    public class AmeriaVPosApiClient
    {
        #region Fields

        private readonly HttpClient _httpClient;
        private readonly AmeriaVPosSettings _ameriaVPosSettings;
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        #endregion

        #region Ctor

        public AmeriaVPosApiClient(HttpClient client, AmeriaVPosSettings ameriaVPosSettings)
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, $"nopCommerce-{NopVersion.CURRENT_VERSION}");

            _httpClient = client;
            _ameriaVPosSettings = ameriaVPosSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Base URL for the hosted pay page the customer is redirected to (admin-configured -
        /// AmeriaBank has not issued a production hostname yet, only the sandbox one)
        /// </summary>
        public string PayBaseUrl => _ameriaVPosSettings.PayBaseUrl;

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string action, TRequest request)
        {
            var response = await _httpClient.PostAsJsonAsync($"{_ameriaVPosSettings.ApiBaseUrl}/api/VPOS/{action}", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
        }

        #endregion

        #region Methods

        public Task<InitPaymentResponse> InitPaymentAsync(InitPaymentRequest request) =>
            PostAsync<InitPaymentRequest, InitPaymentResponse>("InitPayment", request);

        public Task<PaymentDetailsResponse> GetPaymentDetailsAsync(PaymentDetailsRequest request) =>
            PostAsync<PaymentDetailsRequest, PaymentDetailsResponse>("GetPaymentDetails", request);

        public Task<VPosActionResponse> RefundPaymentAsync(RefundPaymentRequest request) =>
            PostAsync<RefundPaymentRequest, VPosActionResponse>("RefundPayment", request);

        public Task<VPosActionResponse> CancelPaymentAsync(CancelPaymentRequest request) =>
            PostAsync<CancelPaymentRequest, VPosActionResponse>("CancelPayment", request);

        #endregion
    }
}
