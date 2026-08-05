using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Represents single record rows or row validation failures
    /// </summary>
    public class BulkPaymentRecordDto
    {
        // --- Success Field Mapping ---
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? id { get; set; }

        public string customerDdsRefNo { get; set; } = string.Empty;
        public string paymentRefNo { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? status { get; set; }

        // --- Error Array Field Mapping (Omitted on success rows) ---
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? errors { get; set; }
    }
}
