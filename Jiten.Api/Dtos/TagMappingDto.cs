using Jiten.Core.Data;

namespace Jiten.Api.Dtos;

public class TagMappingDto
{
    public int ExternalTagMappingId { get; set; }
    public LinkType Provider { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ExternalTagName { get; set; } = string.Empty;
    public int TagId { get; set; }
    public string TagName { get; set; } = string.Empty;
}
