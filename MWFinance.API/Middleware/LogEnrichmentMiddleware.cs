using Serilog.Context;
using System.IdentityModel.Tokens.Jwt;

namespace MWFinance.API.Middleware
{
          /// <summary>
          /// Middleware that runs on every request and pushes clientId + companyName
          /// from the JWT token into Serilog's LogContext — so every log entry written
          /// during that request automatically includes those two values as enriched
          /// properties, which then get stored in the ClientId and CompanyName columns.
          /// </summary>
          public class LogEnrichmentMiddleware
          {
                    private readonly RequestDelegate _next;

                    public LogEnrichmentMiddleware(RequestDelegate next)
                    {
                              _next = next;
                    }

                    public async Task InvokeAsync(HttpContext context)
                    {
                              string clientId = "anonymous";
                              string companyName = "anonymous";

                              // Try to extract claims from the Authorization header
                              // Works for any request that carries a valid Bearer token
                              var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

                              if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                              {
                                        try
                                        {
                                                  string tokenString = authHeader.Substring("Bearer ".Length).Trim();
                                                  var handler = new JwtSecurityTokenHandler();

                                                  if (handler.CanReadToken(tokenString))
                                                  {
                                                            var jwtToken = handler.ReadJwtToken(tokenString);

                                                            // Extract the claims we embedded in AuthController.GetToken()
                                                            clientId = jwtToken.Claims
                                                                .FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                                                                ?? "anonymous";

                                                            companyName = jwtToken.Claims
                                                                .FirstOrDefault(c => c.Type == "companyName")?.Value
                                                                ?? "anonymous";
                                                  }
                                        }
                                        catch
                                        {
                                                  // If token is malformed or unreadable, fall back to "anonymous"
                                                  // Never let a logging enrichment failure break the actual request
                                        }
                              }

                              // Push into Serilog's LogContext for the duration of this request
                              // Every _logger.Log* call made anywhere in the pipeline during this
                              // request will automatically include these two properties
                              using (LogContext.PushProperty("ClientId", clientId))
                              using (LogContext.PushProperty("CompanyName", companyName))
                              {
                                        await _next(context);
                              }
                    }
          }
}