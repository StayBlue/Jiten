namespace Jiten.Core.Data.Providers.Vndb;

public class VndbReleasePageResult
{
    public int Count { get; set; }
    public List<VndbReleaseResult> Results { get; set; } = new List<VndbReleaseResult>();
}