namespace Jiten.Api.Dtos;

public class TagWithPercentageDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Percentage { get; set; }
}
