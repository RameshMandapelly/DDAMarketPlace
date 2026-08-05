namespace MWFinance.API.DTOs
{

    /// <summary>
    /// Internal DTO representing the exact JSON object schema context provided in the UAEDDS specification sheets.
    /// </summary>
    public class UaeDdsStatusResponse
                 
    {
        public string ddaId { get; set; } = string.Empty;
        public string ddaReferenceNumber { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;

        // The exact new field [N 23] tracking variable mapped from your spec screenshots
        public string cbDdaRefNo { get; set; } = string.Empty;
    }
}
