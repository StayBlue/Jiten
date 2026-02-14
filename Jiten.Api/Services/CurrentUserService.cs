using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.FSRS;
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

    public async Task<Dictionary<(int WordId, byte ReadingIndex), List<KnownState>>> GetKnownWordsState(
        IEnumerable<(int WordId, byte ReadingIndex)> keys)
    {
        if (!IsAuthenticated)
            return new();

        var keysSet = keys.ToHashSet();
        if (keysSet.Count == 0)
            return new();

        var wordIds = keysSet.Select(k => k.WordId).Distinct().ToList();

        // 1. Get FsrsCards
        var candidates = await userContext.FsrsCards
                                          .Where(u => u.UserId == UserId && wordIds.Contains(u.WordId))
                                          .ToListAsync();

        var fsrsCardDict = candidates
                            .Where(w => keysSet.Contains((w.WordId, w.ReadingIndex)))
                            .ToDictionary(w => (w.WordId, w.ReadingIndex));

        // 2. Get user's WordSet subscriptions
        var userSetStates = await userContext.UserWordSetStates
                                             .Where(uwss => uwss.UserId == UserId)
                                             .ToListAsync();

        // 3. If user has subscriptions, get which requested words are in those sets
        Dictionary<(int, byte), WordSetStateType> setDerivedStates = new();
        if (userSetStates.Count > 0)
        {
            var subscribedSetIds = userSetStates.Select(s => s.SetId).ToList();

            var memberships = await jitenDbContext.WordSetMembers
                .Where(wsm => subscribedSetIds.Contains(wsm.SetId) && wordIds.Contains(wsm.WordId))
                .Select(wsm => new { wsm.WordId, wsm.ReadingIndex, wsm.SetId })
                .ToListAsync();

            var setStateDict = userSetStates.ToDictionary(s => s.SetId, s => s.State);

            foreach (var m in memberships)
            {
                if (m.ReadingIndex < 0 || m.ReadingIndex > byte.MaxValue) continue;
                var key = (m.WordId, (byte)m.ReadingIndex);
                if (!keysSet.Contains(key))
                    continue;

                var newState = setStateDict[m.SetId];

                // Handle overlapping sets: Mastered wins over Blacklisted
                if (!setDerivedStates.TryGetValue(key, out var existingState))
                {
                    setDerivedStates[key] = newState;
                }
                else if (newState == WordSetStateType.Mastered && existingState == WordSetStateType.Blacklisted)
                {
                    setDerivedStates[key] = WordSetStateType.Mastered;
                }
            }
        }

        // 4. Build result: FsrsCard wins, then WordSet, then New
        return keysSet.ToDictionary(k => k, k =>
        {
            // FsrsCard takes precedence
            if (fsrsCardDict.TryGetValue(k, out var card))
                return GetKnownStatesFromCard(card);

            // Check WordSet-derived state
            if (setDerivedStates.TryGetValue((k.WordId, k.ReadingIndex), out var setState))
            {
                return setState switch
                {
                    WordSetStateType.Blacklisted => [KnownState.Blacklisted],
                    WordSetStateType.Mastered => [KnownState.Mastered],
                    _ => [KnownState.New]
                };
            }

            return [KnownState.New];
        });
    }

    private static List<KnownState> GetKnownStatesFromCard(FsrsCard card)
    {
        List<KnownState> knownState = new();
        switch (card.State)
        {
            case FsrsState.Mastered:
                knownState.Add(KnownState.Mastered);
                break;
            case FsrsState.Blacklisted:
                knownState.Add(KnownState.Blacklisted);
                break;
            case FsrsState.New:
                knownState.Add(KnownState.New);
                break;
        }

        if (knownState.Count > 0)
            return knownState;

        if (card.LastReview == null)
        {
            knownState.Add(KnownState.Due);
            return knownState;
        }

        if (card.Due <= DateTime.UtcNow)
            knownState.Add(KnownState.Due);

        var interval = (card.Due - card.LastReview.Value).TotalDays;
        knownState.Add(interval < 21 ? KnownState.Young : KnownState.Mature);

        return knownState;
    }

    public async Task<List<KnownState>> GetKnownWordState(int wordId, byte readingIndex)
    {
        if (!IsAuthenticated)
            return [KnownState.New];

        // 1. Check FsrsCard first (takes precedence)
        var card = await userContext.FsrsCards.FirstOrDefaultAsync(u => u.UserId == UserId && u.WordId == wordId &&
                                                                        u.ReadingIndex == readingIndex);

        if (card != null)
            return GetKnownStatesFromCard(card);

        // 2. Check WordSet subscriptions (separate queries to avoid cross-context join)
        var userSetStates = await userContext.UserWordSetStates
            .Where(uwss => uwss.UserId == UserId)
            .ToListAsync();

        if (userSetStates.Count > 0)
        {
            var subscribedSetIds = userSetStates.Select(s => s.SetId).ToList();

            var matchingSetIds = await jitenDbContext.WordSetMembers
                .Where(wsm => subscribedSetIds.Contains(wsm.SetId) && wsm.WordId == wordId && wsm.ReadingIndex == readingIndex)
                .Select(wsm => wsm.SetId)
                .ToListAsync();

            var setStateDict = userSetStates.ToDictionary(s => s.SetId, s => s.State);
            var setDerivedStates = matchingSetIds
                .Where(setStateDict.ContainsKey)
                .Select(id => setStateDict[id])
                .ToList();

            if (setDerivedStates.Count > 0)
            {
                var effectiveState = setDerivedStates.Contains(WordSetStateType.Mastered)
                    ? WordSetStateType.Mastered
                    : setDerivedStates.First();

                return effectiveState switch
                {
                    WordSetStateType.Blacklisted => [KnownState.Blacklisted],
                    WordSetStateType.Mastered => [KnownState.Mastered],
                    _ => [KnownState.New]
                };
            }
        }

        return [KnownState.New];
    }

    public async Task<int> AddKnownWords(IEnumerable<DeckWord> deckWords)
    {
        if (!IsAuthenticated) return 0;
        var words = deckWords?.ToList() ?? [];
        if (words.Count == 0) return 0;

        var wordIds = words.Select(w => w.WordId).Distinct().ToList();

        var validForms = await jitenDbContext.WordForms
                                             .AsNoTracking()
                                             .Where(wf => wordIds.Contains(wf.WordId))
                                             .Select(wf => new { wf.WordId, wf.ReadingIndex })
                                             .ToListAsync();
        var validFormSet = validForms.Select(f => (f.WordId, (byte)f.ReadingIndex)).ToHashSet();

        // Determine all (WordId, ReadingIndex) pairs to add, preserving input order
        var pairs = new List<(int WordId, byte ReadingIndex)>();
        var seen = new HashSet<(int, byte)>();
        foreach (var word in words)
        {
            if (!validFormSet.Contains((word.WordId, word.ReadingIndex))) continue;

            var key = (word.WordId, word.ReadingIndex);
            if (seen.Add(key))
                pairs.Add(key);
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

    public async Task<int> BlacklistWords(IEnumerable<DeckWord> deckWords)
    {
        if (!IsAuthenticated) return 0;
        var words = deckWords?.ToList() ?? [];
        if (words.Count == 0) return 0;

        var wordIds = words.Select(w => w.WordId).Distinct().ToList();

        var blValidForms = await jitenDbContext.WordForms
                                               .AsNoTracking()
                                               .Where(wf => wordIds.Contains(wf.WordId))
                                               .Select(wf => new { wf.WordId, wf.ReadingIndex })
                                               .ToListAsync();
        var blValidFormSet = blValidForms.Select(f => (f.WordId, (byte)f.ReadingIndex)).ToHashSet();

        var pairs = new List<(int WordId, byte ReadingIndex)>();
        var seen = new HashSet<(int, byte)>();
        foreach (var word in words)
        {
            if (!blValidFormSet.Contains((word.WordId, word.ReadingIndex))) continue;

            var key = (word.WordId, word.ReadingIndex);
            if (seen.Add(key))
                pairs.Add(key);
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
                                           state: FsrsState.Blacklisted);
                toInsert.Add(newCard);
                cardsToSync.Add((newCard.WordId, newCard.ReadingIndex, newCard, true));
            }
            else
            {
                existingUk.State = FsrsState.Blacklisted;
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
        await srsService.SyncKanaReading(UserId!, wordId, readingIndex, card, DateTime.UtcNow);

        await userContext.SaveChangesAsync();
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}