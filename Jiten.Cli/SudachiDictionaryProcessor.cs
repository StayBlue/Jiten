using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Jiten.Cli;

public class SudachiDictionaryProcessor
{
    public class SudachiLexiconRecord
    {
        // Columns 0-3
        public string Surface { get; set; } = "";
        public int LeftId { get; set; }
        public int RightId { get; set; }
        public int Cost { get; set; }

        // Columns 4-10
        public string DisplayForm { get; set; } = "";
        public string Pos1 { get; set; } = "";
        public string Pos2 { get; set; } = "";
        public string Pos3 { get; set; } = "";
        public string Pos4 { get; set; } = "";
        public string Pos5_ConjType { get; set; } = "";
        public string Pos6_ConjForm { get; set; } = "";

        // Columns 11-17
        public string Yomi { get; set; } = "";
        public string NormalizedForm { get; set; } = "";
        public string DictionaryFormWordId { get; set; } = "";
        public string SplitType { get; set; } = "";
        public string SplitInfoAUnit { get; set; } = "";
        public string SplitInfoBUnit { get; set; } = "";
        public string UnusedField { get; set; } = ""; // Column 17, explicitly handled

        // --- Helper properties, not part of the CSV ---
        public int OriginalIndex { get; set; }
        public string SourceFileName { get; set; } = "";
        public string PosString => $"{Pos1},{Pos2},{Pos3},{Pos4},{Pos5_ConjType},{Pos6_ConjForm}";
    }

    public class ConnectionIdInfo
    {
        public HashSet<int> UsedLeftIds { get; } = new HashSet<int>();
        public HashSet<int> UsedRightIds { get; } = new HashSet<int>();
        public int MaxLeftId { get; set; } = 0;
        public int MaxRightId { get; set; } = 0;
    }

    private static ConnectionIdInfo CollectConnectionIds(string filePath)
    {
        var info = new ConnectionIdInfo();
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, GetCsvConfig());

        while (csv.Read())
        {
            var leftId = csv.GetField<int>(1);
            var rightId = csv.GetField<int>(2);

            info.UsedLeftIds.Add(leftId);
            info.UsedRightIds.Add(rightId);
            info.MaxLeftId = Math.Max(info.MaxLeftId, leftId);
            info.MaxRightId = Math.Max(info.MaxRightId, rightId);
        }

