using System.ComponentModel.DataAnnotations;

namespace Jiten.Api.Dtos.Requests;

public class UpdateRubyTextRequest
{
    [Required]
    public string RubyText { get; set; } = "";
}
