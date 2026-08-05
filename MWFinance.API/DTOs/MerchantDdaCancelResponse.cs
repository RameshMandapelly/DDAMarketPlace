using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Unified outbound object payload sent back down to your Postman/Client consumers
    /// </summary>
    public class MerchantDdaCancelResponse
    {
        // --- Success Properties ([N 11] String IDs matching your exact spelling) ---
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ddcarid { get; set; }

        public string message { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ddcaid { get; set; }

        // --- Error Property ---
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? status { get; set; }
    }
}
