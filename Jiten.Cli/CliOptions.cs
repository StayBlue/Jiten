using CommandLine;

namespace Jiten.Cli;

public class CliOptions
{
    [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
    public bool Verbose { get; set; }

    [Option('i', "import", Required = false, HelpText = "Import the latest JMdict file.")]
    public bool Import { get; set; }

    [Option("xml", Required = false, HelpText = "Path to the JMDict dtd_xml file.")]
    public string? XmlPath { get; set; }

    [Option("dic", Required = false, HelpText = "Path to the JMdict dictionary file.")]
    public string? DictionaryPath { get; set; }

    [Option("namedic", Required = false, HelpText = "Path to the JMNedict dictionary file.")]
    public string? NameDictionaryPath { get; set; }

    [Option("furi", Required = false, HelpText = "Path to the JMDict Furigana dictionary file.")]
    public string? FuriganaPath { get; set; }

    [Option('e', "extract", Required = false, HelpText = "Extract text from a file or a folder and all its subfolders.")]
    public string? ExtractFilePath { get; set; }

    [Option('p', "parse", Required = false, HelpText = "Parse text in directory using metadata.json.")]
    public string? Parse { get; set; }

    [Option('t', "threads", Required = false, HelpText = "Number of threads to use.")]
    public int Threads { get; set; } = 1;

    [Option('s', "script", Required = false, HelpText = "Choose an available extraction script.")]
    public string? Script { get; set; }

    [Option('o', "output", Required = false, HelpText = "Output the operation to a file.")]
    public string? Output { get; set; }

    [Option('x', "extra", Required = false, HelpText = "Extra arguments for some operations.")]
    public string? Extra { get; set; }

    [Option('m', "metadata", Required = false, HelpText = "Download metadata for a folder.")]
    public string? Metadata { get; set; }

    [Option('a', "api", Required = false, HelpText = "API to retrieve metadata from.")]
    public string? Api { get; set; }

    [Option(longName: "deck-type", Required = false, HelpText = "Type of deck for the parser.")]
    public string? DeckType { get; set; }

    [Option(longName: "clean-subtitles", Required = false, HelpText = "Clean subtitles from extra info.")]
    public bool CleanSubtitles { get; set; }

    [Option(longName: "insert", Required = false, HelpText = "Insert the parsed deck.json into the database from a directory.")]
    public string? Insert { get; set; }

    [Option(longName: "update", Required = false,
            HelpText = "Update the parsed deck.json into the database from a directory if it's more recent'.")]
    public bool UpdateDecks { get; set; }

    [Option(longName: "compute-frequencies", Required = false, HelpText = "Compute global word frequencies")]
    public bool ComputeFrequencies { get; set; }

    [Option(longName: "debug-deck", Required = false, HelpText = "Debug a deck by id")]
    public int? DebugDeck { get; set; }

    [Option(longName: "user-dic-mass-add", Required = false,
            HelpText = "Add all JMDict words that are not in the list of word of this file")]
    public string? UserDicMassAdd { get; set; }

    [Option(longName: "apply-migrations", Required = false, HelpText = "Apply migrations to the database")]
    public bool ApplyMigrations { get; set; }

    [Option(longName: "import-pitch-accents", Required = false, HelpText = "Import pitch accents from a yomitan dictinoary directory.")]
    public string? ImportPitchAccents { get; set; }

    [Option("import-vocabulary-origin", Required = false, HelpText = "Path to the VocabularyOrigin CSV file.")]
    public string? ImportVocabularyOrigin { get; set; }

    [Option(longName: "extract-features", Required = false, HelpText = "Extract features from directory for ML.")]
    public string? ExtractFeatures { get; set; }

    [Option(longName: "extract-features-db", Required = false, HelpText = "Extract features from DB for ML.")]
    public string? ExtractFeaturesDb { get; set; }

    [Option(longName: "extract-morphemes", Required = false,
            HelpText = "Extract morphemes from words in the directionary and add them to the database.")]
    public bool ExtractMorphemes { get; set; }

    [Option(longName: "register-admin", Required = false, HelpText = "Register an admin, requires --email --username --password.")]
    public bool RegisterAdmin { get; set; }

    [Option(longName: "email", Required = false, HelpText = "Email for the admin.")]
    public string? Email { get; set; }

    [Option(longName: "username", Required = false, HelpText = "Username for the admin.")]
    public string? Username { get; set; }

    [Option(longName: "password", Required = false, HelpText = "Password for the admin.")]
    public string? Password { get; set; }

    [Option(longName: "compare-jmdict", Required = false, HelpText = "Compare two JMDict XML.")]
    public bool CompareJMDict { get; set; }

    [Option(longName: "prune-sudachi", Required = false, HelpText = "Prune CSV files from sudachi directory")]
    public string? PruneSudachiCsvDirectory { get; set; }

    [Option(longName: "sync-jmnedict", Required = false, HelpText = "Sync missing JMNedict entries and update partial entries with missing readings/definitions.")]
    public string? SyncJMNedict { get; set; }

    [Option(longName: "sync-jmdict", Required = false, HelpText = "Sync JMDict metadata on WordForms and Definitions from XML source.")]
    public bool SyncJmDict { get; set; }

    [Option(longName: "dry-run", Required = false, HelpText = "Preview sync changes without applying them. Exports a report to --output or jmdict-sync-changes.txt.")]
    public bool DryRun { get; set; }

    [Option(longName: "import-kanjidic", Required = false, HelpText = "Import KANJIDIC2 kanji dictionary from XML file.")]
    public string? ImportKanjidic { get; set; }

    [Option(longName: "populate-word-kanji", Required = false, HelpText = "Populate WordKanji junction table after kanji import.")]
    public bool PopulateWordKanji { get; set; }

    [Option(longName: "parse-test", Required = false, HelpText = "Parse text with verbose diagnostic output. Use @filepath to read from file.")]
    public string? ParseTest { get; set; }

    [Option(longName: "parse-test-output", Required = false, HelpText = "Output file path for parse-test diagnostics. Defaults to stdout.")]
    public string? ParseTestOutput { get; set; }

    [Option(longName: "run-parser-tests", Required = false, HelpText = "Run all parser tests with diagnostics and fix suggestions.")]
    public bool RunParserTests { get; set; }

    [Option(longName: "run-form-tests", Required = false, HelpText = "Run form selection tests (WordId/ReadingIndex correctness from FormSelectionTests).")]
    public bool RunFormTests { get; set; }

    [Option(longName: "search-word", Required = false, HelpText = "Search for a word in JMDict by reading or WordId.")]
    public string? SearchWord { get; set; }

    [Option(longName: "search-lookup", Required = false, HelpText = "Search the lookups table for a text and show all matching WordIds.")]
    public string? SearchLookup { get; set; }

    [Option(longName: "deconjugate-test", Required = false, HelpText = "Show all deconjugation results for a word.")]
    public string? DeconjugateTest { get; set; }

    [Option(longName: "flush-redis", Required = false, HelpText = "Flush the Redis cache (clears all cached parser results).")]
    public bool FlushRedis { get; set; }

    [Option(longName: "create-wordset-from-pos", Required = false, HelpText = "Create a WordSet from words with specific Part of Speech.")]
    public bool CreateWordSetFromPos { get; set; }

    [Option(longName: "create-wordset-from-csv", Required = false, HelpText = "Create a WordSet from a CSV file containing WordId,ReadingIndex pairs.")]
    public bool CreateWordSetFromCsv { get; set; }

    [Option(longName: "set-slug", Required = false, HelpText = "URL-friendly identifier for the WordSet.")]
    public string? SetSlug { get; set; }

    [Option(longName: "set-name", Required = false, HelpText = "Display name for the WordSet.")]
    public string? SetName { get; set; }

    [Option(longName: "set-description", Required = false, HelpText = "Description for the WordSet.")]
    public string? SetDescription { get; set; }

    [Option(longName: "pos", Required = false, HelpText = "Part of Speech to filter words by (e.g., 'name', 'place', 'proper noun').")]
    public string? Pos { get; set; }

    [Option(longName: "csv-file", Required = false, HelpText = "Path to CSV file containing WordId,ReadingIndex pairs.")]
    public string? CsvFile { get; set; }

    [Option(longName: "sync-kana", Required = false, Default = true, HelpText = "Sync kana readings when adding kanji readings to WordSet (default: true).")]
    public bool SyncKana { get; set; }

    [Option(longName: "import-deck-difficulty", Required = false, HelpText = "Import precomputed deck difficulty from a directory of JSON files named [DeckId].json.")]
    public string? ImportDeckDifficulty { get; set; }

    [Option(longName: "benchmark", Required = false, HelpText = "Run benchmark on txt files in a directory.")]
    public string? Benchmark { get; set; }

    [Option(longName: "benchmark-warmup", Required = false, Default = true, HelpText = "Run a warmup parse before benchmarking (default: true).")]
    public bool BenchmarkWarmup { get; set; }
}
