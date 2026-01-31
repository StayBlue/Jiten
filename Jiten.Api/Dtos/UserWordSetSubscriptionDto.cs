using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class UserWordSetSubscriptionDto
{
    public int SetId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WordSetStateType State { get; set; }
    public int WordCount { get; set; }
    public int FormCount { get; set; }
    public DateTime SubscribedAt { get; set; }
}
