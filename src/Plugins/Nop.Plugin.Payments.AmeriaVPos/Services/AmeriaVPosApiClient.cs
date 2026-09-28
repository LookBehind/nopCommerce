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

        // Requests (InitPaymentRequest.CardHolderID left null for every ordinary,
        // non-binding payment - the overwhelming majority) must OMIT null properties
        // entirely, not send them as explicit JSON nulls - confirmed live against the
        // sandbox: a bare {"CardHolderID": null, ...} InitPayment call throws their
        // side into "550 System Error" specifically on the strict amount=10 test path
        // (ordinary amounts tolerate it fine, which is why this went unnoticed through
        // every real prod charge). Omitting the key outright reproduces the exact same
        // payload real binding-less payments need and sidesteps their bug.
        private static readonly JsonSerializerOptions _requestJsonOptions =
            new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

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
            var response = await _httpClient.PostAsJsonAsync($"{_ameriaVPosSettings.ApiBaseUrl}/api/VPOS/{action}", request, _requestJsonOptions);
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

        public Task<MakeBindingPaymentResponse> MakeBindingPaymentAsync(MakeBindingPaymentRequest request) =>
            PostAsync<MakeBindingPaymentRequest, MakeBindingPaymentResponse>("MakeBindingPayment", request);

        public Task<DeactivateBindingResponse> DeactivateBindingAsync(DeactivateBindingRequest request) =>
            PostAsync<DeactivateBindingRequest, DeactivateBindingResponse>("DeactivateBinding", request);

        #endregion
    }
}
