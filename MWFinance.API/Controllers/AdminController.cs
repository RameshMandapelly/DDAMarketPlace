using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MWFinance.Infrastructure.Data;


namespace MWFinance.API.Controllers
{
      /// <summary>  
     /// Admin tool to register Fintech API clients.
    /// Use this to create credentials for each Fintech company you onboard.  
   /// </summary>
    [Route("api/admin")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AdminController> _logger;
        public AdminController(ApplicationDbContext context, IWebHostEnvironment env, ILogger<AdminController> logger)
        {
            _context = context;
            _env = env;
            _logger =logger;
        }

        // ── Request DTO ───────────────────────────────────────────────────────
        public class RegisterClientRequest
        {
            /// <summary>
            /// Unique identifier for this Fintech company.
            /// Example: "fintech-xyz-001"
            /// You share this with the Fintech along with their secret.
            /// </summary>
            public string ClientId { get; set; } = string.Empty;

            /// <summary>
            /// Plain text secret — stored as BCrypt hash in DB.
            /// Share this with the Fintech via secure channel (email/WhatsApp).
            /// YOU CANNOT RETRIEVE THIS LATER — note it down before calling this.
            /// </summary>
            public string ClientSecret { get; set; } = string.Empty;

            /// <summary>
            /// Human-readable company name for your records.
            /// Example: "XYZ Fintech LLC"
            /// </summary>
            public string CompanyName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Register a new Fintech API client.
        /// Call this ONCE per Fintech company you want to give access to.
        /// The clientSecret is hashed immediately — you cannot retrieve it later.
        /// </summary>
        /// <remarks>
        /// Example:
        ///
        ///     POST /api/admin/register-client
        ///     {
        ///         "clientId": "fintech-xyz-001",
        ///         "clientSecret": "StrongPassword123!",
        ///         "companyName": "XYZ Fintech LLC"
        ///     }
        ///
        /// After calling this, give the Fintech:
        ///   - clientId: "fintech-xyz-001"
        ///   - clientSecret: "StrongPassword123!"
        ///   - Your API base URL
        /// They use those to call POST /api/auth/token to get a JWT.
        /// </remarks>
        [HttpPost("register-client")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RegisterClient([FromBody] RegisterClientRequest request)
        {

            _logger.LogInformation(
                "RegisterClient REQUEST received: ClientId={ClientId}, CompanyName={CompanyName}",
                request.ClientId, request.CompanyName);
            // ── 1. Input Validation ───────────────────────────────────────────
         try{

            if (string.IsNullOrWhiteSpace(request.ClientId) ||
                string.IsNullOrWhiteSpace(request.ClientSecret) ||
                string.IsNullOrWhiteSpace(request.CompanyName))
            {
                _logger.LogWarning("RegisterClient REJECTED 400: missing required field(s)");
                return BadRequest(new
                {
                    message = "clientId, clientSecret, and companyName are all required.",
                    status = 400
                });
            }

            // ── 2. Check for Duplicate ClientId ──────────────────────────────
            // ⚠️ IMPORTANT: Replace "FintechClienstApi" below with your actual
            // DbSet name from ApplicationDbContext.cs
            // Examples:
            //   _context.ApiClients          ← if your DbSet is named ApiClients
            //   _context.FintechClienstApi   ← if your DbSet is named FintechClienstApi

            bool alreadyExists = _context.FintechClienstApi
                .Any(c => c.ClientId == request.ClientId.Trim());

            if (alreadyExists)
            {
                _logger.LogWarning("RegisterClient REJECTED 400: ClientId={ClientId} already exists",request.ClientId);
                return BadRequest(new
                {
                    message = $"ClientId '{request.ClientId}' is already registered. Use a different clientId.",
                    status = 400
                });
            }

            // ── 3. Hash the Secret with BCrypt ────────────────────────────────
            // Work factor 12 = strong enough, takes ~250ms (intentionally slow)
            // The plain text secret is NEVER stored — only this hash
            string hashedSecret = BCrypt.Net.BCrypt.HashPassword(
                request.ClientSecret.Trim(),
                workFactor: 12
            );

            // ── 4. Create the Entity ──────────────────────────────────────────
            // ⚠️ IMPORTANT: Replace "ApiClient" below with your actual entity
            // class name from MWFinance.Domain/Entities/
            // Check what class name you used when you created the entity file.

            var newClient = new MWFinance.Domain.Entities.FintechClientApi
            {
                ClientId = request.ClientId.Trim(),
                ClientSecretHash = hashedSecret,
                CompanyName = request.CompanyName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // ── 5. Save to Database ───────────────────────────────────────────
            _context.FintechClienstApi.Add(newClient);  // ← same DbSet name as above
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                               "RegisterClient RESPONSE 200: ClientId={ClientId}, CompanyName={CompanyName}, CreatedAt={CreatedAt}",
                               newClient.ClientId, newClient.CompanyName, newClient.CreatedAt);

            // ── 6. Return Success ─────────────────────────────────────────────
            // NOTE: We do NOT return the secret in the response — it's hashed
            // and gone. You must share it with the Fintech separately.
            return Ok(new
            {
                message = "Fintech client registered successfully.",
                clientId = newClient.ClientId,
                companyName = newClient.CompanyName,
                isActive = newClient.IsActive,
                createdAt = newClient.CreatedAt,
                important = "Share the original clientSecret with the Fintech securely. It cannot be retrieved from the database."
            });
         }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RegisterClient FAILED unexpectedly for ClientId={ClientId}",
                    request.ClientId);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "An unexpected error occurred while registering the client.",
                    status = 500
                });
            }
        }

        /// <summary>
        /// Deactivate a Fintech client — blocks their login without deleting their record.
        /// POST /api/admin/deactivate-client/{clientId}
        /// </summary>
        [HttpPost("deactivate-client/{clientId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeactivateClient([FromRoute] string clientId)
        {
            _logger.LogInformation("DeactivateClient REQUEST received: ClientId={ClientId}", clientId);
         try
         {
            // ⚠️ Replace "FintechClienstApi" with your actual DbSet name
            var client = _context.FintechClienstApi
                .FirstOrDefault(c => c.ClientId == clientId);

            if (client == null)
            {
                _logger.LogWarning("DeactivateClient REJECTED 404: ClientId={ClientId} not found", clientId);

                return NotFound(new
                {
                    message = $"No client found with clientId: {clientId}",
                    status = 404
                });
            }

            client.IsActive = false;
            await _context.SaveChangesAsync();
            _logger.LogInformation("DeactivateClient RESPONSE 200: ClientId={ClientId} deactivated", clientId);

            return Ok(new
            {
                message = $"Client '{clientId}' has been deactivated. They can no longer login.",
                clientId = clientId
            });
        }
        
        catch (Exception ex)
            {
                _logger.LogError(ex, "DeactivateClient FAILED unexpectedly for ClientId={ClientId}", clientId);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
            message = "An unexpected error occurred while deactivating the client.",
                    status = 500
                });
            }
        }
    }
}
