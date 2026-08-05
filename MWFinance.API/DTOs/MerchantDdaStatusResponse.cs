using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// 2. This is the FINAL Model your API returns to your callers (Postman/Swagger).
    /// Properties dynamically disappear when null to match your exact format cases
    /// </summary>
    public class MerchantDdaStatusResponse
    {
        public int id { get; set; }
        public string status { get; set; } = string.Empty;
        public string ddaRefNo { get; set; } = string.Empty; 

        // Conditional Field: Populated only for RJCT status
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? reasonCode { get; set; }

        // Conditional Field: Populated only for ACCP status [N 23]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? cbDdaRefNo { get; set; }

        // Conditional Field: Populated only for ERR status
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string[]>? errors { get; set; }
    }
}
