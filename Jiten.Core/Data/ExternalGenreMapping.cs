namespace Jiten.Core.Data;

public class ExternalGenreMapping
{
    public int ExternalGenreMappingId { get; set; }
    public LinkType Provider { get; set; }
    public string ExternalGenreName { get; set; } = string.Empty;
    public Genre JitenGenre { get; set; }
}
