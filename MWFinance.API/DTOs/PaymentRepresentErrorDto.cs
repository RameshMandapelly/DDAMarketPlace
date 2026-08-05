using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Error response DTO — returned when gateway rejects the represent request.
    /// Contains both message and status fields.
    /// </summary>
    public class PaymentRepresentErrorDto
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public int Status
        {
            get; set;
        }
    }
}
