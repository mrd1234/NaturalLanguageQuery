using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NLQueryApp.Api.Services;

public sealed class TeamMovementAnalyser
{
    public async Task AnalyseTeamMovementData()
    {
        const string directoryPath = @"C:\AiLearner\movements";
        const string outputFile = "movement_analysis_report.txt";

        if (!Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Directory not found: {directoryPath}");
            return;
        }

        var analyzer = new MovementAnalyzer();
        await analyzer.AnalyzeDirectory(directoryPath);
        analyzer.GenerateReport(outputFile);

        Console.WriteLine($"Analysis complete. Report saved to {outputFile}");
    }
    
    /// <summary>
    /// Class to track field statistics (occurrences, types, nulls, etc.)
    /// </summary>
    class FieldStats
    {
        public int TotalOccurrences { get; set; }
        public int NullOrEmptyCount { get; set; }
        public HashSet<string> UniqueTypes { get; } = new HashSet<string>();
        public HashSet<string> UniqueSampleValues { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int MaxLength { get; set; }
        public decimal? MinNumericValue { get; set; }
        public decimal? MaxNumericValue { get; set; }

        public void AddValue(JsonElement element)
        {
            TotalOccurrences++;

            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    NullOrEmptyCount++;
                    UniqueTypes.Add("null");
                    break;
                case JsonValueKind.String:
                    UniqueTypes.Add("string");
                    var value = element.GetString();
                    if (string.IsNullOrEmpty(value))
                    {
                        NullOrEmptyCount++;
                    }
                    else
                    {
                        if (value.Length > MaxLength)
                            MaxLength = value.Length;

                        // Keep track of up to 100 unique values
                        if (UniqueSampleValues.Count < 100)
                            UniqueSampleValues.Add(value);
                    }

                    break;
                case JsonValueKind.Number:
                    UniqueTypes.Add("number");
                    try
                    {
                        var numValue = element.GetDecimal();
                        MinNumericValue = MinNumericValue.HasValue
                            ? Math.Min(MinNumericValue.Value, numValue)
                            : numValue;
                        MaxNumericValue = MaxNumericValue.HasValue
                            ? Math.Max(MaxNumericValue.Value, numValue)
                            : numValue;
                    }
                    catch
                    {
                        // Handle case if the number can't be represented as decimal
                        UniqueTypes.Add("large-number");
                    }

                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    UniqueTypes.Add("boolean");
                    break;
                case JsonValueKind.Array:
                    UniqueTypes.Add("array");
                    break;
                case JsonValueKind.Object:
                    UniqueTypes.Add("object");
                    break;
            }
        }
    }

    /// <summary>
    /// Represents the schema discovered from analyzing JSON files
    /// </summary>
    class SchemaTracker
    {
        private readonly ConcurrentDictionary<string, FieldStats> _fieldStats =
            new ConcurrentDictionary<string, FieldStats>();

        private int _totalFiles = 0;

