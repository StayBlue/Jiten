namespace Jiten.Core.Data;

public class UserMetadata
{
    public string UserId { get; set; } = string.Empty;

    public DateTime? CoverageRefreshedAt { get; set; }
    public bool CoverageDirty { get; set; }
    public DateTime? CoverageDirtyAt { get; set; }
}
