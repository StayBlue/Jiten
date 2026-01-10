using System.Diagnostics;
using System.Text;
using System.Threading;
using ImageMagick;
using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jiten.Core;

public static class JitenHelper
{
    // Cap concurrent PostgreSQL COPY operations to reduce server pressure/timeouts
    private static readonly SemaphoreSlim CopySemaphore = new SemaphoreSlim(6);
    public static async Task InsertDeck(IDbContextFactory<JitenDbContext> contextFactory, Deck deck, byte[] cover, bool update = false)
    {
        var totalTimer = Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.UtcNow:O}] Inserting deck {deck.OriginalTitle}...");

        byte[] optimizedCoverBytes = null;
        try
        {
            if (cover.Length > 0)
            {
                using var coverOptimized = new MagickImage(cover);
                coverOptimized.Resize(400, 400);
                coverOptimized.Strip();
                coverOptimized.Quality = 85;
                coverOptimized.Format = MagickFormat.Jpeg;
                optimizedCoverBytes = coverOptimized.ToByteArray();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Warning: cover processing failed: {ex.Message}. Continuing without optimized cover.");
        }

        // Create a context for the transactional metadata update and to get DeckId
        await using var context = await contextFactory.CreateDbContextAsync();
        // start a transaction only around the initial deck insert/update metadata
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var existingDeck =
                await context.Decks
                             .Include(d => d.DeckWords)
                             .Include(d => d.Children).ThenInclude(d => d.DeckWords)
                             .Include(d => d.RawText)
                             .Include(d => d.ExampleSentences)
                             .OrderBy(d => d.DeckOrder)
                             .FirstOrDefaultAsync(d => d.OriginalTitle == deck.OriginalTitle && d.MediaType == deck.MediaType);

            if (existingDeck != null)
            {
                if (!update || (update && existingDeck.LastUpdate >= deck.LastUpdate))
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Deck {deck.OriginalTitle} already exists, no update flag or deck not changed, skipping.");
                    await transaction.RollbackAsync();
                    return;
                }

                Console.WriteLine($"[{DateTime.UtcNow:O}] Deck {deck.OriginalTitle} exists, updating metadata...");
                await UpdateDeck(contextFactory, context, existingDeck, deck);
                await context.SaveChangesAsync();
                // Console.WriteLine($"[{DateTime.UtcNow:O}] Update completed.");
            }
            else
            {
                // New deck: detach deckwords and example sentences so EF doesn't attempt to cascade insert them
                deck.SetParentsAndDeckWordDeck(deck);
                deck.ParentDeckId = null;

                var deckWordsToInsert = deck.DeckWords?.ToList() ?? new List<DeckWord>();
                deck.DeckWords = new List<DeckWord>();

                var exampleSentencesToInsert = deck.ExampleSentences?.ToList() ?? new List<ExampleSentence>();
                if (deck.ExampleSentences != null) deck.ExampleSentences = new List<ExampleSentence>();

                var childrenToInsert = deck.Children?.ToList() ?? new List<Deck>();
                deck.Children = new List<Deck>();

                // Add minimal deck entity
                context.Decks.Add(deck);
                context.Entry(deck).State = EntityState.Added;
                context.Entry(deck).Collection(d => d.Children).IsModified = false;
                context.Entry(deck).Collection(d => d.DeckWords).IsModified = false;
                context.Entry(deck).Collection(d => d.ExampleSentences).IsModified = false;

                // Hint PostgreSQL to skip fsync for this small transaction; reduces latency for large row inserts
                await context.Database.ExecuteSqlRawAsync(@"SET LOCAL synchronous_commit = OFF");

                // Persist the deck so we have deck.DeckId for foreign keys/paths
                var saveTimer = Stopwatch.StartNew();
                var prevDetect = context.ChangeTracker.AutoDetectChangesEnabled;
                try
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    await context.SaveChangesAsync();
                }
                finally
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = prevDetect;
                }

                Console.WriteLine($"[{DateTime.UtcNow:O}] SaveChanges (new deck) took {saveTimer.ElapsedMilliseconds} ms. DeckId={deck.DeckId}");

                // Commit the transactional metadata now so we don't hold it while doing large COPYs
                await transaction.CommitAsync();

                // Upload the cover (network) after committing -- use optimizedCoverBytes
                if (optimizedCoverBytes != null)
                {
                    try
                    {
                        var coverUrl = await BunnyCdnHelper.UploadFile(optimizedCoverBytes, $"{deck.DeckId}/cover.jpg");
                        deck.CoverName = coverUrl;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Warning: cover upload failed: {ex.Message}");
                    }
                }

                // Bulk insert the deck words for this deck (using a separate connection)
                var deckWordTask = BulkInsertDeckWords(contextFactory, deckWordsToInsert, deck.DeckId);

                // Bulk insert example sentences
                Task exampleSentencesTask = Task.CompletedTask;
                if (exampleSentencesToInsert.Any())
                    exampleSentencesTask = BulkInsertExampleSentences(contextFactory, exampleSentencesToInsert, deck.DeckId);

                // Insert child decks: we will add rows to the DB (get their IDs) then perform their heavy COPYs in parallel.
                var childBulkTasks = InsertChildDecks(contextFactory, childrenToInsert, deck.DeckId);

                // Wait for all bulk operations (deck words, sentences, children) to finish
                await Task.WhenAll(deckWordTask, exampleSentencesTask, childBulkTasks);

                // Update deck entity to reflect cover url if any
                await using var updateCtx = await contextFactory.CreateDbContextAsync();
                updateCtx.Attach(deck);
                updateCtx.Entry(deck).State = EntityState.Modified;

                // Small retry to survive transient socket drops during final update
                var attempts = 0;
                Exception lastEx = null;
                while (attempts < 3)
                {
                    try
                    {
                        attempts++;
                        await updateCtx.SaveChangesAsync();
                        lastEx = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        var delayMs = (int)Math.Pow(2, attempts) * 200;
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Warning: final SaveChanges failed (attempt {attempts}): {ex.Message}. Retrying in {delayMs} ms...");
                        await Task.Delay(delayMs);
                    }
                }
                if (lastEx != null) throw lastEx;

                Console.WriteLine($"[{DateTime.UtcNow:O}] Bulk inserts for deck and children completed.");
                Console.WriteLine($"Insert took {totalTimer.ElapsedMilliseconds} ms.");
                return;
            }

            // If existing deck update path: we committed or will do bulk ops below.
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            // If we are still in transaction, rollback
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                /* ignore */
            }

            Console.WriteLine($"[{DateTime.UtcNow:O}] Error inserting deck: {ex.Message}");
            return;
        }
    }

    public static async Task BulkInsertDeckWords(IDbContextFactory<JitenDbContext> contextFactory, ICollection<DeckWord>? deckWords, int deckId)
    {
        if (deckWords == null || deckWords.Count == 0) return;

        // Defensive deduplication - merge any duplicates by (WordId, ReadingIndex)
        var deduplicatedWords = deckWords
            .GroupBy(dw => new { dw.WordId, dw.ReadingIndex })
            .Select(g =>
            {
                var first = g.First();
                if (g.Count() > 1)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] WARNING: Found {g.Count()} duplicate DeckWords for WordId={first.WordId}, ReadingIndex={first.ReadingIndex} in DeckId={deckId}. Merging.");
                    first.Occurrences = g.Sum(dw => dw.Occurrences);
                }
                return first;
            })
            .ToList();

        var timer = Stopwatch.StartNew();
        // Console.WriteLine($"[{DateTime.UtcNow:O}] Bulk inserting {deduplicatedWords.Count} deck words for DeckId {deckId}...");

        await CopySemaphore.WaitAsync();
        try
        {
            await using (var ctx = await contextFactory.CreateDbContextAsync())
            {
                var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
                await conn.OpenAsync();

                // Disable statement timeout for this COPY scope
                await using (var timeoutCmd = new NpgsqlCommand(@"SET LOCAL statement_timeout = 0", conn))
                {
                    timeoutCmd.CommandTimeout = 0;
                    await timeoutCmd.ExecuteNonQueryAsync();
                }

                // Use binary COPY
                await using (var writer = await conn.BeginBinaryImportAsync(
                    @"COPY jiten.""DeckWords"" (""WordId"", ""ReadingIndex"", ""Occurrences"", ""DeckId"") FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (var dw in deduplicatedWords)
                    {
                        await writer.StartRowAsync();
                        await writer.WriteAsync(dw.WordId);
                        await writer.WriteAsync(dw.ReadingIndex);
                        await writer.WriteAsync(dw.Occurrences);
                        await writer.WriteAsync(deckId);
                    }

                    await writer.CompleteAsync();
                }
            }
        }
        finally
        {
            CopySemaphore.Release();
        }

        Console.WriteLine($"[{DateTime.UtcNow:O}] Bulk insert (deck words) took {timer.ElapsedMilliseconds} ms for DeckId {deckId} with {deduplicatedWords.Count} unique words.");
    }

    private static async Task BulkInsertExampleSentences(IDbContextFactory<JitenDbContext> contextFactory, ICollection<ExampleSentence> exampleSentences, int deckId)
    {
        var timer = Stopwatch.StartNew();
        // Console.WriteLine($"[{DateTime.UtcNow:O}] Bulk inserting {exampleSentences.Count} example sentences for DeckId {deckId}...");
        if (!exampleSentences.Any()) return;

        await CopySemaphore.WaitAsync();
        try
        {
            await using (var ctx = await contextFactory.CreateDbContextAsync())
            {
                var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
                await conn.OpenAsync();

                // Disable statement timeout for this session scope
                await using (var timeoutCmd = new NpgsqlCommand(@"SET LOCAL statement_timeout = 0", conn))
                {
                    timeoutCmd.CommandTimeout = 0;
                    await timeoutCmd.ExecuteNonQueryAsync();
                }

                // Step 1: preallocate unique IDs from the backing sequence (safe under concurrency)
                var ids = new List<int>(exampleSentences.Count);
                await using (var idCmd = new NpgsqlCommand(
                    @"SELECT nextval(pg_get_serial_sequence('jiten.""ExampleSentences""', 'SentenceId')::regclass)
                         FROM generate_series(1, @cnt)", conn))
                {
                    idCmd.CommandTimeout = 0;
                    idCmd.Parameters.AddWithValue("cnt", exampleSentences.Count);
                    await using var reader = await idCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        // nextval returns bigint
                        var id64 = reader.GetInt64(0);
                        ids.Add(Convert.ToInt32(id64));
                    }
                }

                if (ids.Count != exampleSentences.Count)
                    throw new InvalidOperationException("Failed to allocate IDs for example sentences.");

                // Step 2: COPY sentences
                await using (var writer = await conn.BeginBinaryImportAsync(
                    @"COPY jiten.""ExampleSentences"" (""SentenceId"", ""Text"", ""Position"", ""DeckId"") FROM STDIN (FORMAT BINARY)"))
                {
                    var idx = 0;
                    foreach (var sentence in exampleSentences)
                    {
                        var id = ids[idx++];
                        await writer.StartRowAsync();
                        await writer.WriteAsync(id);
                        await writer.WriteAsync(sentence.Text);
                        await writer.WriteAsync(sentence.Position);
                        await writer.WriteAsync(deckId);

                        sentence.SentenceId = id;
                        sentence.DeckId = deckId;
                    }

                    await writer.CompleteAsync();
                }

                // Collect example sentence words and assign ExampleSentenceId
                var allWords = new List<ExampleSentenceWord>();
                foreach (var sentence in exampleSentences)
                {
                    foreach (var word in sentence.Words)
                    {
                        word.ExampleSentenceId = sentence.SentenceId;
                        allWords.Add(word);
                    }
                }

                // Step 3: COPY words if any
                if (allWords.Any())
                {
                    await using (var wordWriter = await conn.BeginBinaryImportAsync(
                        @"COPY jiten.""ExampleSentenceWords"" (""ExampleSentenceId"", ""WordId"", ""ReadingIndex"", ""Position"", ""Length"") FROM STDIN (FORMAT BINARY)"))
                    {
                        foreach (var w in allWords)
                        {
                            await wordWriter.StartRowAsync();
                            await wordWriter.WriteAsync(w.ExampleSentenceId);
                            await wordWriter.WriteAsync(w.WordId);
                            await wordWriter.WriteAsync(w.ReadingIndex);
                            await wordWriter.WriteAsync(w.Position);
                            await wordWriter.WriteAsync(w.Length);
                        }

                        await wordWriter.CompleteAsync();
                    }
                }
            }
        }
        finally
        {
            CopySemaphore.Release();
        }

        Console.WriteLine($"[{DateTime.UtcNow:O}] Bulk insert (example sentences+words) took {timer.ElapsedMilliseconds} ms for DeckId {deckId} with {exampleSentences.Count} sentences.");
    }

    private static async Task UpdateDeck(IDbContextFactory<JitenDbContext> contextFactory, JitenDbContext context, Deck existingDeck, Deck deck)
    {
        var deckId = existingDeck.DeckId;

        existingDeck.LastUpdate = DateTime.UtcNow;
        existingDeck.MediaType = deck.MediaType;
        existingDeck.OriginalTitle = deck.OriginalTitle;
        existingDeck.RomajiTitle = deck.RomajiTitle;
        existingDeck.EnglishTitle = deck.EnglishTitle;
        existingDeck.CharacterCount = deck.CharacterCount;
        existingDeck.WordCount = deck.WordCount;
        existingDeck.UniqueWordCount = deck.UniqueWordCount;
        existingDeck.UniqueWordUsedOnceCount = deck.UniqueWordUsedOnceCount;
        existingDeck.UniqueKanjiCount = deck.UniqueKanjiCount;
        existingDeck.UniqueKanjiUsedOnceCount = deck.UniqueKanjiUsedOnceCount;
        existingDeck.SentenceCount = deck.SentenceCount;
        existingDeck.Difficulty = deck.Difficulty;
        existingDeck.DialoguePercentage = deck.DialoguePercentage;
        existingDeck.RawText = deck.RawText;

        await context.Database.ExecuteSqlRawAsync($@"DELETE FROM jiten.""DeckWords"" WHERE ""DeckId"" = {{0}}", deckId);
        await context.Database.ExecuteSqlRawAsync($@"DELETE FROM jiten.""ExampleSentences"" WHERE ""DeckId"" = {{0}}", deckId);

        await BulkInsertDeckWords(contextFactory, deck.DeckWords, deckId);

        if (deck.ExampleSentences != null && deck.ExampleSentences.Any())
        {
            await BulkInsertExampleSentences(contextFactory, deck.ExampleSentences, deckId);
        }

        await UpdateChildDecks(contextFactory, context, existingDeck, deck.Children);
    }

    private static async Task InsertChildDecks(IDbContextFactory<JitenDbContext> contextFactory, ICollection<Deck> children, int parentDeckId)
    {
        if (children == null || !children.Any()) return;

        // Capture child data by object reference (not by DeckId which may be 0 before SaveChanges)
        var childPayloads = new List<(Deck child, List<DeckWord> words, List<ExampleSentence> sentences, DeckRawText? rawText)>();

        await using (var ctx = await contextFactory.CreateDbContextAsync())
        {
            foreach (var child in children)
            {
                child.ParentDeckId = parentDeckId;
                child.CreationDate = DateTime.UtcNow;
                child.LastUpdate = DateTimeOffset.UtcNow;

                // Detach heavy collections for EF insert and keep them in our payload list
                var dwList = child.DeckWords?.ToList() ?? new List<DeckWord>();
                child.DeckWords = new List<DeckWord>();

                var exList = child.ExampleSentences?.ToList() ?? new List<ExampleSentence>();
                if (child.ExampleSentences != null) child.ExampleSentences = new List<ExampleSentence>();

                childPayloads.Add((child, dwList, exList, child.RawText));
                ;

                // Add or update minimal metadata row
                ctx.Entry(child).State = child.DeckId == 0 ? EntityState.Added : EntityState.Modified;
            }

            var saveTimer = Stopwatch.StartNew();
            await ctx.SaveChangesAsync();
            Console.WriteLine($"[{DateTime.UtcNow:O}] Saved {children.Count} child deck metadata in {saveTimer.ElapsedMilliseconds} ms.");
        }

        // Prepare RawText entities with assigned DeckIds
        var rawTextsToUpsert = new List<DeckRawText>();
        foreach (var (child, _, _, rawText) in childPayloads)
        {
            if (rawText == null) continue;
            // Ensure the FK is the newly assigned child.DeckId
            rawText.DeckId = child.DeckId;
            rawTextsToUpsert.Add(rawText);
        }

        // Upsert RawText in one go (performs insert or update based on PK DeckId)
        if (rawTextsToUpsert.Count > 0)
        {
            await using var rawCtx = await contextFactory.CreateDbContextAsync();
            // Reduce fsync cost for this one batch insert/update
            await rawCtx.Database.ExecuteSqlRawAsync(@"SET LOCAL synchronous_commit = OFF");

            // Attach as Added when not present, Modified when present
            // If you always want to overwrite, use Upsert-like behavior:
            foreach (var rt in rawTextsToUpsert)
            {
                // Try to attach as Modified; if not exists, mark as Added
                rawCtx.Entry(rt).State = EntityState.Modified;
            }

            try
            {
                await rawCtx.SaveChangesAsync();
            }
            catch
            {
                // Fallback: if Modified failed because rows don't exist, add them
                rawCtx.ChangeTracker.Clear();
                await rawCtx.DeckRawTexts.AddRangeAsync(rawTextsToUpsert);
                await rawCtx.SaveChangesAsync();
            }
        }

        // Schedule bulk COPYs using the now-assigned DeckIds
        var bulkTasks = new List<Task>();
        foreach (var (child, words, sentences, rawText) in childPayloads)
        {
            var deckId = child.DeckId;
            // Safety: skip if EF failed to assign an ID for some reason
            if (deckId <= 0) continue;

            if (words is { Count: > 0 })
                bulkTasks.Add(BulkInsertDeckWords(contextFactory, words, deckId));

            if (sentences is { Count: > 0 })
                bulkTasks.Add(BulkInsertExampleSentences(contextFactory, sentences, deckId));
        }

        await Task.WhenAll(bulkTasks);
        Console.WriteLine($"[{DateTime.UtcNow:O}] All child bulk inserts completed.");
    }

    private static async Task UpdateChildDecks(IDbContextFactory<JitenDbContext> contextFactory, JitenDbContext context, Deck existingDeck, ICollection<Deck> children)
    {
        var existingChildren = await context.Decks
                                            .Where(d => d.ParentDeckId == existingDeck.DeckId)
                                            .ToListAsync();

        var existingChildrenDict = existingChildren.ToDictionary(c => c.OriginalTitle);

        foreach (var child in children)
        {
            var key = child.OriginalTitle;

            if (existingChildrenDict.TryGetValue(key, out var existingChild))
            {
                // Console.WriteLine("Updating child deck " + key);

                try
                {
                    await context.Entry(existingChild)
                                 .Reference(nameof(existingChild.RawText))
                                 .LoadAsync();
                }
                catch (Exception loadEx)
                {
                    Console.WriteLine($"Error explicitly loading data for child deck {key}: {loadEx.Message}");
                    continue;
                }

                await UpdateDeck(contextFactory, context, existingChild, child);
            }
            else
            {
                Console.WriteLine("Inserting new child deck " + key);

                var newChildDeck = new Deck
                                   {
                                       ParentDeckId = existingDeck.DeckId, ParentDeck = existingDeck, CreationDate = DateTime.UtcNow,
                                       LastUpdate = DateTimeOffset.UtcNow, MediaType = existingDeck.MediaType,
                                       OriginalTitle = child.OriginalTitle, RomajiTitle = child.RomajiTitle,
                                       EnglishTitle = child.EnglishTitle, DeckOrder = child.DeckOrder,
                                       CharacterCount = child.CharacterCount, UniqueWordCount = child.UniqueWordCount,
                                       UniqueKanjiCount = child.UniqueKanjiCount, SentenceCount = child.SentenceCount,
                                       WordCount = child.WordCount, Difficulty = child.Difficulty,
                                       DialoguePercentage = child.DialoguePercentage, RawText = child.RawText
                                   };

                context.Decks.Add(newChildDeck);
                await context.SaveChangesAsync(); // Save to get the ID

                await BulkInsertDeckWords(contextFactory, child.DeckWords, newChildDeck.DeckId);
            }
        }

        // Bulk delete removed children
        var childrenToDelete = existingChildren
                               .Where(ec => children.All(c => c.OriginalTitle != ec.OriginalTitle))
                               .Select(c => c.DeckId)
                               .ToList();

        if (childrenToDelete.Any())
        {
            // Delete DeckWords first (foreign key constraint)
            await context.Database.ExecuteSqlRawAsync(
                                                      $@"DELETE FROM jiten.""DeckWords"" WHERE ""DeckId"" IN ({string.Join(",", childrenToDelete.Select((_, i) => $"{{{i}}}"))})",
                                                      childrenToDelete.Cast<object>().ToArray());

            // Delete the decks
            await context.Database.ExecuteSqlRawAsync(
                                                      $@"DELETE FROM jiten.""Decks"" WHERE ""DeckId"" IN ({string.Join(",", childrenToDelete.Select((_, i) => $"{{{i}}}"))})",
                                                      childrenToDelete.Cast<object>().ToArray());

            Console.WriteLine($"Deleted {childrenToDelete.Count} child decks.");
        }
    }

    /// <summary>
    /// Computes frequency data for a specific media type, or globally if no type is specified.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="mediaType">The media type to compute frequencies for. If null, calculates global frequencies.</param>
    /// <returns>A list of JmDictWordFrequency objects, sorted by rank.</returns>
    public static async Task<List<JmDictWordFrequency>> ComputeFrequencies(IDbContextFactory<JitenDbContext> contextFactory,
                                                                           MediaType? mediaType = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        Dictionary<int, JmDictWordFrequency> wordFrequencies = new();
        var wordReadingCounts = await context.JMDictWords
                                             .AsNoTracking()
                                             .Select(w => new { w.WordId, ReadingsCount = w.Readings.Count })
                                             .ToDictionaryAsync(w => w.WordId, w => w.ReadingsCount);

        foreach (var kvp in wordReadingCounts)
        {
            wordFrequencies.Add(kvp.Key, new JmDictWordFrequency
                                         {
                                             WordId = kvp.Key, FrequencyRank = 0, UsedInMediaAmount = 0,
                                             ReadingsFrequencyPercentage = [.. new double[kvp.Value]],
                                             ReadingsObservedFrequency = [.. new double[kvp.Value]],
                                             ReadingsFrequencyRank = [.. new int[kvp.Value]],
                                             ReadingsUsedInMediaAmount = [.. new int[kvp.Value]],
                                         });
        }

        var primaryDecksQuery = context.Decks.AsNoTracking().Where(d => d.ParentDeck == null);

        if (mediaType.HasValue)
        {
            primaryDecksQuery = primaryDecksQuery.Where(d => d.MediaType == mediaType.Value);
        }

        var primaryDecks = await primaryDecksQuery.Select(d => d.DeckId).ToListAsync();

        if (!primaryDecks.Any())
        {
            // Return empty or default frequencies if no decks match
            return wordFrequencies.Values.ToList();
        }

        var wordAggregates = await context.DeckWords
                                          .Where(d => primaryDecks.Contains(d.DeckId))
                                          .GroupBy(d => d.WordId)
                                          .Select(g => new
                                                       {
                                                           WordId = g.Key, TotalOccurrences = g.Sum(dw => dw.Occurrences),
                                                           DistinctDeckCount =
                                                               g.Select(dw => dw.DeckId).Distinct().Count()
                                                       })
                                          .ToListAsync();

        foreach (var agg in wordAggregates)
        {
            if (wordFrequencies.TryGetValue(agg.WordId, out var freq))
            {
                freq.FrequencyRank = agg.TotalOccurrences; // Store raw count
                freq.UsedInMediaAmount = agg.DistinctDeckCount;
            }
        }

        var readingAggregates = await context.DeckWords
                                             .Where(d => primaryDecks.Contains(d.DeckId))
                                             .GroupBy(d => new { d.WordId, d.ReadingIndex })
                                             .Select(g => new
                                                          {
                                                              g.Key.WordId, g.Key.ReadingIndex,
                                                              TotalOccurrences = g.Sum(dw => dw.Occurrences), EntryCount = g.Count()
                                                          })
                                             .ToListAsync();

        foreach (var agg in readingAggregates)
        {
            if (wordFrequencies.TryGetValue(agg.WordId, out var freq))
            {
                freq.ReadingsFrequencyRank[agg.ReadingIndex] = agg.TotalOccurrences; // Store raw count
                freq.ReadingsUsedInMediaAmount[agg.ReadingIndex] = agg.EntryCount;
            }
        }

        // Apply logarithmic scaling + document frequency formula
        foreach (var word in wordFrequencies.Values)
        {
            word.ObservedFrequency = word.FrequencyRank;
            double score = Math.Log(1 + word.FrequencyRank) * word.UsedInMediaAmount;
            word.FrequencyRank = (int)Math.Round(score * 100);

            for (int j = 0; j < word.ReadingsFrequencyRank.Count; j++)
            {
                word.ReadingsObservedFrequency[j] = word.ReadingsFrequencyRank[j];
                double readingScore = Math.Log(1 + word.ReadingsFrequencyRank[j]) * word.ReadingsUsedInMediaAmount[j];
                word.ReadingsFrequencyRank[j] = (int)Math.Round(readingScore * 100);
            }
        }

        var sortedWordFrequencies = wordFrequencies.Values
                                                   .OrderByDescending(w => w.FrequencyRank)
                                                   .ToList();

        long totalOccurrences = sortedWordFrequencies.Sum(w => (long)w.ObservedFrequency);

        int currentRank = 0;
        int previousScore = -1;
        int rankStep = 0;

        for (int i = 0; i < sortedWordFrequencies.Count; i++)
        {
            var word = sortedWordFrequencies[i];

            int wordRawFrequencyCount = (int)word.ObservedFrequency;
            int wordScore = word.FrequencyRank;

            for (int j = 0; j < word.ReadingsObservedFrequency.Count; j++)
            {
                double readingRawCount = word.ReadingsObservedFrequency[j];

                if (totalOccurrences > 0)
                    word.ReadingsObservedFrequency[j] = readingRawCount / (double)totalOccurrences;
                else
                    word.ReadingsObservedFrequency[j] = 0;

                if (wordRawFrequencyCount > 0)
                    word.ReadingsFrequencyPercentage[j] = (readingRawCount / (double)wordRawFrequencyCount) * 100.0;
                else
                    word.ReadingsFrequencyPercentage[j] = 0;
            }

            if (totalOccurrences > 0)
                word.ObservedFrequency = wordRawFrequencyCount / (double)totalOccurrences;
            else
                word.ObservedFrequency = 0;

            if (wordScore != previousScore)
            {
                currentRank += rankStep;
                rankStep = 1;
                previousScore = wordScore;
            }
            else
            {
                rankStep++;
            }

            word.FrequencyRank = currentRank + 1;
        }

        var readingFrequencyCounts = new Dictionary<int, int>();
        foreach (var wordFreq in sortedWordFrequencies)
        {
            foreach (int readingScore in wordFreq.ReadingsFrequencyRank)
            {
                if (readingScore <= 0) continue;
                readingFrequencyCounts.TryGetValue(readingScore, out int currentCount);
                readingFrequencyCounts[readingScore] = currentCount + 1;
            }
        }

        var sortedReadingFrequencies = readingFrequencyCounts.OrderByDescending(kvp => kvp.Key).ToList();
        var readingFrequencyFinalRanks = new Dictionary<int, int>();
        currentRank = 1;
        foreach (var kvp in sortedReadingFrequencies)
        {
            readingFrequencyFinalRanks.Add(kvp.Key, currentRank);
            currentRank += kvp.Value;
        }

        int zeroReadingRank = currentRank;

        foreach (var wordFreq in sortedWordFrequencies)
        {
            for (int i = 0; i < wordFreq.ReadingsFrequencyRank.Count; i++)
            {
                int readingScore = wordFreq.ReadingsFrequencyRank[i];
                if (readingScore > 0 && readingFrequencyFinalRanks.TryGetValue(readingScore, out int finalRank))
                {
                    wordFreq.ReadingsFrequencyRank[i] = finalRank;
                }
                else
                {
                    wordFreq.ReadingsFrequencyRank[i] = zeroReadingRank;
                }
            }
        }

        return sortedWordFrequencies;
    }

    public static async Task SaveFrequenciesToDatabase(IDbContextFactory<JitenDbContext> contextFactory, List<JmDictWordFrequency> frequencies)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        Console.WriteLine("Updating database with frequencies...");

        // Delete previous frequency data if it exists
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE jmdict.\"WordFrequencies\"");

        // Bulk insert using PostgreSQL COPY command
        const string tempTable = "temp_word_frequencies";
        await using var conn = new NpgsqlConnection(context.Database.GetConnectionString());
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand($@"CREATE TEMP TABLE {tempTable} (LIKE jmdict.""WordFrequencies"" INCLUDING ALL)", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var writer =
                     await
                         conn.BeginBinaryImportAsync($@"COPY {tempTable} (""WordId"", ""FrequencyRank"", ""ObservedFrequency"", ""UsedInMediaAmount"", ""ReadingsFrequencyPercentage"", ""ReadingsObservedFrequency"", ""ReadingsFrequencyRank"", ""ReadingsUsedInMediaAmount"") FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var word in frequencies)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(word.WordId);
                await writer.WriteAsync(word.FrequencyRank);
                await writer.WriteAsync(word.ObservedFrequency);
                await writer.WriteAsync(word.UsedInMediaAmount);
                await writer.WriteAsync(word.ReadingsFrequencyPercentage, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Double);
                await writer.WriteAsync(word.ReadingsObservedFrequency, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Double);
                await writer.WriteAsync(word.ReadingsFrequencyRank, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer);
                await writer.WriteAsync(word.ReadingsUsedInMediaAmount, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer);
            }

            await writer.CompleteAsync();
        }

        await using (var cmd = new NpgsqlCommand($@"
        INSERT INTO jmdict.""WordFrequencies"" (""WordId"", ""FrequencyRank"", ""ObservedFrequency"", ""UsedInMediaAmount"", ""ReadingsFrequencyPercentage"", ""ReadingsObservedFrequency"", ""ReadingsFrequencyRank"", ""ReadingsUsedInMediaAmount"")
        SELECT ""WordId"", ""FrequencyRank"", ""ObservedFrequency"", ""UsedInMediaAmount"", ""ReadingsFrequencyPercentage"", ""ReadingsObservedFrequency"", ""ReadingsFrequencyRank"", ""ReadingsUsedInMediaAmount"" FROM {tempTable};
        DROP TABLE {tempTable};", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        Console.WriteLine("Database update complete.");
    }

    /// <summary>
    /// Computes kanji frequency data based on all deck raw texts.
    /// </summary>
    public static async Task<List<(string kanji, int rank)>> ComputeKanjiFrequencies(IDbContextFactory<JitenDbContext> contextFactory)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var kanjiStats = new Dictionary<Rune, (long Total, int LastDeckId, int UniqueDecks)>();

        int batchSize = 5000;
        int lastId = 0;

        while (true)
        {
            var batch = await context.DeckRawTexts
                                     .AsNoTracking()
                                     .Where(r => r.DeckId > lastId)
                                     .OrderBy(r => r.DeckId)
                                     .Take(batchSize)
                                     .Select(r => new { r.DeckId, r.RawText })
                                     .ToListAsync();

            if (batch.Count == 0) break;

            foreach (var entry in batch)
            {
                if (string.IsNullOrEmpty(entry.RawText)) continue;

                foreach (Rune r in entry.RawText.EnumerateRunes())
                {
                    if (!JapaneseTextHelper.IsKanji(r)) continue;

                    if (kanjiStats.TryGetValue(r, out var stats))
                    {
                        bool isNewDeck = stats.LastDeckId != entry.DeckId;
                        kanjiStats[r] = (
                            stats.Total + 1,
                            entry.DeckId,
                            isNewDeck ? stats.UniqueDecks + 1 : stats.UniqueDecks
                        );
                    }
                    else
                    {
                        kanjiStats[r] = (1, entry.DeckId, 1);
                    }
                }
                lastId = entry.DeckId;
            }
        }

        var scoredKanji = kanjiStats
                          .Select(kvp => new { Kanji = kvp.Key, Score = Math.Log(1 + kvp.Value.Total) * kvp.Value.UniqueDecks })
                          .OrderByDescending(x => x.Score)
                          .ToList();

        var result = new List<(string kanji, int rank)>();
        int currentRank = 0;
        int itemsProcessed = 0;
        double lastScore = -1;

        foreach (var item in scoredKanji)
        {
            itemsProcessed++;
            if (Math.Abs(item.Score - lastScore) > 0.0001)
            {
                currentRank = itemsProcessed;
            }

            result.Add((item.Kanji.ToString(), currentRank));
            lastScore = item.Score;
        }

        return result;
    }



    /// <summary>
    /// Get infos about a deck to debug the difficulty
    /// </summary>
    public static async Task DebugDeck(IDbContextFactory<JitenDbContext> contextFactory, int deckId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        // Retrieve the deck
        var deck = await context.Decks
                                .AsNoTracking()
                                .FirstOrDefaultAsync(d => d.DeckId == deckId);
        if (deck == null)
        {
            Console.WriteLine($"Deck with id {deckId} not found.");
            return;
        }

        Console.WriteLine("=============");
        Console.WriteLine("Deck info");
        Console.WriteLine("=============");
        Console.WriteLine("Original name: " + deck.OriginalTitle);
        Console.WriteLine("Romaji name: " + deck.RomajiTitle);
        Console.WriteLine("Character count: " + deck.CharacterCount);


        var deckWords = await context.DeckWords
                                     .AsNoTracking()
                                     .Where(dw => dw.DeckId == deckId)
                                     .ToListAsync();

        var wordIds = deckWords.Select(dw => dw.WordId)
                               .Distinct()
                               .ToList();

        var wordFrequencies = await context.JmDictWordFrequencies
                                           .AsNoTracking()
                                           .Where(wf => wordIds.Contains(wf.WordId))
                                           .ToListAsync();

        var words = await context.JMDictWords
                                 .AsNoTracking()
                                 .Where(w => wordIds.Contains(w.WordId))
                                 .ToListAsync();

        var query = new List<(JmDictWord word, JmDictWordFrequency frequency)>();

        foreach (var wf in wordFrequencies)
        {
            foreach (var w in words)
            {
                if (wf.WordId == w.WordId)
                {
                    query.Add(new(w, wf));
                }
            }
        }

        // var rarestWords = query.OrderByDescending(x => x.frequency.FrequencyRank)
        //                        .Take(100)
        //                        .ToList();
        //
        // Console.WriteLine("First 100 rarest words in deck:");
        // foreach (var item in rarestWords)
        // {
        //     Console.WriteLine(item.word.Readings[0] + " - " + item.frequency.FrequencyRank);
        // }


        Dictionary<int, int> wordFrequencyBy10k = new Dictionary<int, int>();
        foreach (var w in words)
        {
            var frequencyRank = wordFrequencies.First(wf => wf.WordId == w.WordId).FrequencyRank;
            var nRank = frequencyRank / 1000;

            if (wordFrequencyBy10k.ContainsKey(nRank))
                wordFrequencyBy10k[nRank]++;
            else
                wordFrequencyBy10k[nRank] = 1;
        }

        var list = wordFrequencyBy10k.Select(x => (x.Key, x.Value))
                                     .OrderBy(x => x.Key)
                                     .ToList();

        double totalPoints = 0;
        foreach (var wf in list)
        {
            var points = (wf.Value / (double)words.Count * 100) * Math.Exp(wf.Key / 10.0);
            totalPoints += points;
        }

        foreach (var wf in list)
        {
            var points = (wf.Value / (double)words.Count * 100) * Math.Exp(wf.Key / 10.0);

            Console.WriteLine($"Percentage of words in rank {wf.Key} : {(wf.Value / (double)words.Count * 100f).ToString("F2")}%");
            Console.WriteLine("Points: " + points + " - Percentage of total points: " + (points / totalPoints * 100f).ToString("F2") + "%");
        }

        Console.WriteLine("Total points: " + totalPoints);
    }
}