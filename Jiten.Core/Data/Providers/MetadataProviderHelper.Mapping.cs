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
                                       .Where(m => m.Provider == linkType)
                                       .ToListAsync();

        // Add new genres if found
        if (metadata.Genres.Any())
        {
            var genreMappingsDict = new Dictionary<string, ExternalGenreMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in genreMappings)
            {
                genreMappingsDict.TryAdd(mapping.ExternalGenreName, mapping);
            }

            foreach (var externalGenreName in metadata.Genres)
            {
                genreMappingsDict.TryGetValue(externalGenreName, out var mapping);

                if (mapping != null && deck.DeckGenres.All(dg => dg.Genre != mapping.JitenGenre))
                {
                    deck.DeckGenres.Add(new DeckGenre { DeckId = deck.DeckId, Genre = mapping.JitenGenre });
                }
            }
        }
        
        if (metadata.IsAdultOnly && deck.DeckGenres.All(dg => dg.Genre != Genre.AdultOnly))
            deck.DeckGenres.Add(new DeckGenre { DeckId = deck.DeckId, Genre = Genre.AdultOnly });

        const int NotOriginallyJpTagId = 249;
        if (metadata.IsNotOriginallyJapanese && deck.DeckTags.All(dt => dt.TagId != NotOriginallyJpTagId))
            deck.DeckTags.Add(new DeckTag { DeckId = deck.DeckId, TagId = NotOriginallyJpTagId, Percentage = 100 });

        // Add new tags if found (using highest percentage for duplicate mappings)
        if (metadata.Tags.Any())
        {
            // Phase 1: Group external tags by internal TagId and find max percentage
            var tagCandidates = new Dictionary<int, byte>();

            var tagMappingsDict = new Dictionary<string, ExternalTagMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in tagMappings)
            {
                tagMappingsDict.TryAdd(mapping.ExternalTagName, mapping);
            }

            foreach (var tag in metadata.Tags)
            {
                tagMappingsDict.TryGetValue(tag.Name, out var mapping);

                if (mapping == null) continue;

                byte validPercentage = (byte)Math.Clamp(tag.Percentage, 0, 100);

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
                    // Only set foreign keys, not navigation properties
                    // Setting Tag navigation property causes EF to attempt INSERT of existing Tag entity
                    deck.DeckTags.Add(new DeckTag
                                      {
                                          DeckId = deck.DeckId,
                                          TagId = tagId,
                                          Percentage = maxPercentage
                                      });
                }
            }
        }
    }
}
