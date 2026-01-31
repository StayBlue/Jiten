using System.Security.Claims;
using System.Text.Encodings.Web;
using Jiten.Api.Services;
using Jiten.Core;
using Jiten.Core.Data.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Jiten.Api.Authentication;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UserDbContext userDb,
    UserManager<User> userManager,
    ApiKeyService apiKeyService)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    // Track invalid authentication attempts per client IP to prevent brute-forcing of API keys.
    private static readonly ConcurrentDictionary<string, FixedWindowRateLimiter> FailedAttemptLimiters = new();

    // Max invalid attempts per IP in the configured window before rejecting with 429.
    private const int FAILED_ATTEMPT_LIMIT = 5;
    private static readonly TimeSpan FAILED_WINDOW = TimeSpan.FromMinutes(1);

    // Optional small delay added on failed authentication to slow down brute-force attempts.
    private const int FAILED_DELAY_MIN_MS = 150;
    private const int FAILED_DELAY_MAX_MS = 350;


    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var hash = apiKeyService.ComputeHash(apiKey);

            var apiKeyWithUser = await userDb.ApiKeys
                                             .Include(k => k.User)
                                             .FirstOrDefaultAsync(k => k.Hash == hash &&
                                                                       !k.IsRevoked &&
                                                                       (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));

            if (apiKeyWithUser?.User == null)
            {
                // Log failed attempt and enforce brute-force protection
                var ip = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                Logger.LogWarning("Invalid API key attempt from {IP}", ip);

                var limiter = FailedAttemptLimiters.GetOrAdd(ip, _ =>
                                                                 new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                                                                                            {
                                                                                                PermitLimit = FAILED_ATTEMPT_LIMIT,
                                                                                                Window = FAILED_WINDOW,
                                                                                                QueueProcessingOrder =
                                                                                                    QueueProcessingOrder.OldestFirst,
                                                                                                QueueLimit = 0, AutoReplenishment = true
                                                                                            })
                                                            );

                var lease = limiter.AttemptAcquire(1);
                if (lease.IsAcquired == false)
                {
                    Context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    Context.Response.Headers["Retry-After"] = ((int)FAILED_WINDOW.TotalSeconds).ToString();
                    return AuthenticateResult.Fail("Too many invalid authentication attempts");
                }

                // Small random delay to reduce brute-force speed even under the limit
                try
                {
                    await Task.Delay(Random.Shared.Next(FAILED_DELAY_MIN_MS, FAILED_DELAY_MAX_MS));
                }
                catch
                {
                }

                return AuthenticateResult.Fail("Invalid API key");
            }

            var user = apiKeyWithUser.User;

            // Check if user is active/not locked
            if (await userManager.IsLockedOutAsync(user))
            {
                return AuthenticateResult.Fail("User account is locked");
            }

            // Update last used timestamp asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = Context.RequestServices.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
                    var entry = await dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyWithUser.Id);
                    if (entry != null)
                    {
                        entry.LastUsedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to update API key last used timestamp");
                }
            });

            var claims = await BuildClaims(user, apiKeyWithUser);
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "API key authentication error");
            return AuthenticateResult.Fail("Authentication error");
        }
    }

    private string? ExtractApiKey()
    {
        // Check custom header first
        if (Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            var apiKey = headerValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey;
        }

        // Check Authorization header
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                // Support multiple formats: "ApiKey xxx", "Bearer xxx", "xxx"
                if (value.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring("ApiKey ".Length).Trim();
                }

                if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring("Bearer ".Length).Trim();
                }

                if (!value.Contains(' '))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private async Task<List<Claim>> BuildClaims(User user, ApiKey apiKey)
    {
        var claims = new List<Claim>
                     {
                         new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName ?? string.Empty),
                         new(ClaimTypes.Email, user.Email ?? string.Empty), new("amr", "api_key"), new("auth_scheme", "ApiKey"),
                         new("api_key_id", apiKey.Id.ToString()),
                         new("rate_limit_tier", user.RateLimitTier.ToString())
                     };

        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return claims;
    }
}