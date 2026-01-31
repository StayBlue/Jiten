namespace Jiten.Api.Dtos.Requests;

public class ImportFromIdsRequest
{
    public List<long> WordIds { get; set; } = new();
    public int? FrequencyThreshold { get; set; }
}
