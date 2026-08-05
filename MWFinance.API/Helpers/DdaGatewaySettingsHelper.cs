namespace MWFinance.API.Helpers
{
    /// <summary>
    /// // This is a strongly-typed config class.
    // It maps directly to the "DdaGateway" section in appsettings.json:
    //
    // "DdaGateway": {
    //     "BaseUrl":  "https://test.directdebit.ae/api",
    //     "Username": "xxxx",
    //     "Password": "xxxx"
    // }
    /// </summary>
    public class DdaGatewaySettingsHelper
    {
        // Must match the section name in appsettings.json exactly
        public const string SectionName = "DdaGateway";

        /// <summary>
        /// Base URL of the DDA Marketplace API.
        /// Sandbox:    https://test.directdebit.ae/api
        /// Production: https://directdebit.ae/api
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Basic Auth username for DDA Marketplace.
        /// Comes from appsettings.json → never hardcoded in controllers.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Basic Auth password for DDA Marketplace.
        /// Comes from appsettings.json → never hardcoded in controllers.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Builds the Base64-encoded Basic Auth header value.
        /// Call this once per HTTP request — don't cache the result.
        /// Usage: gatewayRequest.Headers.Authorization =
        ///            new AuthenticationHeaderValue("Basic", _gatewaySettings.GetBasicAuthToken());
        /// </summary>
        public string GetBasicAuthToken()
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes($"{Username}:{Password}");
            return Convert.ToBase64String(bytes);
        }
    }
}
