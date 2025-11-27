using System.Text;
using System.Text.RegularExpressions;
using Hangfire;
using Jiten.Api.Dtos;
using Jiten.Api.Dtos.Requests;
using Jiten.Api.Jobs;
using Jiten.Cli;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.FSRS;
using Jiten.Core.Data.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Archives;
using SharpCompress.Common;
using StackExchange.Redis;
using MetadataProviderHelper = Jiten.Core.MetadataProviderHelper;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/admin")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize("RequiresAdmin")]
public partial class AdminController(
    IConfiguration config,
    HttpClient httpClient,
    IBackgroundJobClient backgroundJobs,
    JitenDbContext dbContext,
    UserDbContext userContext,
    ILogger<AdminController> logger)
    : ControllerBase
{
    private static readonly List<string> _supportedExtensions = [".ass", ".srt", ".ssa"];

    [HttpGet("search-media")]
    public async Task<IResult> SearchMedia(string provider, string query, string? author)
    {
        logger.LogInformation("Admin searching media: Provider={Provider}, Query={Query}, Author={Author}", provider, query, author);
        return Results.Ok(provider switch
        {
            "AnilistManga" => await MetadataProviderHelper.AnilistMangaSearchApi(query),
            "AnilistNovel" => await MetadataProviderHelper.AnilistNovelSearchApi(query),
            "GoogleBooks" =>
                await MetadataProviderHelper.GoogleBooksSearchApi(query + (!string.IsNullOrEmpty(author) ? $"+inauthor:{author}" : "")),
            "Vndb" => await MetadataProviderHelper.VndbSearchApi(query),
            "Igdb" => await MetadataProviderHelper.IgdbSearchApi(config["IgdbClientId"]!, config["IgdbClientSecret"]!, query),
            _ => new List<Metadata>()
        });
    }

    [HttpPost("add-deck")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(254857600)]
    public async Task<IActionResult> AddMediaDeck([FromForm] AddMediaRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            string path = Path.Join(config["StaticFilesPath"], "tmp", Guid.NewGuid().ToString());
            Directory.CreateDirectory(path);
            string? coverImagePathOrUrl = Path.Join(path, "cover.jpg");
            Metadata metadata = new()
                                {
                                    ReleaseDate = model.ReleaseDate.ToDateTime(new TimeOnly()), OriginalTitle = model.OriginalTitle.Trim(),
                                    RomajiTitle = model.RomajiTitle?.Trim(), EnglishTitle = model.EnglishTitle?.Trim(),
                                    Description = model.Description?.Trim(), Image = coverImagePathOrUrl, Links = new List<Link>(),
                                    Rating = model.Rating,
                                    Genres = model.Genres,
                                    Tags = model.Tags.Select(t => (t.Name, t.Percentage)).ToList(),
                                    IsAdultOnly = model.IsAdultOnly
                                };

            // Parse links and aliases from form data
            for (int i = 0; i < Request.Form.Keys.Count; i++)
            {
                string urlKey = $"links[{i}].url";
                string linkTypeKey = $"links[{i}].linkType";

                if (Request.Form.TryGetValue(urlKey, out var urlValue) &&
                    Request.Form.TryGetValue(linkTypeKey, out var linkTypeValue) &&
                    !string.IsNullOrEmpty(urlValue) &&
                    !string.IsNullOrEmpty(linkTypeValue) &&
                    Enum.TryParse<LinkType>(linkTypeValue, out var linkType))
                {
                    metadata.Links.Add(new Link { Url = urlValue.ToString(), LinkType = linkType });
                }

                string aliasKey = $"aliases[{i}]";
                if (Request.Form.TryGetValue(aliasKey, out var aliasValue) && !string.IsNullOrEmpty(aliasValue))
                {
                    metadata.Aliases.Add(aliasValue.ToString());
                }
            }

            if (model.CoverImage is { Length: > 0 })
            {
                await using var stream = new FileStream(coverImagePathOrUrl, FileMode.Create);
                await model.CoverImage.CopyToAsync(stream);
            }
            else
            {
                // If there's no cover image uploaded, then it should be an URL instead
                if (Request.Form.TryGetValue("coverImage", out var coverImageUrlValue) && !string.IsNullOrEmpty(coverImageUrlValue))
                {
                    var imageUrl = coverImageUrlValue.ToString();
                    try
                    {
                        var response = await httpClient.GetAsync(imageUrl);
                        response.EnsureSuccessStatusCode();

                        await using var imageStream = await response.Content.ReadAsStreamAsync();
                        await using var fileStream = new FileStream(coverImagePathOrUrl, FileMode.Create);
                        await imageStream.CopyToAsync(fileStream);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new ArgumentException($"Unable to download cover image from URL: {ex.Message}", ex);
                    }
                }
                else
                {
                    return BadRequest("No cover image or URL provided.");
                }
            }

            if (model.File is { Length: > 0 } && (model.Subdecks == null || !model.Subdecks.Any(sd => sd.File is { Length: > 0 })))
            {
                var mainFilePath = Path.Join(path, $"{Guid.NewGuid()}{Path.GetExtension(model.File.FileName)}");
                await using var stream = new FileStream(mainFilePath, FileMode.Create);
                await model.File.CopyToAsync(stream);

                metadata.FilePath = mainFilePath;
            }
            else if (model.Subdecks != null && model.Subdecks.Any(sd => sd.File is { Length: > 0 }))
            {
                metadata.Children = new List<Metadata>();

                foreach (var subdeck in model.Subdecks)
                {
                    if (subdeck.File is not { Length: > 0 }) continue;
                    var subdeckFilePath = Path.Join(path, $"{Guid.NewGuid()}{Path.GetExtension(subdeck.File.FileName)}");

                    await using var stream = new FileStream(subdeckFilePath, FileMode.Create);
                    await subdeck.File.CopyToAsync(stream);

                    var subdeckMetadata = new Metadata { OriginalTitle = subdeck.OriginalTitle, FilePath = subdeckFilePath };

                    metadata.Children.Add(subdeckMetadata);
                }
            }
            else
            {
                // Return error if no files provided
                return BadRequest("No media files provided. Please upload at least one file.");
            }

            backgroundJobs.Enqueue<ParseJob>(job => job.Parse(metadata, model.MediaType, bool.Parse(config["StoreRawText"] ?? "false")));

            logger.LogInformation("Admin added new deck: Title={Title}, MediaType={MediaType}, SubdeckCount={SubdeckCount}",
                                  model.OriginalTitle, model.MediaType, metadata.Children?.Count ?? 0);

            return Ok(new
                      {
                          Message = "Media added successfully.", Title = model.OriginalTitle, Path = path,
                          SubdeckCount = metadata.Children?.Count ?? 0
                      });
        }
        catch (ArgumentException argEx)
        {
            logger.LogWarning("Failed to add deck - invalid input: {Error}", argEx.Message);
            return BadRequest(new { Message = $"Invalid input: {argEx.Message}" });
        }
        catch (IOException ioEx)
        {
            logger.LogError(ioEx, "Failed to add deck - IO error while saving files");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred while saving files." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add deck - unexpected error");
            return StatusCode(StatusCodes.Status500InternalServerError,
                              new { Message = "An unexpected error occurred while processing your request." });
        }
    }

    [HttpGet("deck/{id}")]
    public async Task<IActionResult> GetDeck(int id)
    {
        var deck = dbContext.Decks.AsNoTracking()
                            .Include(d => d.Children)
                            .Include(d => d.Links)
                            .Include(d => d.Titles)
                            .Include(d => d.DeckGenres)
                            .Include(d => d.DeckTags)
                            .ThenInclude(dt => dt.Tag)
                            .FirstOrDefault(d => d.DeckId == id);

        if (deck == null)
            return NotFound(new { Message = $"No deck found with ID {id}." });

        var subDecks = dbContext.Decks.AsNoTracking().Where(d => d.ParentDeckId == id);

        subDecks = subDecks
            .OrderBy(dw => dw.DeckOrder);

        var mainDeckDto = new DeckDto(deck);
        List<DeckDto> subDeckDtos = new();

        foreach (var subDeck in subDecks)
            subDeckDtos.Add(new DeckDto(subDeck));

        var dto = new DeckDetailDto { MainDeck = mainDeckDto, SubDecks = subDeckDtos };

        return Ok(dto);
    }

    [HttpPost("update-deck")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(254857600)]
    public async Task<IActionResult> UpdateMediaDeck([FromForm] UpdateMediaRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var deck = await dbContext.Decks
                                  .Include(d => d.Links)
                                  .Include(d => d.RawText)
                                  .Include(d => d.Children)
                                  .ThenInclude(deck => deck.RawText)
                                  .Include(deck => deck.Titles)
                                  .Include(d => d.DeckGenres)
                                  .Include(d => d.DeckTags)
                                  .ThenInclude(dt => dt.Tag)
                                  .FirstOrDefaultAsync(d => d.DeckId == model.DeckId);

        if (deck == null)
            return NotFound(new { Message = $"No deck found with ID {model.DeckId}." });

        // Update basic properties
        deck.MediaType = model.MediaType;
        deck.OriginalTitle = model.OriginalTitle.Trim();
        deck.RomajiTitle = model.RomajiTitle?.Trim();
        deck.EnglishTitle = model.EnglishTitle?.Trim();
        deck.ReleaseDate = model.ReleaseDate;
        deck.Description = model.Description?.Trim();
        deck.DifficultyOverride = model.DifficultyOverride;
        deck.HideDialoguePercentage = model.HideDialoguePercentage;

        // Update cover image if provided
        if (model.CoverImage is { Length: > 0 })
        {
            using var memoryStream = new MemoryStream();
            await model.CoverImage.CopyToAsync(memoryStream);
            var cover = memoryStream.ToArray();

            var coverUrl = await BunnyCdnHelper.UploadFile(cover, $"{deck.DeckId}/cover.jpg");
            deck.CoverName = coverUrl;
        }

        string path = Path.Join(config["StaticFilesPath"], "tmp", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);

        // Update text if provided
        if (model.File is { Length: > 0 })
            deck.RawText!.RawText = await GetTextFromFile(model.File);


        // Update links
        if (model.Links.Any())
        {
            var existingLinkIds = deck.Links.Select(l => l.LinkId).ToHashSet();
            var newLinkIds = model.Links.Where(l => l.LinkId > 0).Select(l => l.LinkId).ToHashSet();

            // Remove links that are no longer present
            var linksToRemove = deck.Links.Where(l => !newLinkIds.Contains(l.LinkId));
            dbContext.RemoveRange(linksToRemove);

            // Update existing links and add new ones
            foreach (var link in model.Links)
            {
                if (link.LinkId > 0 && existingLinkIds.Contains(link.LinkId))
                {
                    var existingLink = deck.Links.First(l => l.LinkId == link.LinkId);
                    existingLink.Url = link.Url;
                    existingLink.LinkType = link.LinkType;
                }
                else
                {
                    deck.Links.Add(link);
                }
            }
        }

        // Update aliases
        if (model.Aliases.Any())
        {
            var newAliases = model.Aliases.Where(a => !string.IsNullOrEmpty(a)).ToHashSet();

            // Remove aliases that are no longer present
            var aliasesToRemove = deck.Titles.Where(t => t.TitleType == DeckTitleType.Alias && !newAliases.Contains(t.Title));
            dbContext.RemoveRange(aliasesToRemove);

            // Add new aliases
            foreach (var alias in model.Aliases)
            {
                var trimmed = alias.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var existingAlias = deck.Titles.FirstOrDefault(l => l.Title == trimmed);
                if (existingAlias == null)
                {
                    deck.Titles.Add(new DeckTitle { DeckId = deck.DeckId, Title = trimmed, TitleType = DeckTitleType.Alias });
                }
            }
        }

        // Update genres
        if (model.Genres != null && model.Genres.Any())
        {
            var newGenres = model.Genres.ToHashSet();
            var existingGenres = deck.DeckGenres.Select(dg => (int)dg.Genre).ToHashSet();

            // Remove genres no longer present
            var genresToRemove = deck.DeckGenres.Where(dg => !newGenres.Contains((int)dg.Genre));
            dbContext.RemoveRange(genresToRemove);

            // Add new genres
            foreach (var genreValue in model.Genres)
            {
                if (!existingGenres.Contains(genreValue))
                {
                    deck.DeckGenres.Add(new DeckGenre { DeckId = deck.DeckId, Genre = (Genre)genreValue });
                }
            }
        }
        else if (model.Genres != null)
        {
            // Clear all if empty list provided
            dbContext.RemoveRange(deck.DeckGenres);
        }

        // Update tags
        if (model.Tags != null && model.Tags.Any())
        {
            var newTagIds = model.Tags.Select(t => t.TagId).ToHashSet();
            var existingTagIds = deck.DeckTags.Select(dt => dt.TagId).ToHashSet();

            // Remove tags no longer present
            var tagsToRemove = deck.DeckTags.Where(dt => !newTagIds.Contains(dt.TagId));
            dbContext.RemoveRange(tagsToRemove);

            // Update existing and add new tags
            foreach (var tag in model.Tags)
            {
                var existingTag = deck.DeckTags.FirstOrDefault(dt => dt.TagId == tag.TagId);
                if (existingTag != null)
                {
                    existingTag.Percentage = tag.Percentage;
                }
                else
                {
                    deck.DeckTags.Add(new DeckTag { DeckId = deck.DeckId, TagId = tag.TagId, Percentage = tag.Percentage });
                }
            }
        }
        else if (model.Tags != null)
        {
            // Clear all if empty list provided
            dbContext.RemoveRange(deck.DeckTags);
        }

        // Update subdecks if provided
        if (model.Subdecks != null && model.Subdecks.Count != 0)
        {
            var existingSubdeckIds = deck.Children.Select(d => d.DeckId).ToHashSet();
            var newSubdeckIds = model.Subdecks.Where(d => d.DeckId > 0).Select(d => d.DeckId).ToHashSet();

            // Remove subdecks that are no longer present
            var subdecksToRemove = deck.Children.Where(d => !newSubdeckIds.Contains(d.DeckId));
            dbContext.RemoveRange(subdecksToRemove);

            // Update existing subdecks and add new ones 
            foreach (var subdeck in model.Subdecks)
            {
                if (subdeck.DeckId > 0 && existingSubdeckIds.Contains(subdeck.DeckId))
                {
                    var existingSubdeck = deck.Children.First(d => d.DeckId == subdeck.DeckId);
                    existingSubdeck.OriginalTitle = subdeck.OriginalTitle.Trim();
                    existingSubdeck.DeckOrder = subdeck.DeckOrder;
                    existingSubdeck.DifficultyOverride = subdeck.DifficultyOverride;

                    if (subdeck.File is { Length: > 0 })
                        existingSubdeck.RawText!.RawText = await GetTextFromFile(subdeck.File);
                }
                else
                {
                    var newDeck = new Deck
                                  {
                                      MediaType = deck.MediaType, OriginalTitle = subdeck.OriginalTitle.Trim(),
                                      DeckOrder = subdeck.DeckOrder, DifficultyOverride = subdeck.DifficultyOverride
                                  };

                    if (subdeck.File is { Length: > 0 })
                        newDeck.RawText = new DeckRawText(await GetTextFromFile(subdeck.File));

                    deck.Children.Add(newDeck);
                }
            }
        }

        deck.LastUpdate = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        if (model.Reparse)
            backgroundJobs.Enqueue<ReparseJob>(job => job.Reparse(deck.DeckId));

        logger.LogInformation("Admin updated deck: DeckId={DeckId}, Title={Title}, Reparse={Reparse}",
                              deck.DeckId, deck.OriginalTitle, model.Reparse);

        return Ok(new { Message = $"Media deck {deck.DeckId} updated successfully" });

        async Task<string> GetTextFromFile(IFormFile file)
        {
            var fileExtension = Path.GetExtension(file.FileName);
            var filePath = Path.Join(path, $"{Guid.NewGuid()}{fileExtension}");
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            stream.Close();

            string text;
            if (fileExtension == ".epub")
            {
                var extractor = new EbookExtractor();
                text = await extractor.ExtractTextFromEbook(filePath);

                if (string.IsNullOrEmpty(text))
                {
                    throw new Exception("No text found in the ebook.");
                }
            }
            else if (fileExtension == ".mokuro")
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
                text = await System.IO.File.ReadAllTextAsync(filePath);
            }

            return text;
        }
    }

    [HttpPost("reparse-media-by-type/{mediaType}")]
    public async Task<IActionResult> ReparseMediaByType(MediaType mediaType)
    {
        var mediaToReparse = await dbContext.Decks.AsNoTracking()
                                            .Where(d => d.MediaType == mediaType && d.ParentDeck == null)
                                            .ToListAsync();

        if (!mediaToReparse.Any())
            return NotFound(new { Message = $"No media found of type {mediaType}" });

        int count = 0;
        foreach (var deck in mediaToReparse)
        {
            backgroundJobs.Enqueue<ReparseJob>(job => job.Reparse(deck.DeckId));
            count++;
        }

        logger.LogInformation("Admin queued reparse for media type: MediaType={MediaType}, Count={Count}", mediaType, count);
        return Ok(new { Message = $"Reparsing {count} media items of type {mediaType}", Count = count });
    }

    [HttpPost("recompute-frequencies")]
    public IActionResult RecomputeFrequencies()
    {
        backgroundJobs.Enqueue<ComputationJob>(job => job.RecomputeFrequencies());

        logger.LogInformation("Admin queued recompute frequencies job");
        return Ok(new { Message = "Recomputing frequencies job has been queued" });
    }

    [HttpPost("recompute-coverages")]
    public async Task<IActionResult> RecomputeUserCoverages()
    {
        var userIds = await userContext.Users.AsNoTracking().Select(u => u.Id).ToListAsync();

        foreach (var userId in userIds)
            backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));

        logger.LogInformation("Admin queued recompute coverages for all users: UserCount={UserCount}", userIds.Count);
        return Ok(new { Message = "Recomputing user coverages for all users has been queued" });
    }

    [HttpPost("recompute-coverage/{userId}")]
    public IActionResult RecomputeUserCoverage(string userId)
    {
        backgroundJobs.Enqueue<ComputationJob>(job => job.ComputeUserCoverage(userId));
        logger.LogInformation("Admin queued recompute coverage for user: UserId={UserId}", userId);
        return Ok(new { Message = $"Recomputing user coverage for user {userId} has been queued" });
    }

    [HttpPost("recalculate-parent-deck-unique-counts")]
    public async Task<IActionResult> RecalculateParentDeckUniqueCounts()
    {
        var totalParentDecks = await dbContext.Decks.CountAsync(d => d.ParentDeckId == null);
        int updatedCount = 0;
        const int batchSize = 100;

        for (int offset = 0; offset < totalParentDecks; offset += batchSize)
        {
            var parentDeckIds = await dbContext.Decks
                                               .Where(d => d.ParentDeckId == null)
                                               .OrderBy(d => d.DeckId)
                                               .Skip(offset)
                                               .Take(batchSize)
                                               .Select(d => d.DeckId)
                                               .ToListAsync();

            foreach (var deckId in parentDeckIds)
            {
                var deck = await dbContext.Decks.FindAsync(deckId);
                if (deck == null) continue;

                var oldUniqueCount = deck.UniqueWordCount;
                var oldUniqueUsedOnceCount = deck.UniqueWordUsedOnceCount;

                // Calculate unique word count using database aggregation
                deck.UniqueWordCount = await dbContext.DeckWords
                                                      .Where(dw => dw.DeckId == deckId)
                                                      .Select(dw => new { dw.WordId, dw.ReadingIndex })
                                                      .Distinct()
                                                      .CountAsync();

                deck.UniqueWordUsedOnceCount = await dbContext.DeckWords
                                                              .Where(dw => dw.DeckId == deckId && dw.Occurrences == 1)
                                                              .Select(dw => new { dw.WordId, dw.ReadingIndex })
                                                              .Distinct()
                                                              .CountAsync();

                if (oldUniqueCount != deck.UniqueWordCount || oldUniqueUsedOnceCount != deck.UniqueWordUsedOnceCount)
                {
                    updatedCount++;
                    logger.LogInformation("Updated deck {DeckId} '{Title}': UniqueWordCount {Old} -> {New}, UniqueWordUsedOnceCount {OldOnce} -> {NewOnce}",
                                          deck.DeckId, deck.OriginalTitle, oldUniqueCount, deck.UniqueWordCount, oldUniqueUsedOnceCount,
                                          deck.UniqueWordUsedOnceCount);
                }
            }

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Processed batch: {Offset}-{End} of {Total}", offset, Math.Min(offset + batchSize, totalParentDecks),
                                  totalParentDecks);
        }

        logger.LogInformation("Admin recalculated parent deck unique counts: TotalParentDecks={Total}, UpdatedDecks={Updated}",
                              totalParentDecks, updatedCount);

        return Ok(new
                  {
                      Message = $"Recalculated unique word counts for {totalParentDecks} parent decks", UpdatedCount = updatedCount,
                      TotalCount = totalParentDecks
                  });
    }

    [HttpGet("issues")]
    public async Task<IActionResult> GetIssues()
    {
        var decks = await dbContext.Decks.AsNoTracking().Include(d => d.Links).Include(d => d.DeckGenres).Include(d => d.DeckTags).ToListAsync();
        var issues = new IssuesDto();

        // We always need the romaji title for ordering
        issues.MissingRomajiTitles = decks.Where(d => d.ParentDeckId == null)
                                          .Where(d => string.IsNullOrEmpty(d.RomajiTitle))
                                          .Select(d => d.DeckId).ToList();
        issues.ZeroCharacters = decks.Where(d => d.CharacterCount == 0).Select(d => d.DeckId).ToList();
        issues.MissingLinks = decks.Where(d => d.ParentDeckId == null).Where(d => d.Links.Count == 0).Select(d => d.DeckId).ToList();
        issues.MissingReleaseDate =
            decks.Where(d => d.ParentDeckId == null).Where(d => d.ReleaseDate == default).Select(d => d.DeckId).ToList();
        issues.MissingDescription = decks.Where(d => d.ParentDeckId == null).Where(d => string.IsNullOrEmpty(d.Description))
                                         .Select(d => d.DeckId).ToList();
        issues.MissingGenres = decks.Where(d => d.ParentDeckId == null)
                                    .Where(d => d.DeckGenres.Count == 0)
                                    .Select(d => d.DeckId).ToList();
        issues.MissingTags = decks.Where(d => d.ParentDeckId == null)
                                  .Where(d => d.DeckTags.Count == 0)
                                  .Select(d => d.DeckId).ToList();

        return Ok(issues);
    }

    [HttpPost("fetch-metadata/{deckId}")]
    public async Task<IActionResult> FetchMetadata(int deckId)
    {
        var deck = await dbContext.Decks.Include(d => d.Links).FirstOrDefaultAsync(d => d.DeckId == deckId);

        if (deck == null)
            return NotFound(new { Message = $"Deck {deckId} not found" });

        switch (deck.MediaType)
        {
            case MediaType.Anime or MediaType.Manga:
                backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchAnilistMissingMetadata(deckId));
                break;
            case MediaType.Drama or MediaType.Movie:
                backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchTmdbMissingMetadata(deckId));
                break;
            case MediaType.VisualNovel:
                backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchVndbMissingMetadata(deckId));
                break;
            case MediaType.VideoGame:
                backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchIgdbMissingMetadata(deckId));
                break;
            case MediaType.Novel or MediaType.NonFiction:
                if (deck.Links.Any(l => l.LinkType == LinkType.Anilist))
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchAnilistMissingMetadata(deckId));
                else
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchGoogleBooksMissingMetadata(deckId));
                break;
            default:
                return NotFound("No fetch job for this media type.");
        }

        return Ok(new { Message = $"Fetching metadata for deck {deckId}" });
    }

    [HttpPost("fetch-all-missing-metadata")]
    public async Task<IActionResult> FetchAllMissingMetadata()
    {
        var decks = await dbContext
                          .Decks.Where(d => d.ParentDeck == null)
                          .Include(deck => deck.Links).ToListAsync();

        foreach (var deck in decks)
        {
            switch (deck.MediaType)
            {
                case MediaType.Anime or MediaType.Manga:
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchAnilistMissingMetadata(deck.DeckId));
                    break;
                case MediaType.Drama or MediaType.Movie:
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchTmdbMissingMetadata(deck.DeckId));
                    break;
                case MediaType.VisualNovel:
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchVndbMissingMetadata(deck.DeckId));
                    break;
                case MediaType.VideoGame:
                    backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchIgdbMissingMetadata(deck.DeckId));
                    break;
                case MediaType.Novel or MediaType.NonFiction:
                    if (deck.Links.Any(l => l.LinkType == LinkType.Anilist))
                    {
                        backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchAnilistMissingMetadata(deck.DeckId));
                    }
                    else
                    {
                        // For google books, don't fetch if it's missing aliases or rating
                        if (deck.ReleaseDate == default || string.IsNullOrEmpty(deck.Description))
                            backgroundJobs.Enqueue<FetchMetadataJob>(job => job.FetchGoogleBooksMissingMetadata(deck.DeckId));
                    }

                    break;
                default:
                    break;
            }
        }

        return Ok(new { Message = $"Fetching missing metadata for {decks.Count} decks", Count = decks.Count });
    }

    [HttpGet("get-jimaku/{id}")]
    public async Task<IActionResult> GetJimaku(int id)
    {
        var jimakuResult = new JimakuResultDto();

        var entry = await MetadataProviderHelper.JimakuGetEntryAsync(httpClient, config["JimakuApiKey"]!, id);
        if (entry == null)
        {
            return NotFound();
        }

        jimakuResult.Entry = entry;
        jimakuResult.Files = await MetadataProviderHelper.JimakuGetFilesAsync(httpClient, config["JimakuApiKey"]!, id);

        return Ok(jimakuResult);
    }

    [HttpPost("add-jimaku-deck")]
    public async Task<IActionResult> AddJimakuDeck([FromBody] AddJimakuDeckRequest model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var entry = await MetadataProviderHelper.JimakuGetEntryAsync(httpClient, config["JimakuApiKey"]!, model.JimakuId);
            if (entry == null) return NotFound("Jimaku entry not found.");

            string path = Path.Join(config["StaticFilesPath"], "tmp", Guid.NewGuid().ToString());
            Directory.CreateDirectory(path);

            Metadata metadata;
            if (entry.Flags.Anime && entry.AnilistId.HasValue)
            {
                metadata = await MetadataProviderHelper.AnilistAnimeApi(entry.AnilistId.Value) ??
                           throw new Exception("Anilist API returned null.");
            }
            else if (entry.Flags.Movie && entry.TmdbId != null)
            {
                metadata = await MetadataProviderHelper.TmdbMovieApi(entry.TmdbId.Replace("movie:", ""), config["TmdbApiKey"]!);
                metadata.OriginalTitle = entry.JapaneseName;
                metadata.EnglishTitle = entry.EnglishName;
                metadata.RomajiTitle = entry.Name;
            }
            else if (entry.TmdbId != null)
            {
                metadata = await MetadataProviderHelper.TmdbTvApi(entry.TmdbId.Replace("tv:", ""), config["TmdbApiKey"]!);
                metadata.OriginalTitle = entry.JapaneseName;
                metadata.EnglishTitle = entry.EnglishName;
                metadata.RomajiTitle = entry.Name;
            }
            else
            {
                return BadRequest("No metadata provider found for this entry.");
            }

            if (!string.IsNullOrEmpty(metadata.Image))
            {
                var coverImagePath = Path.Join(path, "cover.jpg");
                var response = await httpClient.GetAsync(metadata.Image);
                response.EnsureSuccessStatusCode();

                await using var imageStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(coverImagePath, FileMode.Create);
                await imageStream.CopyToAsync(fileStream);
                metadata.Image = coverImagePath;
            }

            var downloadedFiles = new List<string>();
            foreach (var file in model.Files)
            {
                var filePath = Path.Join(path, file.Name);
                await MetadataProviderHelper.JimakuDownloadFileAsync(httpClient, file.Url, filePath);

                if (Path.GetExtension(filePath) is ".zip" or ".rar" or ".7z")
                {
                    using var archive = ArchiveFactory.Open(filePath);
                    foreach (var e in archive.Entries.Where(currentEntry => !currentEntry.IsDirectory &&
                                                                            _supportedExtensions
                                                                                .Contains(Path.GetExtension(currentEntry.Key))))
                    {
                        var entryPath = Path.Combine(path, Path.GetFileName(e.Key));
                        e.WriteToFile(entryPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                        downloadedFiles.Add(entryPath);
                    }
                }
                else
                {
                    downloadedFiles.Add(filePath);
                }
            }

            // Preprocess ass files for subtitle parser
            var assFiles = downloadedFiles.Where(f => Path.GetExtension(f) == ".ass").ToList();
            foreach (var assFile in assFiles)
            {
                var ssaFile = Path.ChangeExtension(assFile, ".ssa");
                var lines = await System.IO.File.ReadAllLinesAsync(assFile);
                var filteredLines = lines.Where(line => !line.TrimStart().StartsWith(";") && !line.Contains("cn")).ToList();
                await System.IO.File.WriteAllLinesAsync(ssaFile, filteredLines);
                downloadedFiles.Remove(assFile);
                downloadedFiles.Add(ssaFile);
            }

            var subtitleFiles = downloadedFiles
                                .Where(f => _supportedExtensions.Contains(Path.GetExtension(f)))
                                .ToList();

            List<string> extractedFiles = new();
            foreach (var file in subtitleFiles)
            {
                var parser = new SubtitlesParser.Classes.Parsers.SubParser();
                await using var fileStream = System.IO.File.OpenRead(file);
                var items = parser.ParseStream(fileStream, Encoding.UTF8);
                List<string> lines = items.SelectMany(it => it.PlaintextLines).ToList();
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    lines[i] = Regex.Replace(lines[i], @"\((.*?)\)", "");
                    lines[i] = Regex.Replace(lines[i], @"（(.*?)）", "");

                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        lines.RemoveAt(i);
                    }
                }

                var txtPath = Path.ChangeExtension(file, ".txt");
                await System.IO.File.WriteAllLinesAsync(txtPath, lines);
                extractedFiles.Add(txtPath);
            }

            if (extractedFiles.Count > 1)
            {
                metadata.Children = new List<Metadata>();
                for (var i = 0; i < extractedFiles.Count; i++)
                {
                    var file = extractedFiles[i];
                    metadata.Children.Add(new Metadata { FilePath = file, OriginalTitle = $"Episode {i + 1}" });
                }
            }
            else if (extractedFiles.Count == 1)
            {
                metadata.FilePath = extractedFiles.First();
            }
            else
            {
                return BadRequest("No valid subtitle files found.");
            }

            if (string.IsNullOrEmpty(metadata.OriginalTitle))
                metadata.OriginalTitle = metadata.EnglishTitle ?? metadata.RomajiTitle ?? entry.Name;

            var mediaType = entry.Flags.Anime ? MediaType.Anime : entry.Flags.Movie ? MediaType.Movie : MediaType.Drama;
            backgroundJobs.Enqueue<ParseJob>(job => job.Parse(metadata, mediaType, bool.Parse(config["StoreRawText"] ?? "false")));

            return Ok(new
                      {
                          Message = "Media added successfully.", Title = metadata.OriginalTitle, Path = path,
                          SubdeckCount = metadata.Children?.Count ?? 0
                      });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                              new { Message = "An unexpected error occurred while processing your request.", Details = ex.ToString() });
        }
    }

    [HttpPost("flush-redis-cache")]
    public async Task<IResult> FlushRedisCache()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(config.GetConnectionString("Redis")!);
        var redisDb = connection.GetDatabase();
        redisDb.Execute("FLUSHALL");

        await connection.CloseAsync();

        logger.LogWarning("Admin flushed Redis cache");
        return Results.Ok();
    }
}