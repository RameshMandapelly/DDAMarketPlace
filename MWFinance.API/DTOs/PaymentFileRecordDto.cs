using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Represents a single payment record inside the status report response.
    /// </summary>
    public class PaymentFileRecordDto
    {
        /// <summary>
        /// Unique internal record ID assigned by UAEDDS gateway.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// The customer DDA reference number tied to this payment record.
        /// </summary>
        [JsonPropertyName("customerDdsRefNo")]
        public string CustomerDdsRefNo { get; set; } = string.Empty;

        /// <summary>
        /// The unique payment reference number for this record.
        /// </summary>
        [JsonPropertyName("paymentRefNo")]
        public string PaymentRefNo { get; set; } = string.Empty;

        /// <summary>
        /// Payment status: PNDG | ACCP | RJCT | ERR
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Reason code returned by gateway (present on RJCT or ACCP statuses).
        /// </summary>
        [JsonPropertyName("reasonCode")]
        public string? ReasonCode { get; set; }

        /// <summary>
        /// Financial transaction reference number (present only on ACCP status).
        /// </summary>
        [JsonPropertyName("ftRefNo")]
        public string? FtRefNo { get; set; }

        /// <summary>
        /// List of error descriptions (present only on ERR status).
        /// </summary>
        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }
    }
}
