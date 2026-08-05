using System.Text.Json.Serialization;

namespace MWFinance.API.DTOs
{
    /// <summary>    
    /// 1. This is the RAW Model used ONLY to read responses coming FROM the UAEDDS Gateway.    
    /// </summary>
    public class UaeDdsGatewayRawResponse
    {
        public string? ddaId { get; set; }
        public string? ddaRefNo { get; set; } 
        public object? status { get; set; }
        public string? cbDdaRefNo { get; set; }
        public string? reasonCode { get; set; }
        public Dictionary<string, string[]>? errors { get; set; }
    }
}
