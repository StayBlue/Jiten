using System.ComponentModel.DataAnnotations;

namespace Jiten.Api.Dtos.Requests;

public class UpdateTagRequest
{
    [Required]
    [MaxLength(50)]
    public required string Name { get; set; }
}
