
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MWFinance.API.DTOs;
using MWFinance.API.Helpers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MWFinance.API.Controllers
{
    /// <summary>
    /// Handles bulk direct debit payment file uploads to UAEDDS clearinghouse.
    /// </summary>
    [Authorize]
    [Route("api/v1/merchant/payment-files")]
    [ApiController]
    public class PaymentFilesController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DdaGatewaySettingsHelper _gateway;
        private readonly ILogger<PaymentFilesController> _logger;
        public PaymentFilesController(IHttpClientFactory httpClientFactory,IOptions<DdaGatewaySettingsHelper> options,ILogger<PaymentFilesController> logger )
        {
            _httpClientFactory = httpClientFactory; 
            _gateway = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// Upload bulk direct debit payment requests in CSV format.
        /// POST /api/v1/merchant/payment-files
        /// </summary>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> UploadPaymentFile([FromForm] UploadSignedPdfRequest request)
        {
            // ── 1. Local Structural Validation ───────────────────────────────────
            _logger.LogInformation("UploadPaymentFile REQUEST received: FileName={FileName}, FileSize={FileSize}",
                           request.File?.FileName, request.File?.Length);
            if (request.File == null || request.File.Length == 0)
            {
                _logger.LogWarning("UploadPaymentFile REJECTED 400: file missing or empty");

                return BadRequest(new
                {
                    errors = new
                    {
                        file = new[] { "Payments file in CSV format is mandatory and cannot be empty." }
                    }
                });
            }

            // Validate CSV content type (browsers may send text/csv or application/vnd.ms-excel)
            var allowedContentTypes = new[]
            {
                "text/csv",
                "application/csv",
                "application/vnd.ms-excel",
                "text/plain"
            };

            bool isValidContentType = allowedContentTypes.Any(ct =>
                request.File.ContentType.Equals(ct, StringComparison.OrdinalIgnoreCase));

            // Also validate file extension as a secondary safety check
            bool isValidExtension = Path.GetExtension(request.File.FileName)
                .Equals(".csv", StringComparison.OrdinalIgnoreCase);

            if (!isValidContentType && !isValidExtension)
            {
                _logger.LogWarning("UploadPaymentFile REJECTED 400: Invalid file format. Only CSV files are accepted by the UAEDDS gateway");

                return BadRequest(new
                {
                    errors = new
                    {
                        file = new[] { "Invalid file format. Only CSV files are accepted by the UAEDDS gateway." }
                    }
                });
            }

            // ── 2. Build & Send Request to UAEDDS Gateway ────────────────────────

            var client = _httpClientFactory.CreateClient();
            string targetUrl = $"{_gateway.BaseUrl}/v1/merchant/payment-files"; 
                 
                 

            try
            {
                // Build multipart payload
                using var multipartContent = new MultipartFormDataContent();
                using var fileStream = request.File.OpenReadStream();
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

                // Field name "file" must match exactly what UAEDDS gateway expects
                multipartContent.Add(fileContent, "file", request.File.FileName);

                var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                {
                    Content = multipartContent
                };

         
                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // ── 3. Execute Gateway Call ───────────────────────────────────────

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string rawResponseBody = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("UploadPaymentFile ← gateway responded {StatusCode} for FileName={FileName}",
                                   (int)gatewayResponse.StatusCode, request.File.FileName);
                // ── 4. Handle Error Responses from Gateway ────────────────────────

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("UploadPaymentFile REJECTED {StatusCode} for FileName={FileName}: {Body}",
                        (int)gatewayResponse.StatusCode, request.File.FileName, rawResponseBody);

                    // Try to parse the gateway error response as JSON
                    try
                    {
                        var parsedError = JsonSerializer.Deserialize<JsonElement>(rawResponseBody);

                        // Case 1: Invalid file name → gateway returns { "message": "...", "status": ... }
                        if (parsedError.TryGetProperty("message", out _) &&
                            parsedError.TryGetProperty("status", out _))
                        {
                            return StatusCode((int)gatewayResponse.StatusCode, parsedError);
                        }

                        // Case 2 & 3: Record-level errors → gateway returns { "records": [...] }
                        if (parsedError.TryGetProperty("records", out _))
                        {
                            return StatusCode((int)gatewayResponse.StatusCode, parsedError);
                        }

                        // Fallback: unknown error shape — pass through raw
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = "UAEDDS gateway returned an error.",
                            details = rawResponseBody
                        });
                    }
                    catch (JsonException)
                    {
                        // Gateway returned non-JSON error body
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = "UAEDDS gateway returned an unrecognised error response.",
                            details = rawResponseBody
                        });
                    }
                }

                // ── 5. Handle Success Response (HTTP 201) ─────────────────────────
                _logger.LogInformation("UploadPaymentFile RESPONSE 201: FileName={FileName} uploaded successfully", request.File.FileName);

                try
                {
                    var successPayload = JsonSerializer.Deserialize<JsonElement>(rawResponseBody);
                    return StatusCode(StatusCodes.Status201Created, successPayload);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "UploadPaymentFile FAILED to reach gateway for FileName={FileName}", request.File?.FileName);

                    // Gateway returned 201 but non-JSON body (unlikely but safe fallback)
                    return StatusCode(StatusCodes.Status201Created, new
                    {
                        message = "Payment file uploaded successfully.",
                        raw = rawResponseBody
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "UploadPaymentFile FAILED to reach gateway for :{ex.Message}", ex.Message);

                // Network-level failure (firewall, DNS, timeout)
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "Failed to communicate with the UAEDDS clearinghouse gateway.",
                    details = ex.Message
                });
            }
        }

        /// <summary>
        /// Retrieve the status report of an uploaded bulk payment requests batch file.
        /// GET /api/v1/merchant/payment-files/{paymentFileID}/status
        /// </summary>
        /// <param name="paymentFileID">
        /// Numeric ID [N 11] of the uploaded bulk payment requests batch file. Example: 1280112
        /// </param>
        [HttpGet("{paymentFileID}/status")]
        [ProducesResponseType(typeof(PaymentFileStatusResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetBulkPaymentRequestsStatusReport([FromRoute] string paymentFileID) 
        {
            // ── 1. Local Route Parameter Validation ──────────────────────────────
            _logger.LogInformation("GetBulkPaymentRequestsStatusReport REQUEST received: PaymentFileID={PaymentFileID}", paymentFileID);

            if (string.IsNullOrWhiteSpace(paymentFileID))
            {
                return BadRequest(new
                {
                    errors = new { paymentFileID = new[] { "paymentFileID is mandatory." } }
                });
            }

            // Enforce [N 11]: must be numeric only, max 11 digits
            if (!paymentFileID.All(char.IsDigit))
            {
                _logger.LogWarning("GetBulkPaymentRequestsStatusReport REJECTED 400: PaymentFileID={PaymentFileID}  must contain numeric digits only", paymentFileID);

                return BadRequest(new
                {
                    errors = new { paymentFileID = new[] { "paymentFileID must contain numeric digits only." } }
                });
            }

            if (paymentFileID.Length > 11)
            {
                _logger.LogWarning("GetBulkPaymentRequestsStatusReport REJECTED 400: PaymentFileID={PaymentFileID} exceeds 11 digits", paymentFileID);

                return BadRequest(new
                {
                    errors = new { paymentFileID = new[] { "paymentFileID cannot exceed 11 digits." } }
                });
            }

            // ── 2. Build & Send Request to UAEDDS Gateway ────────────────────────

            var client = _httpClientFactory.CreateClient();
            string targetUrl = $"{_gateway.BaseUrl}/v1/merchant/payment-files/{paymentFileID}/status";
            _logger.LogInformation("GetBulkPaymentRequestsStatusReport ← gateway URL  {targetUrl}",
                               targetUrl);

            try
            {
                var gatewayRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);

           
                gatewayRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", _gateway.GetBasicAuthToken());

                // ── 3. Execute Gateway Call ───────────────────────────────────────

                var gatewayResponse = await client.SendAsync(gatewayRequest);
                string rawResponseBody = await gatewayResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("GetBulkPaymentRequestsStatusReport ← gateway responded {StatusCode} for PaymentFileID={PaymentFileID}",
                                   (int)gatewayResponse.StatusCode, paymentFileID);

                // ── 4. Handle Error Responses ─────────────────────────────────────

                if (!gatewayResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetBulkPaymentRequestsStatusReport REJECTED {StatusCode} for PaymentFileID={PaymentFileID}: {Body}",
                        (int)gatewayResponse.StatusCode, paymentFileID, rawResponseBody);

                    try
                    {
                        var parsedError = JsonSerializer.Deserialize<JsonElement>(rawResponseBody);
                        return StatusCode((int)gatewayResponse.StatusCode, parsedError);
                    }
                    catch (JsonException)
                    {
                        return StatusCode((int)gatewayResponse.StatusCode, new
                        {
                            message = "UAEDDS gateway returned an unrecognised error response.",
                            details = rawResponseBody
                        });
                    }
                }

                // ── 5. Deserialize & Map Gateway Response → PaymentFileStatusResponseDto ──

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true  // handles any casing from gateway
                    };

                    var mappedResponse = JsonSerializer.Deserialize<PaymentFileStatusResponseDto>(
                        rawResponseBody, options);

                    if (mappedResponse == null)
                    {
                        return StatusCode(StatusCodes.Status502BadGateway, new
                        {
                            message = "Gateway returned an empty or unreadable response body.",
                            raw = rawResponseBody
                        });
                    }

                    // ── 6. Return clean typed DTO response ────────────────────────────
                    _logger.LogInformation("GetBulkPaymentRequestsStatusReport RESPONSE 200: PaymentFileID={PaymentFileID}", paymentFileID);

                    return Ok(mappedResponse);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "GetBulkPaymentRequestsStatusReport FAILED to map gateway response for PaymentFileID={PaymentFileID}", paymentFileID);
                    // Gateway returned 200 but body didn't match expected schema
                    return StatusCode(StatusCodes.Status502BadGateway, new
                    {
                        message = "Gateway response could not be mapped to the expected payment file status schema.",
                        details = ex.Message,
                        raw = rawResponseBody
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GetBulkPaymentRequestsStatusReport FAILED to map gateway response Error :{ex.Message}", ex.Message);


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


    