        return info;
    }

    public class PruningContext
    {
        // Holds all records from all files, mapped by a unique global ID
        public Dictionary<int, SudachiLexiconRecord> AllRecords { get; } = new();

        // Maps a unique global ID to the original file name
        public Dictionary<int, string> GlobalIdToFileNameMap { get; } = new();

        // Stores the original line counts for calculating global IDs
        public Dictionary<string, int> OriginalFileSizes { get; } = new();

        // The final set of global IDs for records that will be kept
        public HashSet<int> GlobalIdsToKeep { get; } = new();

        // The final mapping from old global IDs to new sequential global IDs
        public Dictionary<int, int> OldToNewGlobalIdMap { get; } = new();

        // Helper to get original file sizes
        private int GetOriginalSmallLexCount() => OriginalFileSizes.GetValueOrDefault("small_lex.csv", 0);
        private int GetOriginalCoreLexCount() => OriginalFileSizes.GetValueOrDefault("core_lex.csv", 0);

        // Calculates a unique global ID for a record based on its source file and line number
        public int GetOriginalGlobalId(string fileName, int localIndex)
        {
            int baseOffset = 0;
            if (fileName.Contains("core_lex") && !fileName.Contains("notcore"))
            {
                baseOffset = GetOriginalSmallLexCount();
            }
            else if (fileName.Contains("notcore_lex"))
            {
                baseOffset = GetOriginalSmallLexCount() + GetOriginalCoreLexCount();
            }

            return baseOffset + localIndex;
        }
    }

    private static void PreserveAllConnectionIdDefinitions(PruningContext context)
    {
        Console.WriteLine("Finding one representative for each unique Left/Right connection ID...");
        // Use dictionaries to efficiently find the first record for each unique ID.
        // Key: Connection ID, Value: Global ID of the representative record.
        var leftIdRepresentatives = new Dictionary<int, int>();
        var rightIdRepresentatives = new Dictionary<int, int>();

        // Iterate through all records once to find representatives.
        foreach (var (globalId, record) in context.AllRecords)
        {
            if (!leftIdRepresentatives.ContainsKey(record.LeftId))
            {
                leftIdRepresentatives[record.LeftId] = globalId;
            }

            if (!rightIdRepresentatives.ContainsKey(record.RightId))
            {
                rightIdRepresentatives[record.RightId] = globalId;
            }
        }

        // Add the global IDs of these representative records to the final set to keep.
        // This forms the non-prunable "base" of the dictionary.
        int initialKeepCount = context.GlobalIdsToKeep.Count;

        foreach (var globalId in leftIdRepresentatives.Values)
        {
            context.GlobalIdsToKeep.Add(globalId);
        }

        foreach (var globalId in rightIdRepresentatives.Values)
        {
            context.GlobalIdsToKeep.Add(globalId);
        }

        int addedCount = context.GlobalIdsToKeep.Count - initialKeepCount;
        Console.WriteLine($"  └─ Preserved {addedCount} records to cover all {leftIdRepresentatives.Count} LeftIDs and {rightIdRepresentatives.Count} RightIDs.");
    }

    private static void BuildFinalGlobalWordIdMapping(PruningContext context)
    {
        // Order the kept records by their original file order and index to maintain stability
        var sortedKeptGlobalIds = context.GlobalIdsToKeep
                                         .OrderBy(id => GetFileOrder(context.GlobalIdToFileNameMap[id]))
                                         .ThenBy(id => context.AllRecords[id].OriginalIndex)
                                         .ToList();

        // Assign new, sequential global IDs
        for (int i = 0; i < sortedKeptGlobalIds.Count; i++)
        {
            int oldGlobalId = sortedKeptGlobalIds[i];
            context.OldToNewGlobalIdMap[oldGlobalId] = i;
        }
    }


    private static async Task WriteAllPrunedFiles(string[] filePaths, PruningContext context)
    {
        Console.WriteLine("Calculating new file sizes...");
        var newFileSizes = context.GlobalIdsToKeep
                                  .Select(oldId => context.GlobalIdToFileNameMap[oldId])
                                  .GroupBy(fileName => fileName)
                                  .ToDictionary(group => group.Key, group => group.Count());

        // Group all kept records by their original file name for writing
        var recordsByFile = context.GlobalIdsToKeep
                                   .Select(id => context.AllRecords[id])
                                   .GroupBy(r => r.SourceFileName);

        foreach (var group in recordsByFile)
        {
            var fileName = group.Key;
            var originalPath = filePaths.First(p => Path.GetFileName(p) == fileName);
            var outputPath = Path.Combine(Path.GetDirectoryName(originalPath) ?? "",
                                          $"{Path.GetFileNameWithoutExtension(originalPath)}_pruned.csv");

            await using var writer = new StreamWriter(outputPath);
            await using var csv = new CsvWriter(writer, GetCsvConfig());

            var recordsToWrite = group.OrderBy(r => r.OriginalIndex);


            foreach (var record in recordsToWrite)
            {
                // Before writing, fix the ID references using the final map
                record.DictionaryFormWordId = FixIdReference(record.DictionaryFormWordId, record.SourceFileName, context, newFileSizes);
                record.SplitInfoAUnit = FixSplitInfoReference(record.SplitInfoAUnit, "small_lex.csv", context, newFileSizes);
                record.SplitInfoBUnit = FixSplitInfoReference(record.SplitInfoBUnit, "small_lex.csv", context, newFileSizes);

                // (Write record to CSV, same as your original WritePrunedFile)
                csv.WriteField(record.Surface);
                csv.WriteField(record.LeftId);
                csv.WriteField(record.RightId);
                csv.WriteField(record.Cost);
                csv.WriteField(record.DisplayForm);
                csv.WriteField(record.Pos1);
                csv.WriteField(record.Pos2);
                csv.WriteField(record.Pos3);
                csv.WriteField(record.Pos4);
                csv.WriteField(record.Pos5_ConjType);
                csv.WriteField(record.Pos6_ConjForm);
                csv.WriteField(record.Yomi);
                csv.WriteField(record.NormalizedForm);
                csv.WriteField(record.DictionaryFormWordId);
                csv.WriteField(record.SplitType);
                csv.WriteField(record.SplitInfoAUnit);
                csv.WriteField(record.SplitInfoBUnit);
                csv.WriteField(record.SplitInfoAUnit);
                csv.WriteField("*");
                await csv.NextRecordAsync();
            }

            Console.WriteLine($"{fileName}: Wrote {recordsToWrite.Count()} entries.");
        }
    }

    private static string FixIdReference(string idString, string sourceFileForId, PruningContext context,
                                         Dictionary<string, int> newFileSizes)
    {
        if (string.IsNullOrEmpty(idString) || idString == "*") return "*";

        if (int.TryParse(idString, out int oldLocalId))
        {
            int oldGlobalId = context.GetOriginalGlobalId(sourceFileForId, oldLocalId);
            if (context.OldToNewGlobalIdMap.TryGetValue(oldGlobalId, out int newGlobalId))
            {
                // This part is tricky. We need to convert the new *global* ID back to a *local* ID
                // relative to the file where the new ID will be written.
                return GetNewLocalId(newGlobalId, context, newFileSizes).ToString();
            }

            return "*"; // Referenced entry was pruned
        }

        return idString; // Not a numeric ID
    }

    private static string FixSplitInfoReference(string splitInfo, string sourceFileForIds, PruningContext context,
                                                Dictionary<string, int> newFileSizes)
    {
        if (string.IsNullOrEmpty(splitInfo) || splitInfo == "*") return splitInfo;

        bool needsQuotes = splitInfo.StartsWith('"');
        var content = needsQuotes ? splitInfo[1..^1] : splitInfo;
        var fixedParts = new List<string>();

        foreach (var part in content.Split('/'))
        {
            if (part.StartsWith('U') || part.Contains(','))
            {
                fixedParts.Add(part);
                continue;
            }

            fixedParts.Add(FixIdReference(part, sourceFileForIds, context, newFileSizes));
        }

        var result = string.Join("/", fixedParts.Where(p => p != "*"));
        if (string.IsNullOrEmpty(result)) return "*";
        return needsQuotes ? $"\"{result}\"" : result;
    }

    private static int GetNewLocalId(int newGlobalId, PruningContext context, Dictionary<string, int> newFileSizes)
    {
        int smallLexSize = newFileSizes.GetValueOrDefault("small_lex.csv", 0);
        int coreLexSize = newFileSizes.GetValueOrDefault("core_lex.csv", 0);

        if (newGlobalId < smallLexSize)
        {
            return newGlobalId;
        }

        if (newGlobalId < smallLexSize + coreLexSize)
        {
            return newGlobalId - smallLexSize;
        }

        return newGlobalId - smallLexSize - coreLexSize;
    }
    
    private static int GetFileOrder(string fileName)
    {
        if (fileName.Contains("small_lex")) return 0;
        if (fileName.Contains("core_lex") && !fileName.Contains("notcore")) return 1;
        if (fileName.Contains("notcore_lex")) return 2;
        return 3;
    }

    private static Dictionary<string, int> _originalFileSizes = new Dictionary<string, int>();

    private static void RecordOriginalFileSize(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var lineCount = File.ReadLines(filePath).Count();
        _originalFileSizes[fileName] = lineCount;
    }

    private static int GetOriginalSmallLexCount()
    {
        return _originalFileSizes.GetValueOrDefault("small_lex.csv", 0);
    }

    private static int GetOriginalCoreLexCount()
    {
        return _originalFileSizes.GetValueOrDefault("core_lex.csv", 0);
    }

    public static async Task PruneAndFixSudachiCsvFiles(string folderPath, HashSet<string> allReadings)
    {
        try
        {
            var smallLexPath = Path.Combine(folderPath, "small_lex.csv");
            var coreLexPath = Path.Combine(folderPath, "core_lex.csv");
            var notCoreLexPath = Path.Combine(folderPath, "notcore_lex.csv");
            var filePaths = new[] { smallLexPath, coreLexPath, notCoreLexPath };

            var context = new PruningContext();

            // --- Pass 1: Load all records ---
            Console.WriteLine("Pass 1: Loading all dictionary records...");
            LoadAllRecords(filePaths, context);

            // --- Pass 2: Preserve the entire connection matrix structure (THE FIX) ---
            Console.WriteLine("Pass 2: Preserving connection matrix structure...");
            PreserveAllConnectionIdDefinitions(context);

            // --- Pass 3: Identify user-defined entries to keep ---
            Console.WriteLine("Pass 3: Identifying initial set of entries to keep based on readings...");
            MarkInitialEntriesToKeep(context, allReadings);

            // --- Pass 4: Recursively find all dependencies ---
            Console.WriteLine("Pass 4: Finding all required component entries...");
            ResolveComponentDependencies(context, allReadings);

            // --- Pass 5: Build the final Word ID map ---
            Console.WriteLine("Pass 5: Building final global word ID mapping...");
            BuildFinalGlobalWordIdMapping(context);

            // --- Pass 6: Write the new pruned files ---
            Console.WriteLine("Pass 6: Writing pruned dictionary files...");
            await WriteAllPrunedFiles(filePaths, context);

            Console.WriteLine("Pruning complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing Sudachi dictionaries: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void LoadAllRecords(string[] filePaths, PruningContext context)
    {
        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            context.OriginalFileSizes[fileName] = 0;

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, GetCsvConfig());

            int index = 0;
            while (csv.Read())
            {
                var record = new SudachiLexiconRecord
                             {
                                 OriginalIndex = index, SourceFileName = fileName, Surface = csv.GetField(0) ?? "", LeftId = csv.GetField<int>(1),
                                 RightId = csv.GetField<int>(2), Cost = csv.GetField<int>(3), DisplayForm = csv.GetField(4) ?? "",
                                 Pos1 = csv.GetField(5) ?? "", Pos2 = csv.GetField(6) ?? "", Pos3 = csv.GetField(7) ?? "", Pos4 = csv.GetField(8) ?? "",
                                 Pos5_ConjType = csv.GetField(9) ?? "", Pos6_ConjForm = csv.GetField(10) ?? "", Yomi = csv.GetField(11) ?? "",
                                 NormalizedForm = csv.GetField(12) ?? "", DictionaryFormWordId = csv.GetField(13) ?? "", SplitType = csv.GetField(14) ?? "",
                                 SplitInfoAUnit = csv.GetField(15) ?? "", SplitInfoBUnit = csv.GetField(16) ?? "",
                                 UnusedField = csv.TryGetField<string>(17, out var val) ? val ?? "*" : "*"
                             };


                int globalId = context.GetOriginalGlobalId(fileName, index);
                context.AllRecords[globalId] = record;
                context.GlobalIdToFileNameMap[globalId] = fileName;
                index++;
            }

            context.OriginalFileSizes[fileName] = index;
        }
    }

    private static void MarkInitialEntriesToKeep(PruningContext context, HashSet<string> allReadings)
    {
        var normalizedFormToGlobalIds = context.AllRecords.Values
                                               .Where(r => !string.IsNullOrEmpty(r.NormalizedForm) && r.NormalizedForm != "*")
                                               .GroupBy(r => r.NormalizedForm)
                                               .ToDictionary(g => g.Key,
                                                             g => g.Select(r => context.GetOriginalGlobalId(r.SourceFileName,
                                                                               r.OriginalIndex)).ToList());

        foreach (var record in context.AllRecords.Values)
        {
            bool shouldKeep = (record.Pos1 == "動詞" || record.Pos1 == "空白") ||
                              (allReadings.Contains(record.Surface) &&
                               !(record is { Pos1: "名詞", Pos2: "固有名詞", Surface.Length: > 4 }) &&
                               !(record is { Pos1: "名詞", Surface.Length: > 8 }));

            if (!shouldKeep) continue;
            int globalId = context.GetOriginalGlobalId(record.SourceFileName, record.OriginalIndex);
            context.GlobalIdsToKeep.Add(globalId);

            // Also keep all related conjugations
            if (!normalizedFormToGlobalIds.TryGetValue(record.NormalizedForm, out var conjugationIds)) continue;
            foreach (var id in conjugationIds)
            {
                context.GlobalIdsToKeep.Add(id);
            }
        }
    }

    private static void ResolveComponentDependencies(PruningContext context, HashSet<string> allReadings)
    {
        var queue = new Queue<int>(context.GlobalIdsToKeep);
        var visited = new HashSet<int>(context.GlobalIdsToKeep); // Prevents reprocessing

        while (queue.Count > 0)
        {
            var globalId = queue.Dequeue();
            if (!context.AllRecords.TryGetValue(globalId, out var record)) continue;

            var componentIds = new HashSet<int>();
            string componentSourceFile = "small_lex.csv";
            ExtractComponentIds(record.SplitInfoAUnit, componentIds);
            ExtractComponentIds(record.SplitInfoBUnit, componentIds);

            foreach (var localComponentId in componentIds)
            {
                // Convert the local component ID to its global ID
                int componentGlobalId = context.GetOriginalGlobalId(componentSourceFile, localComponentId);

                if (!visited.Add(componentGlobalId)) continue; // If it's a newly discovered component
                if (!context.AllRecords.TryGetValue(componentGlobalId, out var componentRecord)) continue;
                if (!ShouldKeepAsComponent(componentRecord.Surface, componentRecord.Pos1, componentRecord.Pos2, allReadings))
                    continue;
                context.GlobalIdsToKeep.Add(componentGlobalId);
                queue.Enqueue(componentGlobalId); // Add its dependencies to the queue
            }
        }
    }


    private static CsvConfiguration GetCsvConfig() => new(CultureInfo.InvariantCulture) { HasHeaderRecord = false };


    private static bool ShouldKeepAsComponent(string surfaceForm, string pos, string subPos1, HashSet<string> allReadings)
    {
        if (allReadings.Contains(surfaceForm))
        {
            if (pos == "名詞" && subPos1 == "固有名詞" && surfaceForm.Length > 4) return false;
            return true;
        }

        switch (pos)
        {
            case "動詞" or "空白" or "助詞" or "助動詞" or "接続詞" or "感動詞" or "接頭詞" or "接尾詞":
                return true;
            case "名詞" when subPos1 == "固有名詞":
                return surfaceForm.Length <= 2;
            case "名詞":
                return surfaceForm.Length <= 2;
            case "形容詞" or "副詞" when surfaceForm.Length <= 3:
                return true;
            default:
                return surfaceForm.Length <= 2;
        }
    }

    private static void ExtractComponentIds(string splitInfo, HashSet<int> componentIds)
    {
        if (string.IsNullOrEmpty(splitInfo) || splitInfo == "*") return;
        var content = splitInfo.StartsWith('"') && splitInfo.EndsWith('"') ? splitInfo[1..^1] : splitInfo;
        foreach (var part in content.Split('/'))
        {
            if (string.IsNullOrEmpty(part) || part.Contains(',') || part.StartsWith('U')) continue;
            if (int.TryParse(part, out var lineId)) componentIds.Add(lineId);
        }
    }
}