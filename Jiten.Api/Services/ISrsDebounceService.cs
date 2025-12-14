namespace Jiten.Api.Services;

public interface ISrsDebounceService
{
    bool TryAcquire(string userId, int wordId, byte readingIndex);
}
