using Jiten.Core.Data;

namespace Jiten.Api.Dtos.Requests;

public class SetDeckStatusRequest
{
    public required DeckStatus Status { get; set; }
}
