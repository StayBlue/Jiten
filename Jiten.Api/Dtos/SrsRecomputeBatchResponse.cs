namespace Jiten.Api.Dtos;

public class SrsRecomputeBatchResponse
{
    public int Processed { get; set; }
    public int Total { get; set; }
    public long LastCardId { get; set; }
    public bool Done { get; set; }
}
