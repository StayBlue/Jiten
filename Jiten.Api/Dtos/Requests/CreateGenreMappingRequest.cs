using System.ComponentModel.DataAnnotations;
using Jiten.Core.Data;

namespace Jiten.Api.Dtos.Requests;

public class CreateGenreMappingRequest
{
    [Required]
    public LinkType Provider { get; set; }

    [Required]
    [MaxLength(100)]
    public required string ExternalGenreName { get; set; }

    [Required]
    public Genre JitenGenre { get; set; }
}
