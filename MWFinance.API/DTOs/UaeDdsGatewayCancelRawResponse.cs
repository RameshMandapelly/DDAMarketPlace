using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Internal mirror class to securely map the incoming response directly from UAEDDS Gateway
    /// </summary>
    public class UaeDdsGatewayCancelRawResponse
    {
        [JsonPropertyName("ddcarId")]
        public string? ddcarId { get; set; } // Keeps [N 11] value safe without boxing

        [JsonPropertyName("ddaId")]
        public string? ddaId { get; set; }   // Keeps [N 11] value safe without boxing

        [JsonPropertyName("message")]
        public string? message { get; set; } // Strongly typed string to capture error descriptions smoothly

        [JsonPropertyName("status")]
        public int? status { get; set; }     // Strongly typed HTTP integer status code
    }
}
