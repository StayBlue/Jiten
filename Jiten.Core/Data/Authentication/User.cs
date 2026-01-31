using Microsoft.AspNetCore.Identity;

namespace Jiten.Core.Data.Authentication;

public class User : IdentityUser
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime TosAcceptedAt { get; set; }
    public bool ReceivesNewsletter { get; set; }
    public RateLimitTier RateLimitTier { get; set; } = RateLimitTier.Default;
}