namespace Jiten.Api.Dtos.Requests;

public class SetMaintenanceBannerRequest
{
    public bool IsActive { get; set; }
    public string? Message { get; set; }
}
