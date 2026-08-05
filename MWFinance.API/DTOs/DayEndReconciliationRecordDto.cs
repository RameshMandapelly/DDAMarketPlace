using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Represents a single payment record in the day-end reconciliation report.
    /// Fields are conditionally present depending on payment status:
    /// ACCP → id, customerDdsRefNo, paymentRefNo, status, amount, reasonCode, remarks, ftRefNo
    /// RJCT → id, customerDdsRefNo, paymentRefNo, status, amount, reasonCode, remarks
    /// APRP → id, customerDdsRefNo, paymentRefNo, status, amount, remarks
    /// </summary>
    public class DayEndReconciliationRecordDto
    {
        /// <summary>
        /// Unique internal payment record ID assigned by UAEDDS gateway.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// The customer DDA reference number tied to this payment.
        /// </summary>
        [JsonPropertyName("customerDdsRefNo")]
        public string CustomerDdsRefNo { get; set; } = string.Empty;

        /// <summary>
        /// The unique payment reference number for this record.
        /// </summary>
        [JsonPropertyName("paymentRefNo")]
        public string PaymentRefNo { get; set; } = string.Empty;

        /// <summary>
        /// Payment status: ACCP | RJCT | APRP
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Payment amount for this record.
        /// </summary>
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Reason code (present on ACCP and RJCT statuses, absent on APRP).
        /// </summary>
        [JsonPropertyName("reasonCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasonCode { get; set; }

        /// <summary>
        /// Remarks field (present on all statuses).
        /// </summary>
        [JsonPropertyName("remarks")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Financial transaction reference number (present on ACCP status only).
        /// </summary>
        [JsonPropertyName("ftRefNo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FtRefNo { get; set; }
    }
}
