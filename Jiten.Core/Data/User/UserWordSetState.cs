namespace Jiten.Core.Data;

public class UserWordSetState
{
    public string UserId { get; set; } = string.Empty;
    public int SetId { get; set; }
    public WordSetStateType State { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
