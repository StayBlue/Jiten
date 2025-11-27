using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Core;

public static partial class MetadataProviderHelper
{
    /// <summary>
    /// Applies genre and tag mappings from external provider to Jiten's internal categories.
    /// Only adds NEW genres/tags - never removes existing ones.
    /// </summary>
    /// <param name="context">Database context for querying mappings</param>
    /// <param name="deck">Deck to update with mapped genres/tags</param>
    /// <param name="metadata">Metadata containing external genre/tag names and percentages</param>
    /// <param name="linkType">Provider type to determine which mappings to use</param>
    /// <returns>Task</returns>
    public static async Task ApplyGenreAndTagMappings(JitenDbContext context, Deck deck, Metadata metadata, LinkType linkType)
    {
        var genreMappings = await context.ExternalGenreMappings
                                         .AsNoTracking()
                                         .Where(m => m.Provider == linkType)
                                         .ToListAsync();

        var tagMappings = await context.ExternalTagMappings
                                       .AsNoTracking()
                                       .Include(m => m.Tag)
                                       .Where(m => m.Provider == linkType)
                                       .ToListAsync();

        // Add new genres if found
        if (metadata.Genres.Any())
        {
            foreach (var externalGenreName in metadata.Genres)
            {
                var mapping =
                    genreMappings.FirstOrDefault(m => m.ExternalGenreName.Equals(externalGenreName, StringComparison.OrdinalIgnoreCase));

                if (mapping != null && deck.DeckGenres.All(dg => dg.Genre != mapping.JitenGenre))
                {
                    deck.DeckGenres.Add(new DeckGenre { DeckId = deck.DeckId, Genre = mapping.JitenGenre, Deck = deck });
                }
            }
        }
        
        if (metadata.IsAdultOnly && deck.DeckGenres.All(dg => dg.Genre != Genre.AdultOnly))
            deck.DeckGenres.Add(new DeckGenre { DeckId = deck.DeckId, Genre = Genre.AdultOnly, Deck = deck });

        // Add new tags if found (using highest percentage for duplicate mappings)
        if (metadata.Tags.Any())
        {
            // Phase 1: Group external tags by internal TagId and find max percentage
            var tagCandidates = new Dictionary<int, byte>();

            foreach (var (externalTagName, percentage) in metadata.Tags)
            {
                var mapping = tagMappings.FirstOrDefault(m => m.ExternalTagName.Equals(externalTagName, StringComparison.OrdinalIgnoreCase));

                if (mapping == null) continue;

                byte validPercentage = (byte)Math.Clamp(percentage, 0, 100);

                // If we've seen this TagId before, keep the higher percentage
                if (!tagCandidates.TryAdd(mapping.TagId, validPercentage))
                {
                    tagCandidates[mapping.TagId] = Math.Max(tagCandidates[mapping.TagId], validPercentage);
                }
            }

            // Phase 2: Add new tags or update existing ones if new percentage is higher
            foreach (var (tagId, maxPercentage) in tagCandidates)
            {
                var existingDeckTag = deck.DeckTags.FirstOrDefault(dt => dt.TagId == tagId);

                if (existingDeckTag != null)
                {
                    // Tag already exists - update percentage if new value is higher
                    if (maxPercentage > existingDeckTag.Percentage)
                    {
                        existingDeckTag.Percentage = maxPercentage;
                    }
                }
                else
                {
                    // Tag doesn't exist - add it
                    var tagMapping = tagMappings.First(m => m.TagId == tagId);
                    deck.DeckTags.Add(new DeckTag
                                      {
                                          DeckId = deck.DeckId,
                                          TagId = tagId,
                                          Percentage = maxPercentage,
                                          Deck = deck,
                                          Tag = tagMapping.Tag
                                      });
                }
            }
        }
    }
}