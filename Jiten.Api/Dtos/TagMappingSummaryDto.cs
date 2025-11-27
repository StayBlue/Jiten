namespace Jiten.Api.Dtos;

public class TagMappingSummaryDto
{
    public int TotalMappings { get; set; }
    public Dictionary<string, int> MappingsByProvider { get; set; } = new();
}
