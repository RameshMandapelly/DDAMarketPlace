using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Success response DTO — returned on HTTP 200.
    /// Only contains message field. Status field is intentionally excluded.
    /// </summary>
    public class PaymentRepresentSuccessDto
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
