using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class GenreMappingDto
{
    public int ExternalGenreMappingId { get; set; }
    public LinkType Provider { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ExternalGenreName { get; set; } = string.Empty;
    public Genre JitenGenre { get; set; }
    public string JitenGenreName { get; set; } = string.Empty;
}
