using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Jiten.Api.Dtos;
using Jiten.Core;
using Jiten.Core.Data.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Jiten.Api.Services;

public class TokenService
{
    private readonly IConfiguration _configuration;
    private readonly UserManager<User> _userManager;
    private readonly UserDbContext _context;

    public TokenService(IConfiguration configuration, UserManager<User> userManager, UserDbContext context)
    {
        _configuration = configuration;
        _userManager = userManager;
        _context = context;
    }

    public async Task<TokenResponse> GenerateTokens(User user)
    {
        var userRoles = await _userManager.GetRolesAsync(user);
        var jwtSettings = _configuration.GetSection("JwtSettings");

        var claims = new List<Claim>
                     {
                         new(ClaimTypes.NameIdentifier, user.Id), new(JwtRegisteredClaimNames.Sub, user.Id),
                         new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                         new(JwtRegisteredClaimNames.Email, user.Email!), new(ClaimTypes.Name, user.UserName!),
                         // Add amr (Authentication Method Reference) claim for 2FA
                         // "mfa" indicates multi-factor authentication was performed
                         // This is useful for clients or other services to know the strength of the authentication
                         new("amr", "pwd"),
                         new("rate_limit_tier", user.RateLimitTier.ToString())
                     };

        foreach (var role in userRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var accessTokenExpiration = DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["AccessTokenExpirationMinutes"]!));

        var tokenDescriptor = new SecurityTokenDescriptor
                              {
                                  Subject = new ClaimsIdentity(claims), Expires = accessTokenExpiration, Issuer = jwtSettings["Issuer"],
                                  Audience = jwtSettings["Audience"], SigningCredentials = creds
                              };

        var tokenHandler = new JwtSecurityTokenHandler();
        var accessToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        // Generate Refresh Token
        var refreshToken = GenerateRefreshToken();
        var refreshTokenEntity = new RefreshToken
                                 {
                                     Token = refreshToken, JwtId = claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value,
                                     UserId = user.Id, CreationDate = DateTime.UtcNow,
                                     ExpiryDate = DateTime.UtcNow.AddDays(double.Parse(jwtSettings["RefreshTokenExpirationDays"]!)),
                                 };

        await _context.RefreshTokens.AddAsync(refreshTokenEntity);
        // SaveChanges will be called by the controller action after this method completes successfully

        return new TokenResponse { AccessToken = accessToken, AccessTokenExpiration = accessTokenExpiration, RefreshToken = refreshToken };
    }

    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var tokenValidationParameters = new TokenValidationParameters
                                        {
                                            ValidateAudience =
                                                true, // You might want to set this to false if you are not using the same audience for refresh token validation
                                            ValidateIssuer = true, // Same as above
                                            ValidIssuer = jwtSettings["Issuer"], ValidAudience = jwtSettings["Audience"],
                                            ValidateIssuerSigningKey = true,
                                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!)),
                                            ValidateLifetime = false // We check an expired token here
                                        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
        var jwtSecurityToken = securityToken as JwtSecurityToken;
        if (jwtSecurityToken == null ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            return null; // Invalid token algorithm

        return principal;
    }
}