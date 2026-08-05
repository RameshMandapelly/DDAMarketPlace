using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MWFinance.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MWFinance.API.Controllers
{
    /// <summary>
    /// Authentication endpoint.
    /// Fintech companies call POST /api/auth/token to receive a JWT access token.
    /// All other endpoints require this token in the Authorization header.
    /// </summary>
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ApplicationDbContext context, IConfiguration configuration, ILogger<AuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;

        }

        // ─────────────────────────────────────────────────────────────────────
        // REQUEST / RESPONSE DTOs (inline — small enough to keep here)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// What Fintech sends to login.
        /// </summary>
        public class TokenRequest
        {
            /// <summary>
            /// The client identifier you gave them during onboarding.
            /// Example: "fintech-xyz-001"
            /// </summary>
            public string ClientId { get; set; } = string.Empty;

            /// <summary>
            /// The secret you gave them during onboarding (plain text here, 
            /// we verify it against the BCrypt hash stored in DB).
            /// </summary>
            public string ClientSecret { get; set; } = string.Empty;
        }

        /// <summary>
        /// What you return on successful login.
        /// </summary>
        public class TokenResponse
        {
            /// <summary>
            /// The JWT Bearer token. Fintech must send this in every request:
            /// Authorization: Bearer {access_token}
            /// </summary>
            public string AccessToken { get; set; } = string.Empty;

            /// <summary>
            /// Token type — always "Bearer" (industry standard).
            /// </summary>
            public string TokenType { get; set; } = "Bearer";

            /// <summary>
            /// Seconds until this token expires. After this, Fintech must login again.
            /// </summary>
            public int ExpiresIn { get; set; }

            /// <summary>
            /// UTC timestamp when the token expires (convenient for Fintech's scheduler).
            /// </summary>
            public DateTime ExpiresAt { get; set; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /api/auth/token
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fintech login — exchange clientId + clientSecret for a JWT access token.
        /// This endpoint does NOT require an existing token (it's how you get one).
        /// </summary>
        /// <remarks>
        /// Example request:
        ///
        ///     POST /api/auth/token
        ///     {
        ///         "clientId": "fintech-xyz-001",
        ///         "clientSecret": "their-secret-password"
        ///     }
        ///
        /// Store the returned access_token and send it in all future requests:
        ///     Authorization: Bearer eyJhbGci...
        /// </remarks>
        [HttpPost("token")]
        [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetToken([FromBody] TokenRequest request)
        {
            // ── 1. Basic Input Validation ─────────────────────────────────────
            _logger.LogInformation("GetToken REQUEST received: ClientId={ClientId}", request.ClientId);
        try 
        {
            if (string.IsNullOrWhiteSpace(request.ClientId) ||
                string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                return BadRequest(new
                {
                    message = "Both clientId and clientSecret are required.",
                    status = 400
                });
            }

            // ── 2. Look Up Client in Database ─────────────────────────────────
            // We find by ClientId first (public identifier).
            // Never hint to the caller whether the clientId or secret was wrong
            // — always return the same generic 401 message (security best practice).

            var client = await _context.FintechClienstApi
                .FirstOrDefaultAsync(c => c.ClientId == request.ClientId);

            // ── 3. Verify Client Exists, Is Active, and Secret Matches ────────

            bool isValid = client != null
                && client.IsActive
                && BCrypt.Net.BCrypt.Verify(request.ClientSecret, client.ClientSecretHash);

            if (!isValid)
            {
                // Generic message — do NOT say "clientId not found" or "wrong password"
                // This prevents attackers from probing which clientIds exist.
                return Unauthorized(new
                {
                    message = "Invalid credentials.",
                    status = 401
                });
            }

            // ── 4. Build JWT Token ────────────────────────────────────────────

            var jwtKey = _configuration["Jwt:Key"]!;
            var issuer = _configuration["Jwt:Issuer"]!;
            var audience = _configuration["Jwt:Audience"]!;
            var expiryMins = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMins);

            // Claims are data embedded inside the token.
            // Your controllers can read these later to know WHO is calling.
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, client!.ClientId),
                new Claim("companyName", client.CompanyName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // unique token ID
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            _logger.LogInformation(
                   "GetToken RESPONSE 200: ClientId={ClientId}, CompanyName={CompanyName}, ExpiresAt={ExpiresAt}",
                   client.ClientId, client.CompanyName, expiresAt);

            // ── 5. Return Token to Fintech ────────────────────────────────────

            return Ok(new TokenResponse
            {
                AccessToken = tokenString,
                TokenType = "Bearer",
                ExpiresIn = expiryMins * 60, // in seconds (OAuth2 standard)
                ExpiresAt = expiresAt
            });
        }        
        catch (Exception ex)
            {
                _logger.LogError(ex, "GetToken FAILED unexpectedly for ClientId={ClientId}", request.ClientId);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
            message = "An unexpected error occurred while processing the login request.",
                    status = 500
                });
            }      
        }  
    }
}