        public void TrackJsonElement(string path, JsonElement element)
        {
            // Get or create field stats
            var stats = _fieldStats.GetOrAdd(path, _ => new FieldStats());
            stats.AddValue(element);

            // If this is an object, track its properties
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    TrackJsonElement($"{path}.{property.Name}", property.Value);
                }
            }
            // If this is an array, track its items with indices
            else if (element.ValueKind == JsonValueKind.Array)
            {
                // For arrays, we'll look at the first 10 items to understand structure
                var count = Math.Min(element.GetArrayLength(), 10);
                for (var i = 0; i < count; i++)
                {
                    TrackJsonElement($"{path}[{i}]", element[i]);
                }

                // Also track general array item structure (without index)
                // This helps us understand the common structure of array items
                foreach (var item in element.EnumerateArray())
                {
                    TrackJsonElement($"{path}[]", item);
                }
            }
        }

        public void AddFile()
        {
            Interlocked.Increment(ref _totalFiles);
        }

        public int TotalFiles => _totalFiles;
        public IReadOnlyDictionary<string, FieldStats> FieldStats => _fieldStats;
    }

    /// <summary>
    /// Main analyzer class to process JSON files and generate reports
    /// </summary>
    class MovementAnalyzer
    {
        private readonly SchemaTracker _schema = new SchemaTracker();
        private readonly ConcurrentBag<string> _processingErrors = new ConcurrentBag<string>();

        private readonly ConcurrentDictionary<string, int> _movementTypeDistribution =
            new ConcurrentDictionary<string, int>();

        private readonly ConcurrentDictionary<string, int>
            _statusDistribution = new ConcurrentDictionary<string, int>();

        public async Task AnalyzeDirectory(string directoryPath)
        {
            // Find all matching files recursively
            var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json",
                SearchOption.AllDirectories);
            Console.WriteLine($"Found {files.Length} team movement files to analyze");

            // Process files in batches with parallelism
            var batchSize = 100;
            var maxParallelism = Environment.ProcessorCount * 2;
            var fileCount = 0;

            for (var i = 0; i < files.Length; i += batchSize)
            {
                var currentBatch = files.Skip(i).Take(batchSize).ToArray();

                // Process batch in parallel
                await Parallel.ForEachAsync(
                    currentBatch,
                    new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                    async (file, token) =>
                    {
                        await ProcessFile(file);
                        var processed = Interlocked.Increment(ref fileCount);
                        if (processed % 500 == 0)
                        {
                            Console.WriteLine($"Processed {processed} of {files.Length} files");
                        }
                    });
            }

            Console.WriteLine($"Processed {fileCount} files with {_processingErrors.Count} errors");
        }

        private async Task ProcessFile(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Track this file
                _schema.AddFile();

                // Start tracking from the root
                _schema.TrackJsonElement("root", root);

                // Track specific distributions
                if (root.TryGetProperty("movementType", out var movementTypeElement) &&
                    movementTypeElement.ValueKind == JsonValueKind.String)
                {
                    var movementType = movementTypeElement.GetString() ?? "null";
                    _movementTypeDistribution.AddOrUpdate(movementType, 1, (_, count) => count + 1);
                }

                if (root.TryGetProperty("status", out var statusElement) &&
                    statusElement.ValueKind == JsonValueKind.String)
                {
                    var status = statusElement.GetString() ?? "null";
                    _statusDistribution.AddOrUpdate(status, 1, (_, count) => count + 1);
                }
            }
            catch (Exception ex)
            {
                _processingErrors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        public void GenerateReport(string outputFilePath)
        {
            // Create snapshots of concurrent collections to prevent thread safety issues
            var fieldStatsSnapshot = _schema.FieldStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new {
                    TotalOccurrences = kvp.Value.TotalOccurrences,
                    NullOrEmptyCount = kvp.Value.NullOrEmptyCount,
                    UniqueTypes = kvp.Value.UniqueTypes.ToList(), // Create a copy
                    UniqueSampleValues = kvp.Value.UniqueSampleValues.ToList(), // Create a copy
                    MaxLength = kvp.Value.MaxLength,
                    MinNumericValue = kvp.Value.MinNumericValue,
                    MaxNumericValue = kvp.Value.MaxNumericValue
                }
            );
            
            var movementTypeDistSnapshot = _movementTypeDistribution.ToDictionary(k => k.Key, v => v.Value);
            var statusDistSnapshot = _statusDistribution.ToDictionary(k => k.Key, v => v.Value);
            var totalFiles = _schema.TotalFiles;
            var processingErrorsSnapshot = _processingErrors.ToList();

            var sb = new StringBuilder();

            sb.AppendLine("# Team Movement Data Analysis Report");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Files Analyzed: {totalFiles}");
            sb.AppendLine($"Processing Errors: {processingErrorsSnapshot.Count}");
            sb.AppendLine();

            // Output movement type distribution
            sb.AppendLine("## Movement Types Distribution");
            foreach (var kvp in movementTypeDistSnapshot.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value} ({(double)kvp.Value / totalFiles:P1})");
            }
            sb.AppendLine();

            // Output status distribution
            sb.AppendLine("## Status Distribution");
            foreach (var kvp in statusDistSnapshot.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value} ({(double)kvp.Value / totalFiles:P1})");
            }
            sb.AppendLine();

            // Output field stats
            sb.AppendLine("## Field Analysis");
            
            // Group fields by top-level property path for better organization
            var groupedFields = fieldStatsSnapshot
                .Select(kvp => new 
                {
                    Key = kvp.Key,
                    Value = kvp.Value,
                    Property = GetTopLevelProperty(kvp.Key)
                })
                .GroupBy(x => x.Property)
                .OrderBy(g => g.Key)
                .ToList(); // Make sure this is materialized before using

            foreach (var group in groupedFields)
            {
                sb.AppendLine($"### {group.Key}");
                sb.AppendLine();
                
                foreach (var field in group.OrderBy(f => f.Key).ToList()) // Materialize to list before iterating
                {
                    // Simplify field path for display
                    string displayPath = field.Key.Replace("root.", "");
                    
                    sb.AppendLine($"#### {displayPath}");
                    sb.AppendLine($"- Occurrence: {field.Value.TotalOccurrences} of {totalFiles} files ({(double)field.Value.TotalOccurrences / totalFiles:P1})");
                    sb.AppendLine($"- Data Types: {string.Join(", ", field.Value.UniqueTypes)}");
                    sb.AppendLine($"- Null/Empty: {field.Value.NullOrEmptyCount} ({(double)field.Value.NullOrEmptyCount / field.Value.TotalOccurrences:P1})");
                    
                    if (field.Value.UniqueTypes.Contains("string"))
                    {
                        sb.AppendLine($"- Max Length: {field.Value.MaxLength}");
                    }
                    
                    if (field.Value.UniqueTypes.Contains("number"))
                    {
                        sb.AppendLine($"- Range: {field.Value.MinNumericValue} to {field.Value.MaxNumericValue}");
                    }
                    
                    // For fields that could be lookup candidates, show sample values
                    if (ShouldShowSampleValues(displayPath, field.Value.UniqueTypes, field.Value.UniqueSampleValues))
                    {
                        sb.AppendLine("- Sample Values:");
                        int count = Math.Min(field.Value.UniqueSampleValues.Count, 10);
                        foreach (var value in field.Value.UniqueSampleValues.Take(count))
                        {
                            sb.AppendLine($"  - \"{value}\"");
                        }
                        if (field.Value.UniqueSampleValues.Count > 10)
                        {
                            sb.AppendLine($"  - ... and {field.Value.UniqueSampleValues.Count - 10} more");
                        }
                    }
                    
                    sb.AppendLine();
                }
            }

            // Output errors
            if (processingErrorsSnapshot.Count > 0)
            {
                sb.AppendLine("## Processing Errors");
                foreach (var error in processingErrorsSnapshot.Take(20))
                {
                    sb.AppendLine($"- {error}");
                }
                if (processingErrorsSnapshot.Count > 20)
                {
                    sb.AppendLine($"... and {processingErrorsSnapshot.Count - 20} more errors");
                }
                sb.AppendLine();
            }

            // Schema Recommendations Section
            sb.AppendLine("## Schema Recommendations");
            sb.AppendLine();
            sb.AppendLine("Based on the analysis, here are recommendations for improving the database schema:");
            sb.AppendLine();

            // Analyze fields with high null rates
            var highNullRateFields = fieldStatsSnapshot
                .Where(f => f.Value.TotalOccurrences > 0 && (double)f.Value.NullOrEmptyCount / f.Value.TotalOccurrences > 0.95)
                .OrderByDescending(f => (double)f.Value.NullOrEmptyCount / f.Value.TotalOccurrences)
                .ToList(); // Materialize the list

            sb.AppendLine("### Fields with High Null Rates (>95%)");
            foreach (var field in highNullRateFields.Take(20))
            {
                string displayPath = field.Key.Replace("root.", "");
                sb.AppendLine($"- {displayPath}: {(double)field.Value.NullOrEmptyCount / field.Value.TotalOccurrences:P1} null rate");
            }
            sb.AppendLine();

            // Analyze lookup tables
            sb.AppendLine("### Potential Lookup Tables");
            var lookupFieldCandidates = fieldStatsSnapshot
                .Where(f => ShouldShowSampleValues(f.Key, f.Value.UniqueTypes, f.Value.UniqueSampleValues) && 
                            f.Value.UniqueSampleValues.Count <= 30 && 
                            f.Value.UniqueSampleValues.Count > 1)
                .OrderBy(f => f.Value.UniqueSampleValues.Count)
                .ToList(); // Materialize the list

            foreach (var field in lookupFieldCandidates)
            {
                string displayPath = field.Key.Replace("root.", "");
                sb.AppendLine($"- {displayPath}: {field.Value.UniqueSampleValues.Count} unique values");
            }
            sb.AppendLine();

            // Generate SQL for default lookup values
            sb.AppendLine("### SQL for Default Lookup Values");
            sb.AppendLine("```sql");
            sb.AppendLine("-- Insert default 'Unknown' value in all lookup tables");
            sb.AppendLine("INSERT INTO lookup.movement_status (status_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.movement_type (type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.employee_group (group_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.employee_subgroup (subgroup_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.banner (banner_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.brand (brand_code, brand_name, brand_display_name) VALUES ('UNK', 'Unknown', 'Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.department (department_code, department_name) VALUES ('UNK', 'Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.cost_centre (cost_centre_code, cost_centre_name) VALUES ('UNK', 'Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.role_type (role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.job_role (job_role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.mutual_flag (flag_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.break_type (break_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("INSERT INTO lookup.history_event_type (event_type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;");
            sb.AppendLine("```");
            sb.AppendLine();

            // Implementation recommendations
            sb.AppendLine("## Implementation Recommendations");
            sb.AppendLine();
            sb.AppendLine("### Robust Error Handling");
            sb.AppendLine("1. **Handle missing required fields** - All properties like `movementId`, `status`, etc. can be missing");
            sb.AppendLine("2. **Handle null lookup values** - Default to 'Unknown' for null/empty lookup values");
            sb.AppendLine("3. **Add fallbacks for non-existent relationships** - Check for existing references before insertion");
            sb.AppendLine("4. **Transaction handling** - Use a transaction per file to ensure consistency");
            sb.AppendLine();

            sb.AppendLine("### Performance Optimizations");
            sb.AppendLine("1. **Bulk inserts** - Use `COPY` or batch inserts for better performance");
            sb.AppendLine("2. **Reduced lookups** - Cache lookup IDs to reduce database round trips");
            sb.AppendLine("3. **Lazy schema creation** - Create tables and lookup values only when needed");
            sb.AppendLine("4. **Connection pooling** - Ensure proper connection handling for parallel processing");
            sb.AppendLine();

            // Write to file
            File.WriteAllText(outputFilePath, sb.ToString());
        }

        // Helper method to safely get the top-level property from a path
        private string GetTopLevelProperty(string path)
        {
            var parts = path.Split('.');
            if (parts.Length > 1 && parts[0] == "root")
            {
                return parts[1];
            }
            
            // Handle array notation like "root.array[]"
            if (parts.Length > 1 && parts[1].Contains("["))
            {
                return parts[1].Split('[')[0];
            }
            
            // Fallback for unexpected formats
            return "Other";
        }

        // Modified version of ShouldShowSampleValues that works with the snapshots
        private bool ShouldShowSampleValues(string path, List<string> uniqueTypes, List<string> uniqueSampleValues)
        {
            // Show sample values for potential lookup fields
            if (uniqueTypes.Contains("string") && !uniqueTypes.Contains("array") && !uniqueTypes.Contains("object"))
            {
                // Fields that look like they could be lookup table candidates
                return Regex.IsMatch(path, @"(type|name|status|role|group|code|flag|banner|brand)s?\b", RegexOptions.IgnoreCase) &&
                       uniqueSampleValues.Count > 0 &&
                       uniqueSampleValues.Count <= 100; // Only show for fields with reasonable number of unique values
            }
            return false;
        }

        private bool ShouldShowSampleValues(string path, FieldStats stats)
        {
            // Show sample values for potential lookup fields
            if (stats.UniqueTypes.Contains("string") && !stats.UniqueTypes.Contains("array") &&
                !stats.UniqueTypes.Contains("object"))
            {
                // Fields that look like they could be lookup table candidates
                return Regex.IsMatch(path, @"(type|name|status|role|group|code|flag|banner|brand)s?\b",
                           RegexOptions.IgnoreCase) &&
                       stats.UniqueSampleValues.Count > 0 &&
                       stats.UniqueSampleValues.Count <=
                       100; // Only show for fields with reasonable number of unique values
            }

            return false;
        }
    }
}