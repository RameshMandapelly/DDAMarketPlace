using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MWFinance.API.DTOs;
using MWFinance.API.Helpers;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MWFinance.API.Controllers
{
    /// <summary>
    /// Handles bulk direct debit payment file uploads to UAEDDS clearinghouse.
    /// </summary>
    [Authorize]
    [Route("api/v1/merchant/payments")]
    [ApiController]
    public class PaymentsController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DdaGatewaySettingsHelper _gateway;
        private readonly ILogger<PaymentsController> _logger;
        public PaymentsController(IHttpClientFactory httpClientFactory,IOptions<DdaGatewaySettingsHelper> options, ILogger<PaymentsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _gateway = options.Value;
            _logger = logger;
        }


        /// <summary>
        /// Resubmit a previously rejected payment (RJCT status only) for collection.
        /// Maximum 3 representment attempts allowed per payment.
        /// POST /api/v1/merchant/payments/{paymentId}/represent
        /// </summary>
        /// <param name="paymentId">
        /// The unique ID of the payment to resubmit. Must be numeric.
        /// Example: 21251
        /// </param>
        [HttpPost("{paymentId}/represent")]
        [ProducesResponseType(typeof(PaymentRepresentSuccessDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> RepresentPayment([FromRoute] string paymentId)
        {
            // ── 1. Local Route Parameter Validation ──────────────────────────────
            _logger.LogInformation("RepresentPayment REQUEST received: PaymentId={PaymentId}", paymentId);

            if (string.IsNullOrWhiteSpace(paymentId))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "paymentId is mandatory.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (!paymentId.All(char.IsDigit))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "paymentId must contain numeric digits only.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // ── 2. Build & Send Request to UAEDDS Gateway ────────────────────────

            var client = _httpClientFactory.CreateClient();
            string targetUrl = $"{_gateway.BaseUrl}/v1/merchant/payments/{paymentId}/represent";
            _logger.LogInformation("RepresentPayment ← gateway URL {targetUrl}",targetUrl);

            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl);

               
                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // ── 3. Execute Gateway Call ───────────────────────────────────────

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string rawResponseBody = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("RepresentPayment ← gateway responded {StatusCode} for PaymentId={PaymentId}",
                                    (int)gatewayResponse.StatusCode, paymentId);

                // ── 4. Handle Error Responses from Gateway ────────────────────────

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("RepresentPayment REJECTED {StatusCode} for PaymentId={PaymentId}: {Body}",
                        (int)gatewayResponse.StatusCode, paymentId, rawResponseBody);

                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var errorPayload = JsonSerializer.Deserialize<PaymentRepresentErrorDto>(
                            rawResponseBody, options);

                        if (errorPayload != null)
                        {
                            // Case 1: Payment not found or not in RJCT status → 404
                            // Case 2: Max 3 representments exceeded          → 400
                            // Both share same DTO shape { message, status }
                            // We trust the gateway status code directly
                            return StatusCode((int)gatewayResponse.StatusCode, errorPayload);
                        }

                        // Fallback: unrecognised error shape
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = "UAEDDS gateway returned an unrecognised error response.",
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                    catch (JsonException)
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = rawResponseBody,
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // ── 5. Handle Success Response (HTTP 200) ─────────────────────────
                // Success shape: { "message": "Updated Successfully" }
                // Status field is intentionally NOT included in success response

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var successPayload = JsonSerializer.Deserialize<PaymentRepresentSuccessDto>(
                        rawResponseBody, options);

                    if (successPayload != null)
                    {
                        _logger.LogInformation("RepresentPayment RESPONSE 200: PaymentId={PaymentId}", paymentId);

                        return Ok(successPayload);
                    }

                    // Fallback: gateway returned 200 but body unreadable
                    _logger.LogInformation("RepresentPayment RESPONSE 200 (fallback): PaymentId={PaymentId}", paymentId);
                    return Ok(new PaymentRepresentSuccessDto
                    {
                        Message = "Updated Successfully"
                    });
                }
                catch (JsonException)
                {
                    _logger.LogInformation("RepresentPayment RESPONSE 200 (unparsed body): PaymentId={PaymentId}", paymentId);

                    return Ok(new PaymentRepresentSuccessDto
                    {
                        Message = "Updated Successfully"
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "RepresentPayment FAILED to reach gateway for PaymentId={PaymentId}", paymentId);

                // Network-level failure (firewall, DNS, timeout)
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the UAEDDS clearinghouse gateway.",
                    details = ex.Message
                });
            }
        }


        /// <summary>
        /// Retrieve the Central BANK approved Direct Debit Memo document for a failed payment.
        /// GET /api/v1/merchant/payments/{paymentId}/bounce-memo
        /// </summary>
        /// <param name="paymentId">
        /// The unique ID of the failed payment. Must be numeric.
        /// Example: 21251
        /// </param>
        [HttpGet("{paymentId}/bounce-memo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetPaymentBounceMemo([FromRoute] string paymentId)
        {
            // ── 1. Local Route Parameter Validation ──────────────────────────────
            _logger.LogInformation("GetPaymentBounceMemo REQUEST received: PaymentId={PaymentId}", paymentId);

            if (string.IsNullOrWhiteSpace(paymentId))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "paymentId is mandatory.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (!paymentId.All(char.IsDigit))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "paymentId must contain numeric digits only.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // ── 2. Build & Send Request to UAEDDS Gateway ────────────────────────

            var client = _httpClientFactory.CreateClient();
            string targetUrl = $"{_gateway.BaseUrl}/v1/merchant/payments/{paymentId}/bounce-memo";
            _logger.LogInformation("GetPaymentBounceMemo ← gateway URL :{targetUrl}",targetUrl);

            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);
              
                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // ── 3. Execute Gateway Call — stream headers only first for efficiency ─

                var gatewayResponse = await client.SendAsync(
                    gatewayRequest, HttpCompletionOption.ResponseHeadersRead);

                _logger.LogInformation("GetPaymentBounceMemo ← gateway responded {StatusCode} for PaymentId={PaymentId}",
                    (int)gatewayResponse.StatusCode, paymentId);

                // ── 4. Handle Error Responses from Gateway ────────────────────────

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    
                    // Read body only on error
                    string rawErrorBody = await gatewayResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("GetPaymentBounceMemo REJECTED {StatusCode} for PaymentId={PaymentId}: {Body}",
                                            (int)gatewayResponse.StatusCode, paymentId, rawErrorBody);

                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var errorPayload = JsonSerializer.Deserialize<PaymentRepresentErrorDto>(
                            rawErrorBody, options);

                        if (errorPayload != null)
                            return StatusCode((int)gatewayResponse.StatusCode, errorPayload);

                        // Fallback: unrecognised error shape
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = "UAEDDS gateway returned an unrecognised error response.",
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                    catch (JsonException)
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = rawErrorBody,
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // ── 5. Stream PDF directly back to caller (HTTP 200) ─────────────
                // Gateway returns the Central Bank approved memo as a PDF binary stream.
                // We stream it directly without loading the entire file into memory.

                var pdfStream = await gatewayResponse.Content.ReadAsStreamAsync();
                _logger.LogInformation("GetPaymentBounceMemo RESPONSE 200: PDF stream returned for PaymentId={PaymentId}", paymentId);

                return File(
                    pdfStream,
                    "application/pdf",
                    $"BounceMemo_Payment_{paymentId}.pdf"
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GetPaymentBounceMemo FAILED to reach gateway for PaymentId={PaymentId}", paymentId);

                // Network-level failure (firewall, DNS, timeout)
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the UAEDDS clearinghouse gateway.",
                    details = ex.Message
                });
            }
        }

        /// <summary>
        /// Retrieve the day-end reconciliation report of payment requests for a specific date.
        /// GET /api/v1/merchant/payments/day-end?date={date}
        /// </summary>
        /// <param name="date">
        /// The date for which the day-end reconciliation report is required.
        /// Format: yyyy-MM-dd. Example: 2026-03-31
        /// </param>
        [HttpGet("day-end")]
        [ProducesResponseType(typeof(List<DayEndReconciliationRecordDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(PaymentRepresentErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetDayEnd([FromQuery] string date)
        {
            // ── 1. Local Query Parameter Validation ──────────────────────────────
            _logger.LogInformation("GetDayEnd REQUEST received: Date={Date}", date);

            if (string.IsNullOrWhiteSpace(date))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "date query parameter is mandatory.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Enforce strict yyyy-MM-dd format as per spec [yyyy-mm-dd]
            if (!DateTime.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out _))
            {
                return BadRequest(new PaymentRepresentErrorDto
                {
                    Message = "date must be a valid date in the format yyyy-MM-dd. Example: 2026-03-31",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // ── 2. Build & Send Request to UAEDDS Gateway ────────────────────────

            var client = _httpClientFactory.CreateClient();
            string targetUrl = $"{_gateway.BaseUrl}/v1/merchant/payments/day-end?date={date}";
            _logger.LogInformation("GetDayEnd ← gateway URL: {targetUrl}",targetUrl);

            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);

                // Inject Basic Authentication header
                
                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // ── 3. Execute Gateway Call ───────────────────────────────────────

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string rawResponseBody = await gatewayResponse.Content.ReadAsStringAsync();

                // ── 4. Handle Error Responses from Gateway ────────────────────────
                _logger.LogInformation("GetDayEnd ← gateway responded {StatusCode} for Date={Date}",
                                  (int)gatewayResponse.StatusCode, date);

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetDayEnd REJECTED {StatusCode} for Date={Date}: {Body}",
                        (int)gatewayResponse.StatusCode, date, rawResponseBody);

                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        // Error shape: { "message": "No payments found for date: 2026-03-30", "status": 404 }
                        var errorPayload = JsonSerializer.Deserialize<PaymentRepresentErrorDto>(
                            rawResponseBody, options);

                        if (errorPayload != null)
                            return StatusCode((int)gatewayResponse.StatusCode, errorPayload);

                        // Fallback: unrecognised error shape
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = "UAEDDS gateway returned an unrecognised error response.",
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                    catch (JsonException)
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new PaymentRepresentErrorDto
                        {
                            Message = rawResponseBody,
                            Status = (int)gatewayResponse.StatusCode
                        });
                    }
                }

                // ── 5. Deserialize & Map Gateway Response → List<DayEndReconciliationRecordDto> ──
                // Success shape: JSON array [ { id, customerDdsRefNo, paymentRefNo,
                //                               status, amount, reasonCode?, remarks?, ftRefNo? }, ... ]

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var records = JsonSerializer.Deserialize<List<DayEndReconciliationRecordDto>>(
                        rawResponseBody, options);

                    if (records == null || records.Count == 0)
                    {
                        _logger.LogInformation("GetDayEnd RESPONSE 404: no Payment found for Date={Date}", date);

                        return NotFound(new PaymentRepresentErrorDto
                        {
                            Message = $"No payments found for date: {date}",
                            Status = StatusCodes.Status404NotFound
                        });
                    }

                    // ── 6. Return clean typed DTO list ────────────────────────────
                    _logger.LogInformation("GetDayEnd RESPONSE 200: Date={Date}, RecordCount={RecordCount}", date, records.Count);
                    return Ok(records);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "GetDayEnd FAILED to map gateway response for Date={Date}", date);
                    return StatusCode(StatusCodes.Status502BadGateway, new
                    {
                        message = "Gateway response could not be mapped to the expected day-end reconciliation schema.",
                        details = ex.Message,
                        raw = rawResponseBody
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GetDayEnd FAILED to reach gateway for Date={Date}", date);

                // Network-level failure
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the UAEDDS clearinghouse gateway.",
                    details = ex.Message
                });
            }
        }
    }
}
