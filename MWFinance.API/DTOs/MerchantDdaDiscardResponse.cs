using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Final output sent back to your corporate clients / Postman
    /// </summary>
    public class MerchantDdaDiscardResponse
    {
        public string message { get; set; } = string.Empty;

        // Omitted entirely on success. Visible only during an Error state [JsonIgnore]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? status { get; set; }
    }
}
