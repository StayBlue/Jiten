using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Api.Jobs;

public class ReparseJob(IDbContextFactory<JitenDbContext> contextFactory)
{
    public async Task Reparse(int deckId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var deck = await context.Decks.AsNoTracking().Include(d => d.RawText).Include(d => d.ExampleSentences).Include(d => d.Children).ThenInclude(deck => deck.RawText).Include(deck => deck.ExampleSentences)
                                .FirstOrDefaultAsync(d => d.DeckId == deckId);
        if (deck == null)
            throw new Exception($"Deck with ID {deckId} not found.");

        if (deck.RawText == null && deck.Children.Count == 0)
            throw new Exception($"Deck with ID {deckId} has no raw text to reparse.");

        var children = deck.Children.ToList();

        if (deck.Children.Count == 0)
        {
            Deck newDeck = await Parser.Parser.ParseTextToDeck(contextFactory, deck.RawText.RawText, true, true, deck.MediaType);
            deck.CharacterCount = newDeck.CharacterCount;
            deck.WordCount = newDeck.WordCount;
            deck.UniqueWordCount = newDeck.UniqueWordCount;
            deck.UniqueWordUsedOnceCount = newDeck.UniqueWordUsedOnceCount;
            deck.UniqueKanjiCount = newDeck.UniqueKanjiCount;
            deck.UniqueKanjiUsedOnceCount = newDeck.UniqueKanjiUsedOnceCount;
            deck.SentenceCount = newDeck.SentenceCount;
            deck.DeckWords = newDeck.DeckWords;
            deck.ExampleSentences = newDeck.ExampleSentences;
            deck.Difficulty = newDeck.Difficulty;
            deck.DialoguePercentage = newDeck.DialoguePercentage;

            if (deck.MediaType is MediaType.Manga or MediaType.Anime or MediaType.Movie or MediaType.Drama)
                deck.SentenceCount = 0;
        }
        else
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children.ElementAt(i);
                if (child.RawText == null)
                    throw new Exception($"Child deck with ID {child.DeckId} has no raw text to reparse.");

                Deck newDeck = await Parser.Parser.ParseTextToDeck(contextFactory, child.RawText.RawText, true, true, child.MediaType);

                children[i].CharacterCount = newDeck.CharacterCount;
                children[i].WordCount = newDeck.WordCount;
                children[i].UniqueWordCount = newDeck.UniqueWordCount;
                children[i].UniqueWordUsedOnceCount = newDeck.UniqueWordUsedOnceCount;
                children[i].UniqueKanjiCount = newDeck.UniqueKanjiCount;
                children[i].UniqueKanjiUsedOnceCount = newDeck.UniqueKanjiUsedOnceCount;
                children[i].SentenceCount = newDeck.SentenceCount;
                children[i].DeckWords = newDeck.DeckWords;
                children[i].ExampleSentences = newDeck.ExampleSentences;
                children[i].Difficulty = newDeck.Difficulty;
                children[i].DialoguePercentage = newDeck.DialoguePercentage;

                if (children[i].MediaType is MediaType.Manga or MediaType.Anime or MediaType.Movie or MediaType.Drama)
                    children[i].SentenceCount = 0;
            }

            deck.Children = children;
            await deck.AddChildDeckWords(context);
        }

        deck.LastUpdate = DateTime.UtcNow;

        await JitenHelper.InsertDeck(contextFactory, deck, [], true);
    }
}