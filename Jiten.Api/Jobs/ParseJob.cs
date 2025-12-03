using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Jiten.Core.Data.User;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Jiten.Cli;

namespace Jiten.Api.Jobs;

public class ParseJob(IDbContextFactory<JitenDbContext> contextFactory, IDbContextFactory<UserDbContext> userContextFactory, IBackgroundJobClient backgroundJobs)
{
    public async Task Parse(Metadata metadata, MediaType deckType, bool storeRawText = false)
    {
        Deck deck = new();
        string filePath = metadata.FilePath;

        await using var context = await contextFactory.CreateDbContextAsync();

        if (!string.IsNullOrEmpty(metadata.FilePath))
        {
            if (!File.Exists(metadata.FilePath))
            {
                throw new FileNotFoundException($"File {filePath} not found.");
            }

            string text = "";
            if (Path.GetExtension(filePath).ToLower() == ".epub")
            {
                var extractor = new EbookExtractor();
                text = await extractor.ExtractTextFromEbook(filePath);

                if (string.IsNullOrEmpty(text))
                {
                    throw new Exception("No text found in the ebook.");
                }
            }
            else if (Path.GetExtension(filePath).ToLower() == ".mokuro")
            {
                var extractor = new MokuroExtractor();
                text = await extractor.Extract(filePath, false);

                if (string.IsNullOrEmpty(text))
                {
                    throw new Exception("No text found in the mokuro file.");
                }
            }
            else
            {
                text = await File.ReadAllTextAsync(filePath);
            }

            deck = await Parser.Parser.ParseTextToDeck(contextFactory, text, storeRawText, true, deckType);
        }

        // Process children recursively
        int deckOrder = 0;
        foreach (var child in metadata.Children)
        {
            var childDeck = await ParseChild(child, deck, deckType, ++deckOrder, storeRawText);
            deck.Children.Add(childDeck);
        }

        await deck.AddChildDeckWords(context);

        deck.OriginalTitle = metadata.OriginalTitle;
        deck.MediaType = deckType;

        if (metadata.ReleaseDate != null)
            deck.ReleaseDate = DateOnly.FromDateTime(metadata.ReleaseDate.Value);

        if (deckType is MediaType.Manga or MediaType.Anime or MediaType.Movie or MediaType.Drama)
            deck.SentenceCount = 0;

        deck.RomajiTitle = metadata.RomajiTitle;
        deck.EnglishTitle = metadata.EnglishTitle;
        deck.Description = metadata.Description?.Length > 2000 ? metadata.Description?[..2000] : metadata.Description;;
        deck.Links = metadata.Links;
        deck.CoverName = metadata.Image ?? "nocover.jpg";
        deck.CreationDate = DateTimeOffset.UtcNow;
        deck.LastUpdate = DateTime.UtcNow;
        deck.DifficultyOverride = -1;
        deck.Titles = metadata.Aliases.Select(a => new DeckTitle { DeckId = deck.DeckId, Title = a, TitleType = DeckTitleType.Alias }).ToList();
        deck.ExternalRating = metadata.Rating != null ? (byte)metadata.Rating : (byte)0;

        foreach (var link in deck.Links)
        {
            link.Deck = deck;
        }

        // Apply genre and tag mappings based on first link
        if (metadata.Links.Any())
        {
            var firstLink = metadata.Links.First();
            await MetadataProviderHelper.ApplyGenreAndTagMappings(context, deck, metadata, firstLink.LinkType);
        }

        var coverImage = await File.ReadAllBytesAsync(metadata.Image ?? throw new Exception("No cover image found."));

        // Insert the deck into the database
        await JitenHelper.InsertDeck(contextFactory, deck, coverImage ?? [], false);

        // Queue coverage computation jobs for all users with at least 10 known words
        await QueueCoverageJobsForDeck(deck);

        // Queue coverage statistics computation
        backgroundJobs.Enqueue<StatsComputationJob>(job => job.ComputeDeckCoverageStats(deck.DeckId));
    }

    private async Task<Deck> ParseChild(Metadata metadata, Deck parentDeck, MediaType deckType, int deckOrder, bool storeRawText)
    {
        Deck deck = new();
        string filePath = metadata.FilePath;

        if (!string.IsNullOrEmpty(metadata.FilePath))
        {
            if (!File.Exists(metadata.FilePath))
            {
                throw new FileNotFoundException($"File {filePath} not found.");
            }

            string text = "";
            if (Path.GetExtension(filePath).ToLower() == ".epub")
            {
                var extractor = new EbookExtractor();
                text = await extractor.ExtractTextFromEbook(filePath);

                if (string.IsNullOrEmpty(text))
                {
                    throw new Exception("No text found in the ebook.");
                }
            }
            else if (Path.GetExtension(filePath).ToLower() == ".mokuro")
            {
                var extractor = new MokuroExtractor();
                text = await extractor.Extract(filePath, false);

                if (string.IsNullOrEmpty(text))
                {
                    throw new Exception("No text found in the mokuro file.");
                }
            }
            else
            {
                text = await File.ReadAllTextAsync(filePath);
            }

            deck = await Parser.Parser.ParseTextToDeck(contextFactory, text, storeRawText, true, deckType);
            deck.ParentDeck = parentDeck;
            deck.DeckOrder = deckOrder;
            deck.OriginalTitle = metadata.OriginalTitle;
            deck.MediaType = deckType;
            deck.DifficultyOverride = -1;

            if (deckType is MediaType.Manga or MediaType.Anime or MediaType.Movie or MediaType.Drama)
                deck.SentenceCount = 0;
        }

        // Process children recursively
        int childDeckOrder = 0;
        foreach (var child in metadata.Children)
        {
            var childDeck = await ParseChild(child, deck, deckType, ++childDeckOrder, storeRawText);
            deck.Children.Add(childDeck);
        }

        return deck;
    }

    private async Task QueueCoverageJobsForDeck(Deck deck)
    {
        await using var userContext = await userContextFactory.CreateDbContextAsync();

        var userIds = await userContext.Users
                                       .Where(u => userContext.FsrsCards.Count(c => c.UserId == u.Id) >= 10)
                                       .Select(u => u.Id)
                                       .ToListAsync();

        foreach (var userId in userIds)
        {
                backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserDeckCoverage(userId, deck.DeckId));
        }
    }
}