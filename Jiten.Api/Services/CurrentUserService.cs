using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.User;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Services;

public class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    JitenDbContext jitenDbContext,
    UserDbContext userContext,
    ISrsService srsService)
    : ICurrentUserService
{
    public ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? UserId
    {
        get
        {
            var user = Principal;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
                return null;

            return userId;
        }
    }

    public async Task<Dictionary<(int WordId, byte ReadingIndex), KnownState>> GetKnownWordsState(
        IEnumerable<(int WordId, byte ReadingIndex)> keys, bool getDue = false)
    {
        if (!IsAuthenticated)
            return new Dictionary<(int, byte), KnownState>();

        var keysList = keys.Distinct().ToList();
        if (!keysList.Any())
            return new Dictionary<(int, byte), KnownState>();

        var wordIds = keysList.Select(k => k.WordId).Distinct().ToList();

        var candidates = await userContext.FsrsCards
                                          .Where(u => u.UserId == UserId && wordIds.Contains(u.WordId))
                                          .ToListAsync();

        var candidateDict = candidates
                            .Where(w => keysList.Contains((w.WordId, w.ReadingIndex)))
                            .ToDictionary<FsrsCard, (int WordId, byte ReadingIndex), KnownState>(w => (w.WordId, w.ReadingIndex), w =>
                            {
                                if (w.State == FsrsState.Mastered) return KnownState.Mastered;
                                if (w.State == FsrsState.Blacklisted) return KnownState.Blacklisted;
                                if (w.State == FsrsState.New) return KnownState.New;

                                if (w.LastReview == null || (getDue && w.Due <= DateTime.UtcNow))
                                    return KnownState.Due;

                                var interval = (w.Due - w.LastReview.Value).TotalDays;
                                return interval < 21 ? KnownState.Young : KnownState.Mature;
                            });

        return keysList.ToDictionary(k => k,
                                     k => candidateDict.GetValueOrDefault(k, KnownState.New));
    }

    public async Task<KnownState> GetKnownWordState(int wordId, byte readingIndex)
    {
        if (!IsAuthenticated)
            return KnownState.New;

        var word = await userContext.FsrsCards.FirstOrDefaultAsync(u => u.UserId == UserId && u.WordId == wordId &&
                                                                        u.ReadingIndex == readingIndex);

        if (word == null || word.State == FsrsState.New)
            return KnownState.New;

        if (word.State == FsrsState.Blacklisted)
            return KnownState.Blacklisted;

        if (word.State == FsrsState.Mastered)
            return KnownState.Mastered;

        if (word.LastReview == null)
            return KnownState.New;

        var dueIn = (word.Due - word.LastReview.Value).TotalDays;
        return dueIn < 21 ? KnownState.Young : KnownState.Mature;
    }

    public async Task<int> AddKnownWords(IEnumerable<DeckWord> deckWords)
    {
        if (!IsAuthenticated) return 0;
        var words = deckWords?.ToList() ?? [];
        if (words.Count == 0) return 0;

        var byWordId = words.GroupBy(w => w.WordId).ToDictionary(g => g.Key, g => g.Select(x => x.ReadingIndex).Distinct().ToList());
        var wordIds = byWordId.Keys.ToList();

        // Load needed JMDict words
        var jmdictWords = await jitenDbContext.JMDictWords
                                              .AsNoTracking()
                                              .Where(w => wordIds.Contains(w.WordId))
                                              .Select(w => new { w.WordId, w.ReadingTypes })
                                              .ToListAsync();

        // Determine all (WordId, ReadingIndex) pairs to add
        var pairs = new HashSet<(int WordId, byte ReadingIndex)>();
        foreach (var jw in jmdictWords)
        {
            if (!byWordId.TryGetValue(jw.WordId, out var indices)) continue;
            foreach (var idx in indices)
            {
                if (idx >= jw.ReadingTypes.Count) continue;
                pairs.Add((jw.WordId, idx));
            }
        }

        if (pairs.Count == 0) return 0;

        DateTime now = DateTime.UtcNow;
        List<int> pairWordIds = pairs.Select(p => p.WordId).Distinct().ToList();
        List<FsrsCard> existing = await userContext.FsrsCards
                                                   .Where(uk => uk.UserId == UserId && pairWordIds.Contains(uk.WordId))
                                                   .ToListAsync();
        var existingSet = existing.ToDictionary(e => (e.WordId, e.ReadingIndex));

        List<FsrsCard> toInsert = new();
        var cardsToSync = new List<(int WordId, byte ReadingIndex, FsrsCard SourceCard, bool Overwrite)>();

        foreach (var p in pairs)
        {
            if (!existingSet.TryGetValue(p, out var existingUk))
            {
                var newCard = new FsrsCard(UserId!, p.WordId, p.ReadingIndex, due: now, lastReview: now,
                                           state: FsrsState.Mastered);
                toInsert.Add(newCard);
                cardsToSync.Add((newCard.WordId, newCard.ReadingIndex, newCard, true));
            }
            else
            {
                existingUk.State = FsrsState.Mastered;
                cardsToSync.Add((existingUk.WordId, existingUk.ReadingIndex, existingUk, true));
            }
        }

        if (toInsert.Count > 0)
            await userContext.FsrsCards.AddRangeAsync(toInsert);

        await userContext.SaveChangesAsync();

        if (cardsToSync.Count > 0)
        {
            await srsService.SyncKanaReadingBatch(UserId!, cardsToSync, now);
        }

        return (toInsert.Count);
    }

    public async Task AddKnownWord(int wordId, byte readingIndex)
    {
        await AddKnownWords([new DeckWord { WordId = wordId, ReadingIndex = readingIndex }]);
    }

    public async Task RemoveKnownWord(int wordId, byte readingIndex)
    {
        if (!IsAuthenticated) return;

        var card = await userContext.FsrsCards.FirstOrDefaultAsync(u => u.UserId == UserId && u.WordId == wordId &&
                                                                        u.ReadingIndex == readingIndex);
        if (card == null) return;

        card.State = FsrsState.New;
        await srsService.SyncKanaReading(UserId, wordId, readingIndex, card, DateTime.UtcNow);

        await userContext.SaveChangesAsync();
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}