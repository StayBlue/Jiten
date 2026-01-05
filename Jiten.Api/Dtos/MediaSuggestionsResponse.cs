namespace Jiten.Api.Dtos;

public class MediaSuggestionsResponse
{
    public List<MediaSuggestionDto> Suggestions { get; set; } = new();
    public int TotalCount { get; set; }
}
