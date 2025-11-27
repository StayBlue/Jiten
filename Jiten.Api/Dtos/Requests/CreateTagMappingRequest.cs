using System.ComponentModel.DataAnnotations;
using Jiten.Core.Data;

namespace Jiten.Api.Dtos.Requests;

public class CreateTagMappingRequest
{
    [Required]
    public LinkType Provider { get; set; }

    [Required]
    [MaxLength(100)]
    public required string ExternalTagName { get; set; }

    [Required]
    public int TagId { get; set; }
}
