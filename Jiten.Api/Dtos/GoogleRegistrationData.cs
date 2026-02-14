namespace Jiten.Api.Dtos;

public class GoogleRegistrationData
{
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
    public string? Username { get; set; }
    public required string TempToken { get; set; }
}