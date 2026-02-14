using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Core.Data.Providers;
using Microsoft.EntityFrameworkCore;

namespace Jiten.Cli.Commands;

public class DeckCommands(CliContext context)
{
    private static readonly List<string> SubtitleCleanStartsWith =
    [
        "---", "本字幕由", "更多中日", "本整理", "压制", "日听",
        "校对", "时轴", "台本整理", "听翻", "翻译", "ED",
        "OP", "字幕", "诸神", "负责", "阿里", "日校",
        "翻译", "校对", "片源", "◎", "m"
    ];

    public async Task<bool> Insert(CliOptions options)
    {
        var directories = Directory.GetDirectories(options.Insert!).ToList();
        int directoryCount = directories.Count;

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            ReferenceHandler = ReferenceHandler.Preserve
        };

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.Threads };

        await Parallel.ForEachAsync(directories, parallelOptions, async (directory, _) =>
        {
            if (!File.Exists(Path.Combine(directory, "deck.json")))
            {
                if (options.Verbose)
                    Console.WriteLine($"No deck found in {directory}, skipping.");
                return;
            }

            if (options.Verbose)
            {
                Console.WriteLine("=========================================");
                Console.WriteLine($"Processing directory {directory} ({directories.IndexOf(directory) + 1}/{directoryCount}) [{(directories.IndexOf(directory) + 1) * 100 / directoryCount}%]");
                Console.WriteLine("=========================================");
            }

            var deck = JsonSerializer.Deserialize<Deck>(await File.ReadAllTextAsync(Path.Combine(directory, "deck.json")),
                                                        serializerOptions);
            if (deck == null) return;

            using var coverOptimized = new ImageMagick.MagickImage(Path.Combine(directory, "cover.jpg"));

            coverOptimized.Resize(400, 400);
            coverOptimized.Strip();
            coverOptimized.Quality = 85;
            coverOptimized.Format = ImageMagick.MagickFormat.Jpeg;

            await JitenHelper.InsertDeck(context.ContextFactory, deck, coverOptimized.ToByteArray(), options.UpdateDecks);

            if (options.Verbose)
                Console.WriteLine($"Deck {deck.OriginalTitle} inserted into the database.");
        });
        return false;
    }

    public async Task Parse(CliOptions options)
    {
        if (options.DeckType == null || !Enum.TryParse(options.DeckType, out MediaType deckType))
        {
            Console.WriteLine("Please specify a deck type for the parser. Available types:");
            foreach (var type in Enum.GetNames(typeof(MediaType)))
            {
                Console.WriteLine(type);
            }

            return;
        }

        if (options.Parse == null)
            return;

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            ReferenceHandler = ReferenceHandler.Preserve
        };

        var directories = Directory.GetDirectories(options.Parse).ToList();
        int directoryCount = directories.Count;

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.Threads };

        await Parallel.ForEachAsync(directories, parallelOptions, async (directory, _) =>
        {
            if (File.Exists(Path.Combine(directory, "deck.json")))
            {
                if (options.Verbose)
                    Console.WriteLine($"Deck already exists in {directory}, skipping.");
                return;
            }

            if (!File.Exists(Path.Combine(directory, "metadata.json")))
            {
                if (options.Verbose)
                    Console.WriteLine($"No metadata found in {directory}, skipping.");
                return;
            }

            if (options.Verbose)
            {
                Console.WriteLine("=========================================");
                Console.WriteLine($"Processing directory {directory} ({directories.IndexOf(directory) + 1}/{directoryCount}) [{(directories.IndexOf(directory) + 1) * 100 / directoryCount}%]");
                Console.WriteLine("=========================================");
            }

            var metadata = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(Path.Combine(directory, "metadata.json")));
            if (metadata == null) return;

            var baseDeck = await ProcessMetadata(directory, metadata, null, options, deckType, 0);
            if (baseDeck == null)
            {
                Console.WriteLine("ERROR: BASE DECK RETURNED NULL");
                return;
            }

            baseDeck.MediaType = deckType;
            baseDeck.OriginalTitle = metadata.OriginalTitle;
            baseDeck.RomajiTitle = metadata.RomajiTitle;
            baseDeck.EnglishTitle = metadata.EnglishTitle;
            baseDeck.Links = metadata.Links;
            baseDeck.CoverName = metadata.Image ?? "nocover.jpg";

            foreach (var link in baseDeck.Links)
            {
                link.Deck = baseDeck;
            }

            await File.WriteAllTextAsync(Path.Combine(directory, "deck.json"), JsonSerializer.Serialize(baseDeck, serializerOptions));

            if (options.Verbose)
                Console.WriteLine($"Base deck {baseDeck.OriginalTitle} processed with {baseDeck.DeckWords.Count} words." +
                                  Environment.NewLine);
        });
    }

    private async Task<Deck?> ProcessMetadata(string directory, Metadata metadata, Deck? parentDeck, CliOptions options, MediaType deckType, int deckOrder)
    {
        Deck deck = new();
        string? filePath = metadata.FilePath;
        await using var context1 = await context.ContextFactory.CreateDbContextAsync();

        if (!string.IsNullOrEmpty(filePath))
        {
            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(directory, Path.GetFileName(filePath));
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File {filePath} not found.");
                    return null;
                }
            }

            List<string> lines = [];
            if (Path.GetExtension(filePath)?.ToLower() == ".epub")
            {
                var extractor = new EbookExtractor();
                var text = await ExtractEpub(filePath, extractor, options);

                if (string.IsNullOrEmpty(text))
                {
                    Console.WriteLine("ERROR: TEXT RETURNED EMPTY");
                    return deck;
                }

                lines = text.Split(Environment.NewLine).ToList();
            }
            else
            {
                lines = (await File.ReadAllLinesAsync(filePath)).ToList();
            }

            if (options.CleanSubtitles)
            {
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    lines[i] = lines[i].Trim();
                    if (SubtitleCleanStartsWith.Any(s => lines[i].StartsWith(s)))
                    {
                        lines.RemoveAt(i);
                        break;
                    }

                    lines[i] = Regex.Replace(lines[i], @"\((.*?)\)", "");
                    lines[i] = Regex.Replace(lines[i], @"（(.*?)）", "");

                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        lines.RemoveAt(i);
                    }
                }
            }

            deck = await Jiten.Parser.Parser.ParseTextToDeck(context.ContextFactory, string.Join(Environment.NewLine, lines), context.StoreRawText, true,
                                                             deckType);
            deck.ParentDeck = parentDeck;
            deck.DeckOrder = deckOrder;
            deck.OriginalTitle = metadata.OriginalTitle;
            deck.MediaType = deckType;

            if (deckType is MediaType.Manga or MediaType.Anime or MediaType.Movie or MediaType.Drama)
                deck.SentenceCount = 0;

            if (options.Verbose)
                Console.WriteLine($"Parsed {filePath} with {deck.DeckWords.Count} words.");
        }

        foreach (var child in metadata.Children)
        {
            var childDeck = await ProcessMetadata(directory, child, deck, options, deckType, ++deckOrder);
            if (childDeck == null)
            {
                Console.WriteLine("ERROR: CHILD DECK RETURNED NULL");
                return null;
            }

            deck.Children.Add(childDeck);
        }

        await deck.AddChildDeckWords(context1);

        return deck;
    }

    private async Task<string> ExtractEpub(string? file, EbookExtractor extractor, CliOptions options)
    {
        if (options.Verbose)
        {
            Console.WriteLine("=========================================");
            Console.WriteLine($"=== Processing {file} ===");
            Console.WriteLine("=========================================");
        }

        var extension = Path.GetExtension(file)?.ToLower();
        if (extension is ".epub" or ".txt")
        {
            var text = extension switch
            {
                ".epub" => await extractor.ExtractTextFromEbook(file),
                ".txt" => await File.ReadAllTextAsync(file!),
                _ => throw new NotSupportedException($"File extension {extension} not supported")
            };

            if (String.IsNullOrEmpty(text))
            {
                Console.WriteLine("ERROR: TEXT RETURNED EMPTY");
                return "";
            }

            return text;
        }

        return "";
    }
}
