using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Full response envelope for the payment file status report.
    /// </summary>
    public class PaymentFileStatusResponseDto
    {

        /// <summary>
        /// The payment file batch ID this status report belongs to.
        /// </summary>
        [JsonPropertyName("paymentFileID")]
        public string PaymentFileId { get; set; } = string.Empty;

        /// <summary>
        /// All payment records contained in this batch with their current statuses.
        /// </summary>
        [JsonPropertyName("records")]
        public List<PaymentFileRecordDto> Records { get; set; } = new();
    }
}
