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
            
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogError($"Directory not found: {directoryPath}");
                return BadRequest($"Import directory not found: {directoryPath}");
            }
            
            var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json", 
                SearchOption.AllDirectories);
            
            _logger.LogInformation($"Found {files.Length} team movement files to import");
            
            if (files.Length == 0)
            {
                return Ok(new { message = "No files found to import", filesCount = 0 });
            }
            
            // Step 1: Analyze files
            _logger.LogInformation("Analyzing JSON files...");
            var analyzer = new SchemaAnalyzer();
            await analyzer.AnalyzeDirectory(directoryPath);
            
            // Step 2: Create schema
            _logger.LogInformation("Creating database schema...");
            var schemaCreator = new SchemaCreator(connectionString);
            await schemaCreator.CreateSchema(analyzer);
            
            // Step 3: Import data with reduced parallelism
            _logger.LogInformation("Importing data...");
            var importer = new DataImporter(connectionString);
            await importer.ImportData(directoryPath, analyzer);
            
            // Step 4: Verify results
            var verificationResults = await importer.VerifyImportResults();
            
            return Ok(new {
                status = "Process completed",
                filesFound = files.Length,
                filesProcessed = importer.ImportedCount,
                errors = importer.ErrorCount,
                errorDetails = importer.ProcessingErrors.Take(10).ToList(),
                warnings = importer.Warnings.Count,
                warningsSample = importer.Warnings.Take(10).ToList(),
                databaseResults = verificationResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import process failed");
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}