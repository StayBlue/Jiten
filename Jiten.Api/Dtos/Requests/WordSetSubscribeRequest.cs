using Jiten.Core.Data;

namespace Jiten.Api.Dtos.Requests;

public class WordSetSubscribeRequest
{
    public WordSetStateType State { get; set; } = WordSetStateType.Blacklisted;
}
