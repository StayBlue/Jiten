using Jiten.Core.Data;

namespace Jiten.Api.Dtos.Requests;

public class AddMediaRequest
{
    public MediaType MediaType { get; set; }
    public DateOnly ReleaseDate { get; set; }
    public required string OriginalTitle { get; set; }
    public string? RomajiTitle { get; set; }
    public string? EnglishTitle { get; set; }
    public string? Description { get; set; }
    public int Rating { get; set; }
    public IFormFile? CoverImage { get; set; }
    public IFormFile? File { get; set; }
    public List<Link> Links { get; set; } = new List<Link>();
    public List<AddMediaRequestSubdeck>? Subdecks { get; set; } = new List<AddMediaRequestSubdeck>();
    public List<string> Genres { get; set; } = new List<string>();
    public List<AddMediaRequestTag> Tags { get; set; } = new List<AddMediaRequestTag>();
    public bool IsAdultOnly { get; set; }
}

public class AddMediaRequestSubdeck
{
    public required string OriginalTitle { get; set; }
    public required IFormFile File { get; set; }
}

public class AddMediaRequestTag
{
    public required string Name { get; set; }
    public int Percentage { get; set; }
}
