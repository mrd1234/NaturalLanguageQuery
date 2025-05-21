using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NLQueryApp.Api.Controllers.Import;

namespace NLQueryApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImportController> _logger;

    public ImportController(IConfiguration configuration, ILogger<ImportController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("team-movements")]
    public async Task<IActionResult> ImportTeamMovements()
    {
        try
        {
            _logger.LogInformation("Team Movement Database Importer Started");
            
            // Configuration
            var directoryPath = _configuration["ImportSettings:DirectoryPath"] ?? @"C:\AiLearner\movements";
            var connectionString = _configuration.GetConnectionString("DefaultConnection") ?? 
                "Host=localhost:5432;Database=nlquery;Username=postgres;Password=postgres;Maximum Pool Size=50;Timeout=30;Command Timeout=30;";
            
            // Validate directory exists
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogError($"Directory not found: {directoryPath}");
                return BadRequest(new { success = false, message = $"Import directory not found: {directoryPath}" });
            }
            
            // Check for matching files
            var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json", 
                SearchOption.AllDirectories);
            
            _logger.LogInformation($"Found {files.Length} team movement files to import");
            
            if (files.Length == 0)
            {
                return Ok(new { success = true, message = "No files found to import", filesCount = 0 });
            }
            
            // Step 1: Analyze files
            _logger.LogInformation("Analyzing JSON files...");
            var analyzer = new SchemaAnalyzer();
            await analyzer.AnalyzeDirectory(directoryPath);
            
            // Step 2: Create schema
            _logger.LogInformation("Creating database schema...");
            var schemaCreator = new SchemaCreator(connectionString);
            await schemaCreator.CreateSchema(analyzer);
            
            // Step 3: Import data with improved error handling
            _logger.LogInformation("Importing data...");
            var importer = new DataImporter(connectionString);
            
            // Set up cancellation token with timeout protection
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromHours(2)); // 2 hour timeout for long imports
            
            try
            {
                await importer.ImportData(directoryPath, analyzer);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Import operation timed out after 2 hours");
                return StatusCode(408, new { 
                    success = false, 
                    message = "Import operation timed out after 2 hours",
                    filesFound = files.Length,
                    filesProcessed = importer.ImportedCount,
                    errors = importer.ErrorCount
                });
            }
            
            // Step 4: Verify results
            var verificationResults = await importer.VerifyImportResults();
            
            // Group warnings by type for better reporting
            var warningGroups = importer.Warnings
                .GroupBy(w => GetWarningCategory(w))
                .ToDictionary(g => g.Key, g => g.Take(5).ToList());
            
            // Group errors by type
            var errorGroups = importer.ProcessingErrors
                .GroupBy(e => GetErrorCategory(e))
                .ToDictionary(g => g.Key, g => g.Take(5).ToList());
            
            // Calculate success percentage
            double successRate = files.Length > 0 
                ? (double)(importer.ImportedCount) / files.Length * 100 
                : 0;
            
            return Ok(new {
                success = true,
                status = "Process completed",
                filesFound = files.Length,
                filesProcessed = importer.ImportedCount,
                errors = importer.ErrorCount,
                successRate = $"{successRate:F1}%",
                errorCategories = errorGroups.Keys.ToList(), 
                errorDetails = errorGroups,
                warningCount = importer.Warnings.Count,
                warningCategories = warningGroups.Keys.ToList(),
                warningSamples = warningGroups,
                databaseResults = verificationResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import process failed with critical error");
            
            // Capture inner exception details to provide better error reporting
            var innerExceptions = new List<string>();
            var current = ex.InnerException;
            while (current != null)
            {
                innerExceptions.Add($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
            }
            
            return StatusCode(500, new { 
                success = false,
                error = ex.Message, 
                innerExceptions = innerExceptions,
                stackTrace = ex.StackTrace,
                errorType = ex.GetType().Name,
                helpText = "Check database connection and schema existence. Ensure database service is running."
            });
        }
    }
    
    // Helper method to categorize errors
    private string GetErrorCategory(string errorMessage)
    {
        if (errorMessage.Contains("GetInt32"))
            return "Integer Parsing Error";
        if (errorMessage.Contains("GetDecimal") || errorMessage.Contains("GetDouble"))
            return "Decimal Parsing Error";
        if (errorMessage.Contains("baseHours"))
            return "Base Hours Field Error";
        if (errorMessage.Contains("workingDaysPerWeek"))
            return "Working Days Field Error"; 
        if (errorMessage.Contains("salary"))
            return "Salary Field Error";
        if (errorMessage.Contains("DateTime") || errorMessage.Contains("date"))
            return "Date Parsing Error";
        if (errorMessage.Contains("57P03") || errorMessage.Contains("shutting down"))
            return "Database Connection Error";
        if (errorMessage.Contains("end of the stream"))
            return "Network Connection Error";
        
        // Default category
        return "Other Error";
    }
    
    // Helper method to categorize warnings
    private string GetWarningCategory(string warningMessage)
    {
        if (warningMessage.Contains("parse time string"))
            return "Time Parsing Warning";
        if (warningMessage.Contains("parse workingDaysPerWeek"))
            return "Working Days Format Warning";
        if (warningMessage.Contains("parse baseHours"))
            return "Base Hours Format Warning";
        if (warningMessage.Contains("parse salary"))
            return "Salary Format Warning";
        if (warningMessage.Contains("import mutual flag"))
            return "Mutual Flag Warning";
        if (warningMessage.Contains("import shift"))
            return "Shift Import Warning";
        if (warningMessage.Contains("import week"))
            return "Week Import Warning";
        if (warningMessage.Contains("import break"))
            return "Break Import Warning";
        
        // Default category
        return "Other Warning";
    }
    
    // New endpoint to retry failed imports
    [HttpPost("team-movements/retry-failed")]
    public async Task<IActionResult> RetryFailedImports()
    {
        try
        {
            // Get error log directory
            string errorLogDir = "import_errors";
            if (!Directory.Exists(errorLogDir))
            {
                return BadRequest(new { 
                    success = false, 
                    message = "No error logs found to retry imports from" 
                });
            }
            
            // Find all error log files
            var errorLogFiles = Directory.GetFiles(errorLogDir, "*.log");
            if (errorLogFiles.Length == 0)
            {
                return Ok(new { 
                    success = true, 
                    message = "No failed imports found to retry" 
                });
            }
            
            // Extract original file paths from error logs
            var filesToRetry = new List<string>();
            foreach (var logFile in errorLogFiles)
            {
                try
                {
                    string content = await System.IO.File.ReadAllTextAsync(logFile);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"File: (.+?)[\r\n]");
                    if (match.Success && match.Groups.Count > 1)
                    {
                        string originalFile = match.Groups[1].Value;
                        if (System.IO.File.Exists(originalFile))
                        {
                            filesToRetry.Add(originalFile);
                        }
                    }
                }
                catch
                {
                    // Skip any logs we can't parse
                    continue;
                }
            }
            
            if (filesToRetry.Count == 0)
            {
                return Ok(new { 
                    success = true, 
                    message = "No valid files found to retry from error logs" 
                });
            }
            
            _logger.LogInformation($"Retrying import for {filesToRetry.Count} previously failed files");
            
            // Get connection string
            var connectionString = _configuration.GetConnectionString("DefaultConnection") ?? 
                "Host=localhost:5432;Database=nlquery;Username=postgres;Password=postgres;Maximum Pool Size=50;Timeout=30;Command Timeout=30;";
            
            // Create importer and retry files
            var importer = new DataImporter(connectionString);
            
            // TODO: Add implementation for targeted retry of specific files
            // This would involve modifying DataImporter to accept a list of specific files to import
            
            return Ok(new {
                success = true,
                message = "Retry functionality is planned but not yet implemented",
                filesToRetry = filesToRetry.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry imports");
            return StatusCode(500, new { 
                success = false, 
                error = ex.Message 
            });
        }
    }
    
    // New endpoint to get import statistics
    [HttpGet("team-movements/stats")]
    public async Task<IActionResult> GetImportStats()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection") ?? 
                "Host=localhost:5432;Database=nlquery;Username=postgres;Password=postgres;Maximum Pool Size=50;Timeout=30;Command Timeout=30;";
            
            // Create connection
            await using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            
            // Get movement count
            await using var cmd1 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.movements", conn);
            var movementCount = Convert.ToInt32(await cmd1.ExecuteScalarAsync() ?? 0);
            
            // Get participant count
            await using var cmd2 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.participants", conn);
            var participantCount = Convert.ToInt32(await cmd2.ExecuteScalarAsync() ?? 0);
            
            // Get job_info count
            await using var cmd3 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.job_info", conn);
            var jobInfoCount = Convert.ToInt32(await cmd3.ExecuteScalarAsync() ?? 0);
            
            // Get contract count
            await using var cmd4 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.contracts", conn);
            var contractCount = Convert.ToInt32(await cmd4.ExecuteScalarAsync() ?? 0);
            
            // Get history event count
            await using var cmd5 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.history_events", conn);
            var historyCount = Convert.ToInt32(await cmd5.ExecuteScalarAsync() ?? 0);
            
            // Get tag count
            await using var cmd6 = new Npgsql.NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.tags", conn);
            var tagCount = Convert.ToInt32(await cmd6.ExecuteScalarAsync() ?? 0);
            
            // Get error log count
            string errorLogDir = "import_errors";
            int errorLogCount = Directory.Exists(errorLogDir) 
                ? Directory.GetFiles(errorLogDir, "*.log").Length 
                : 0;
            
            return Ok(new {
                success = true,
                importStats = new {
                    movementCount,
                    participantCount,
                    jobInfoCount,
                    contractCount,
                    historyCount,
                    tagCount,
                    errorLogCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get import statistics");
            return StatusCode(500, new { 
                success = false, 
                error = ex.Message 
            });
        }
    }
}