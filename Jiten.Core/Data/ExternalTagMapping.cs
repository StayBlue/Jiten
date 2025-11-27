namespace Jiten.Core.Data;

public class ExternalTagMapping
{
    public int ExternalTagMappingId { get; set; }
    public LinkType Provider { get; set; }
    public string ExternalTagName { get; set; } = string.Empty;
    public int TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
