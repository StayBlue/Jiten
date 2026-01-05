using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class MediaSuggestionDto
{
    public int DeckId { get; set; }
    public string OriginalTitle { get; set; } = "";
    public string? RomajiTitle { get; set; }
    public string? EnglishTitle { get; set; }
    public MediaType MediaType { get; set; }
    public string CoverName { get; set; } = "nocover.jpg";
}
