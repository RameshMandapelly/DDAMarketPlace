namespace MWFinance.API.DTOs
{
    /// <summary>
    /// Outbound JSON container used exclusively during document download validation failures
    /// </summary>
    public class MerchantDdaDownloadErrorResponse
    {
        public string message { get; set; } = string.Empty;
        public int status { get; set; }
    }
}
