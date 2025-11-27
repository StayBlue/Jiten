namespace Jiten.Core.Data.Providers;

public class Metadata
{
    public string? FilePath { get; set; }
    public string OriginalTitle { get; set; } = "Unknown";
    public string? RomajiTitle { get; set; }
    public string? EnglishTitle { get; set; }
    public string? Description { get; set; }
    public string? Image { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public List<Link> Links { get; set; } = new();
    public List<Metadata> Children { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
    public int? Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<(string, int)> Tags { get; set; } = new();
    public bool IsAdultOnly { get; set; }
}