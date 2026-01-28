using System.Diagnostics;
using CommandLine;
using Jiten.Cli;
using Jiten.Cli.Commands;
using Jiten.Core;

// ReSharper disable MethodSupportsCancellation

public class Program
{
    static async Task Main(string[] args)
    {
        var cliContext = CliContext.Create();

        await Parser.Default.ParseArguments<CliOptions>(args)
                    .WithParsedAsync<CliOptions>(async options =>
                    {
                        var watch = Stopwatch.StartNew();

                        if (options.Threads > 1)
                        {
                            Console.WriteLine($"Using {options.Threads} threads.");
                        }

                        await ExecuteCommands(cliContext, options);

                        if (options.Verbose)
                            Console.WriteLine($"Execution time: {watch.ElapsedMilliseconds} ms");
                    });
    }

    private static async Task ExecuteCommands(CliContext context, CliOptions options)
    {
        var importCommands = new ImportCommands(context);
        var deckCommands = new DeckCommands(context);
        var extractionCommands = new ExtractionCommands();
        var diagnosticCommands = new DiagnosticCommands(context);
        var dictionaryCommands = new DictionaryCommands(context);
        var adminCommands = new AdminCommands(context);
        var mlCommands = new MlCommands(context);
        var metadataCommands = new MetadataCommands();
        var benchmarkCommands = new BenchmarkCommands(context);

        // Import commands
        if (options.Import)
        {
            await importCommands.ImportJmDict(options);
        }

        if (!string.IsNullOrEmpty(options.ImportKanjidic))
        {
            await importCommands.ImportKanjidic(options.ImportKanjidic);
        }

        if (options.PopulateWordKanji)
        {
            await importCommands.PopulateWordKanji();
        }

        if (!string.IsNullOrEmpty(options.ImportPitchAccents))
        {
            await importCommands.ImportPitchAccents(options);
        }

        if (!string.IsNullOrEmpty(options.ImportVocabularyOrigin))
        {
            await importCommands.ImportVocabularyOrigin(options);
        }

        if (!string.IsNullOrEmpty(options.SyncJMNedict))
        {
            await importCommands.SyncJMNedict(options);
        }

        if (options.CompareJMDict)
        {
            await importCommands.CompareJMDict(options);
        }

        if (options.ApplyMigrations)
        {
            await importCommands.ApplyMigrations();
        }

        // Extraction commands
        if (options.ExtractFilePath != null)
        {
            if (await extractionCommands.Extract(options)) return;
        }

        // Metadata commands
        if (options.Metadata != null)
        {
            await metadataCommands.DownloadMetadata(options);
        }

        // Deck commands
        if (options.Parse != null)
        {
            await deckCommands.Parse(options);
        }

        if (options.Insert != null)
        {
            if (await deckCommands.Insert(options)) return;
        }

        if (options.ComputeFrequencies)
        {
            await JitenHelper.ComputeFrequencies(context.ContextFactory);
        }

        if (options.DebugDeck != null)
        {
            await JitenHelper.DebugDeck(context.ContextFactory, options.DebugDeck.Value);
        }

        // Diagnostic commands
        if (options.ParseTest != null)
        {
            await diagnosticCommands.ParseTest(options);
        }

        if (options.RunParserTests)
        {
            await diagnosticCommands.RunParserTests(options);
        }

        if (!string.IsNullOrEmpty(options.SearchWord))
        {
            await diagnosticCommands.SearchWord(options.SearchWord);
        }

        if (!string.IsNullOrEmpty(options.SearchLookup))
        {
            await diagnosticCommands.SearchLookup(options.SearchLookup);
        }

        if (options.FlushRedis)
        {
            await diagnosticCommands.FlushRedisCache();
        }

        // Dictionary commands
        if (!string.IsNullOrEmpty(options.UserDicMassAdd))
        {
            if (string.IsNullOrEmpty(options.XmlPath))
            {
                Console.WriteLine("You need to specify -xml path/to/user_dic.xml");
                return;
            }

            Console.WriteLine("Importing words...");
            await dictionaryCommands.AddWordsToUserDictionary(options.UserDicMassAdd, options.XmlPath);
        }

        if (!string.IsNullOrEmpty(options.PruneSudachiCsvDirectory))
        {
            Console.WriteLine("Pruning files...");
            await dictionaryCommands.PruneSudachiCsvFiles(options.PruneSudachiCsvDirectory);
        }

        // WordSet commands
        if (options.CreateWordSetFromPos)
        {
            if (string.IsNullOrEmpty(options.SetSlug) || string.IsNullOrEmpty(options.SetName) || string.IsNullOrEmpty(options.Pos))
            {
                Console.WriteLine("For --create-wordset-from-pos, you need to specify --set-slug, --set-name, and --pos.");
                return;
            }
        }

        if (options.CreateWordSetFromCsv)
        {
            if (string.IsNullOrEmpty(options.SetSlug) || string.IsNullOrEmpty(options.SetName) || string.IsNullOrEmpty(options.CsvFile))
            {
                Console.WriteLine("For --create-wordset-from-csv, you need to specify --set-slug, --set-name, and --csv-file.");
                return;
            }

            if (!File.Exists(options.CsvFile))
            {
                Console.WriteLine($"CSV file not found: {options.CsvFile}");
                return;
            }
        }

        // Admin commands
        if (options.RegisterAdmin && !string.IsNullOrEmpty(options.Email) && !string.IsNullOrEmpty(options.Username) &&
            !string.IsNullOrEmpty(options.Password))
        {
            await adminCommands.RegisterAdmin(options.Email, options.Username, options.Password);
        }

        // ML commands
        if (!string.IsNullOrEmpty(options.ExtractFeatures))
        {
            await mlCommands.ExtractFeatures(options.ExtractFeatures);
        }

        if (!string.IsNullOrEmpty(options.ExtractFeaturesDb))
        {
            await mlCommands.ExtractFeaturesDb(options.ExtractFeaturesDb);
        }

        if (options.ExtractMorphemes)
        {
            await mlCommands.ExtractMorphemes();
        }

        if (!string.IsNullOrEmpty(options.ImportDeckDifficulty))
        {
            await mlCommands.ImportDeckDifficulty(options.ImportDeckDifficulty);
        }

        // Benchmark commands
        if (!string.IsNullOrEmpty(options.Benchmark))
        {
            await benchmarkCommands.RunBenchmark(options);
        }
    }
}
