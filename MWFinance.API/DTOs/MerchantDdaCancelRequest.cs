using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Captures the incoming request parameters from your front-end merchant
    /// </summary>
    public class MerchantDdaCancelRequest
    {
        [JsonPropertyName("reasonCode")]
        public string ReasonCode { get; set; } = string.Empty;

        [JsonPropertyName("originatorComments")]
        public string OriginatorComments { get; set; } = string.Empty;
    }
}
