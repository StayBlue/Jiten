namespace Jiten.Core.Data.Providers.Vndb;

public class VndbRequestResult
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public DateTime Released { get; set; }
    public List<VnTitle>? Titles { get; set; }
    public VnImage? Image { get; set; }
    public string? Description { get; set; }
    public List<string> Aliases { get; set; }
    public double? Rating { get; set; }
    public List<VndbTag> Tags { get; set; } = new();
}