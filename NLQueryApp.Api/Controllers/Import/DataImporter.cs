using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace NLQueryApp.Api.Controllers.Import;

public class DataImporter
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, int> _lookupCache = new();
    private int _importedCount = 0;
    private int _errorCount = 0;
    private readonly ConcurrentBag<string> _processingErrors = new();
    
    // New properties for improved error reporting
    public int ImportedCount => _importedCount;
    public int ErrorCount => _errorCount;
    public IReadOnlyCollection<string> ProcessingErrors => _processingErrors;
    public List<string> Warnings { get; } = new List<string>();
    
    public DataImporter(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task ImportData(string directoryPath, SchemaAnalyzer analyzer)
    {
        // Find all matching files recursively
        var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json", 
            SearchOption.AllDirectories);
        
        Console.WriteLine($"Found {files.Length} team movement files to import");
        
        // Preload lookup tables into cache for performance
        await PreloadLookups();
        
        // Process files in batches
        var batchSize = 20;
        for (var i = 0; i < files.Length; i += batchSize)
        {
            var currentBatch = files.Skip(i).Take(batchSize).ToArray();
            var maxParallelism = Environment.ProcessorCount * 2;
            
            // Import batch of files
            await Parallel.ForEachAsync(
                currentBatch,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                async (file, token) =>
                {
                    await ImportFile(file);
                });
            
            if ((i + batchSize) % 100 == 0 || (i + batchSize) >= files.Length)
            {
                Console.WriteLine($"Imported {_importedCount} of {files.Length} files with {_errorCount} errors");
            }
        }
        
        Console.WriteLine($"Import complete: {_importedCount} files imported with {_errorCount} errors");
    }
    
    public async Task<ImportResults> VerifyImportResults()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        
        var results = new ImportResults
        {
            FilesProcessed = _importedCount,
            ErrorCount = _errorCount
        };
        
        try
        {
            await using var cmd1 = new NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.movements", conn);
            var movementResult = await cmd1.ExecuteScalarAsync();
            results.MovementsCount = movementResult != null ? Convert.ToInt32(movementResult) : 0;
            
            await using var cmd2 = new NpgsqlCommand(
                "SELECT COUNT(*) FROM team_movements.participants", conn);
            var participantResult = await cmd2.ExecuteScalarAsync();
            results.ParticipantsCount = participantResult != null ? Convert.ToInt32(participantResult) : 0;
        }
        catch (Exception ex)
        {
            results.VerificationError = ex.Message;
        }
        
        return results;
    }
    
    private async Task PreloadLookups()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        
        await PreloadLookup(conn, "movement_types", "id", "type_name");
        await PreloadLookup(conn, "statuses", "id", "status_name");
        await PreloadLookup(conn, "employee_groups", "id", "group_name");
        await PreloadLookup(conn, "employee_subgroups", "id", "subgroup_name");
        await PreloadLookup(conn, "banners", "id", "banner_name");
        await PreloadLookup(conn, "brands", "id", "brand_code");
        await PreloadLookup(conn, "business_groups", "id", "group_code");
        await PreloadLookup(conn, "departments", "id", "department_code");
        await PreloadLookup(conn, "cost_centres", "id", "cost_centre_code");
        await PreloadLookup(conn, "participant_roles", "id", "role_name");
        await PreloadLookup(conn, "job_roles", "id", "role_name");
        await PreloadLookup(conn, "mutual_flags", "id", "flag_name");
        await PreloadLookup(conn, "break_types", "id", "break_name");
        await PreloadLookup(conn, "history_event_types", "id", "event_type_name");
    }
    
    private async Task PreloadLookup(NpgsqlConnection conn, string tableName, string idColumn, string valueColumn)
    {
        await using var cmd = new NpgsqlCommand($"SELECT {idColumn}, {valueColumn} FROM team_movements.{tableName}", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var value = reader.GetString(1);
            _lookupCache[$"{tableName}:{value}"] = id;
        }
    }
    
    private TimeSpan SafeParseTimeSpan(string timeString)
    {
        if (string.IsNullOrEmpty(timeString))
            return TimeSpan.Zero;
    
        try
        {
            // Standard TimeSpan format (hh:mm or hh:mm:ss)
            if (TimeSpan.TryParse(timeString, out var result))
                return result;
        
            // Try 24-hour format (military time)
            if (timeString.Length == 4 && int.TryParse(timeString, out var militaryTime))
            {
                var hours = militaryTime / 100;
                var minutes = militaryTime % 100;
            
                if (hours >= 0 && hours < 24 && minutes >= 0 && minutes < 60)
                    return new TimeSpan(hours, minutes, 0);
            }
        
            // Try DateTime format
            if (DateTime.TryParse(timeString, out var dateTime))
                return dateTime.TimeOfDay;
        
            // Try custom formats
            string[] formats = { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "H.mm", "HH.mm" };
            if (DateTime.TryParseExact(timeString, formats, null, System.Globalization.DateTimeStyles.None, out var parsedDateTime))
                return parsedDateTime.TimeOfDay;
        
            // If nothing works, log and return default
            Warnings.Add($"Could not parse time string '{timeString}', using default 00:00");
            return TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            Warnings.Add($"Error parsing time string '{timeString}': {ex.Message}");
            return TimeSpan.Zero;
        }
    }
    
    private async Task ImportFile(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            // Start a transaction with explicit isolation level
            await using var transaction = await conn.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted);
            
            try
            {
                // STEP 1: Process cost centres with location data FIRST
                // This ensures cost centres are created/updated with location data before other imports
                try
                {
                    // Process cost centres from current job info
                    if (root.TryGetProperty("currentJobInfo", out var currentJobInfoElement))
                    {
                        await ProcessAndStoreCostCentreDetails(conn, currentJobInfoElement);
                    }
                    
                    // Process cost centres from new job info
                    if (root.TryGetProperty("newJobInfo", out var newJobInfoElement))
                    {
                        await ProcessAndStoreCostCentreDetails(conn, newJobInfoElement);
                    }
                    
                    // Process cost centres from history events
                    if (root.TryGetProperty("history", out var historyElement))
                    {
                        await ProcessHistoryEventCostCentres(conn, historyElement);
                    }
                }
                catch (Exception ex)
                {
                    Warnings.Add($"Error processing cost centre location data: {ex.Message}");
                    // Continue with import even if cost centre processing fails
                }
                
                // STEP 2: Import movement (existing code)
                int movementId;
                try
                {
                    movementId = await ImportMovement(conn, root);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing movement: {ex.Message}", ex);
                }
                
                // STEP 3: Import other entities (existing code)
                try
                {
                    // Import participants
                    await ImportParticipants(conn, root, movementId);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing participants: {ex.Message}", ex);
                }
                
                try
                {
                    // Import current job info
                    await ImportJobInfo(conn, root, movementId, "currentJobInfo", true);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing currentJobInfo: {ex.Message}", ex);
                }
                
                try
                {
                    // Import new job info
                    await ImportJobInfo(conn, root, movementId, "newJobInfo", false);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing newJobInfo: {ex.Message}", ex);
                }
                
                try
                {
                    // Import current contract
                    await ImportContract(conn, root, movementId, "currentContract", true);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing currentContract: {ex.Message}", ex);
                }
                
                try
                {
                    // Import new contract
                    await ImportContract(conn, root, movementId, "newContract", false);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing newContract: {ex.Message}", ex);
                }
                
                try
                {
                    // Import history
                    await ImportHistory(conn, root, movementId);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing history: {ex.Message}", ex);
                }
                
                try
                {
                    // Import tags
                    await ImportTags(conn, root, movementId);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error importing tags: {ex.Message}", ex);
                }
                
                // Commit transaction
                await transaction.CommitAsync();
                
                // Only increment after successful commit
                Interlocked.Increment(ref _importedCount);
                Console.WriteLine($"Successfully imported: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Failed to import file: {ex.Message}", ex);
            }
        }
        catch (JsonException jex)
        {
            Interlocked.Increment(ref _errorCount);
            var fileName = Path.GetFileName(filePath);
            var movementId = ExtractMovementIdFromFileName(fileName);
            Console.WriteLine($"Error parsing JSON in file {fileName} (MovementID: {movementId}): {jex.Message}");
            
            // Log more details if needed
            LogDetailedError(filePath, "JSON_PARSE_ERROR", jex.Message, jex);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            var fileName = Path.GetFileName(filePath);
            var movementId = ExtractMovementIdFromFileName(fileName);
            
            // Unwrap nested exceptions to get to the root cause
            var innermost = ex;
            while (innermost.InnerException != null)
            {
                innermost = innermost.InnerException;
            }
            
            Console.WriteLine($"Error importing {fileName} (MovementID: {movementId}): {ex.Message}");
            Console.WriteLine($"Root cause: {innermost.Message}");
            
            // Add to the errors collection
            _processingErrors.Add($"{fileName}: {innermost.Message}");
            
            // Log the full exception details to a file for deeper analysis
            LogDetailedError(filePath, "IMPORT_ERROR", innermost.Message, ex);
        }
    }

    private string ExtractMovementIdFromFileName(string fileName)
    {
        // Filename format is typically: tms_team_movements_team_movement_[MOVEMENT_ID]_[TIMESTAMP].json
        try
        {
            var noExtension = Path.GetFileNameWithoutExtension(fileName);
            string[] parts = noExtension.Split('_');
            if (parts.Length >= 6)
            {
                return parts[5]; // The movement ID should be the 6th part (index 5)
            }
        }
        catch
        {
            // Ignore parsing errors, just return empty if we can't extract
        }
        return "";
    }

    private void LogDetailedError(string filePath, string errorType, string errorMessage, Exception ex)
    {
        try
        {
            var logDir = "import_errors";
            Directory.CreateDirectory(logDir);
            
            var fileName = Path.GetFileName(filePath);
            var logFile = Path.Combine(logDir, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            using var writer = new StreamWriter(logFile);
            writer.WriteLine($"Error Type: {errorType}");
            writer.WriteLine($"Timestamp: {DateTime.Now}");
            writer.WriteLine($"File: {filePath}");
            writer.WriteLine($"Error Message: {errorMessage}");
            writer.WriteLine();
            writer.WriteLine("Exception Details:");
            writer.WriteLine(ex.ToString());
            
            // Fixed PostgreSQL error handling
            if (ex is NpgsqlException npgsqlEx)
            {
                writer.WriteLine();
                writer.WriteLine("PostgreSQL Error Details:");
                writer.WriteLine($"Error Code: {npgsqlEx.SqlState ?? "Unknown"}");
                
                if (npgsqlEx is PostgresException pgEx)
                {
                    // PostgreSQL server errors - full detail available
                    writer.WriteLine($"Constraint: {pgEx.ConstraintName ?? "None"}");
                    writer.WriteLine($"Detail: {pgEx.Detail ?? "None"}");
                    writer.WriteLine($"Hint: {pgEx.Hint ?? "None"}");
                    writer.WriteLine($"Where: {pgEx.Where ?? "None"}");
                    writer.WriteLine($"Column: {pgEx.ColumnName ?? "None"}");
                    writer.WriteLine($"Table: {pgEx.TableName ?? "None"}");
                    writer.WriteLine($"Schema: {pgEx.SchemaName ?? "None"}");
                    writer.WriteLine($"Line: {pgEx.Line}, Position: {pgEx.Position}");
                }
                else
                {
                    // Other Npgsql errors (connection, timeout, etc.)
                    writer.WriteLine("Error Type: Connection/Client Error");
                    if (npgsqlEx.InnerException != null)
                        writer.WriteLine($"Inner Exception: {npgsqlEx.InnerException.Message}");
                    if (!string.IsNullOrEmpty(npgsqlEx.Source))
                        writer.WriteLine($"Source: {npgsqlEx.Source}");
                }
            }
            
            // Try to include a sample of the file content
            try
            {
                writer.WriteLine();
                writer.WriteLine("File Sample (first 2000 chars):");
                var content = File.ReadAllText(filePath);
                writer.WriteLine(content.Length > 2000 ? content.Substring(0, 2000) + "..." : content);
            }
            catch
            {
                writer.WriteLine("Could not read file content for logging.");
            }
        }
        catch
        {
            // If logging fails, continue - we don't want to interrupt the import process
            Console.WriteLine("Warning: Failed to write detailed error log.");
        }
    }
    
    private async Task<int> ImportMovement(NpgsqlConnection conn, JsonElement root)
    {
        var movementId = root.GetProperty("movementId").GetString();
        if (string.IsNullOrEmpty(movementId))
            throw new Exception("Movement ID is required");
        
        var employeeId = root.GetProperty("employeeId").GetString() ?? "Unknown";
        
        var movementTypeId = GetLookupId(
            "movement_types", 
            root.TryGetProperty("movementType", out var movementTypeElement) && 
            movementTypeElement.ValueKind == JsonValueKind.String
                ? movementTypeElement.GetString() ?? "Unknown"
                : "Unknown"
        );
        
        var statusId = GetLookupId(
            "statuses", 
            root.TryGetProperty("status", out var statusElement) && 
            statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString() ?? "Unknown"
                : "Unknown"
        );
        
        // Improved date parsing with better error handling
        DateTime? startDate = null;
        if (root.TryGetProperty("startDate", out var startDateElement))
        {
            startDate = SafeParseDateTime(startDateElement, "startDate");
        }
        
        DateTime? endDate = null;
        if (root.TryGetProperty("endDate", out var endDateElement))
        {
            endDate = SafeParseDateTime(endDateElement, "endDate");
        }
        
        var workflowDefinitionId = "Unknown";
        var workflowVersion = 0;
        var workflowArchived = false;
        
        if (root.TryGetProperty("workflow", out var workflowElement) && 
            workflowElement.ValueKind == JsonValueKind.Object)
        {
            if (workflowElement.TryGetProperty("definitionId", out var defIdElement) && 
                defIdElement.ValueKind == JsonValueKind.String)
            {
                workflowDefinitionId = defIdElement.GetString() ?? "Unknown";
            }
            
            // Improved version parsing with fallbacks
            if (workflowElement.TryGetProperty("version", out var versionElement))
            {
                workflowVersion = SafeParseInt32(versionElement, "workflow.version", 0) ?? default;
            }
            
            // Improved boolean parsing
            if (workflowElement.TryGetProperty("archived", out var archivedElement))
            {
                workflowArchived = SafeParseBool(archivedElement, "workflow.archived", false);
            }
        }
        
        // Check if movement already exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT id FROM team_movements.movements WHERE movement_id = @movementId", conn);
        checkCmd.Parameters.AddWithValue("@movementId", movementId);
        var existingId = await checkCmd.ExecuteScalarAsync();
        
        if (existingId != null && existingId != DBNull.Value)
        {
            // Update existing movement
            await using var updateCmd = new NpgsqlCommand(@"
                UPDATE team_movements.movements SET 
                    employee_id = @employeeId,
                    movement_type_id = @movementTypeId,
                    status_id = @statusId,
                    start_date = @startDate,
                    end_date = @endDate,
                    workflow_definition_id = @workflowDefinitionId,
                    workflow_version = @workflowVersion,
                    workflow_archived = @workflowArchived,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
            ", conn);
            updateCmd.Parameters.AddWithValue("@id", Convert.ToInt32(existingId));
            updateCmd.Parameters.AddWithValue("@employeeId", employeeId);
            updateCmd.Parameters.AddWithValue("@movementTypeId", movementTypeId);
            updateCmd.Parameters.AddWithValue("@statusId", statusId);
            updateCmd.Parameters.AddWithValue("@startDate", startDate ?? (object)DBNull.Value);
            updateCmd.Parameters.AddWithValue("@endDate", endDate ?? (object)DBNull.Value);
            updateCmd.Parameters.AddWithValue("@workflowDefinitionId", workflowDefinitionId);
            updateCmd.Parameters.AddWithValue("@workflowVersion", workflowVersion);
            updateCmd.Parameters.AddWithValue("@workflowArchived", workflowArchived);
            await updateCmd.ExecuteNonQueryAsync();
            
            return Convert.ToInt32(existingId);
        }
        else
        {
            // Insert new movement
            await using var insertCmd = new NpgsqlCommand(@"
                INSERT INTO team_movements.movements (
                    movement_id,
                    employee_id,
                    movement_type_id,
                    status_id,
                    start_date,
                    end_date,
                    workflow_definition_id,
                    workflow_version,
                    workflow_archived
                ) VALUES (
                    @movementId,
                    @employeeId,
                    @movementTypeId,
                    @statusId,
                    @startDate,
                    @endDate,
                    @workflowDefinitionId,
                    @workflowVersion,
                    @workflowArchived
                ) RETURNING id
            ", conn);
            insertCmd.Parameters.AddWithValue("@movementId", movementId);
            insertCmd.Parameters.AddWithValue("@employeeId", employeeId);
            insertCmd.Parameters.AddWithValue("@movementTypeId", movementTypeId);
            insertCmd.Parameters.AddWithValue("@statusId", statusId);
            insertCmd.Parameters.AddWithValue("@startDate", startDate ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("@endDate", endDate ?? (object)DBNull.Value);
            insertCmd.Parameters.AddWithValue("@workflowDefinitionId", workflowDefinitionId);
            insertCmd.Parameters.AddWithValue("@workflowVersion", workflowVersion);
            insertCmd.Parameters.AddWithValue("@workflowArchived", workflowArchived);
            
            var result = await insertCmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value)
                throw new Exception("Failed to insert movement and get ID");
                
            return Convert.ToInt32(result);
        }
    }
    
    private async Task ImportParticipants(NpgsqlConnection conn, JsonElement root, int movementId)
    {
        // Delete existing participants for this movement
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM team_movements.participants WHERE movement_id = @movementId", conn);
        deleteCmd.Parameters.AddWithValue("@movementId", movementId);
        await deleteCmd.ExecuteNonQueryAsync();
        
        // Import participants
        if (root.TryGetProperty("participants", out var participantsElement) && 
            participantsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var participant in participantsElement.EnumerateArray())
            {
                var employeeId = participant.TryGetProperty("employeeId", out var empIdElement) && 
                                 empIdElement.ValueKind == JsonValueKind.String
                    ? empIdElement.GetString() ?? "Unknown"
                    : "Unknown";
                
                var name = participant.TryGetProperty("name", out var nameElement) && 
                           nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? ""
                    : "";
                
                var positionId = participant.TryGetProperty("position", out var posElement) && 
                                 posElement.ValueKind == JsonValueKind.String
                    ? posElement.GetString() ?? ""
                    : "";
                
                var positionTitle = participant.TryGetProperty("positionTitle", out var posTitleElement) && 
                                    posTitleElement.ValueKind == JsonValueKind.String
                    ? posTitleElement.GetString() ?? ""
                    : "";
                
                var bannerId = GetLookupId(
                    "banners", 
                    participant.TryGetProperty("banner", out var bannerElement) && 
                    bannerElement.ValueKind == JsonValueKind.String
                        ? bannerElement.GetString() ?? "Unknown"
                        : "Unknown"
                );
                
                var brandId = GetLookupId(
                    "brands", 
                    participant.TryGetProperty("brandDisplayName", out var brandElement) && 
                    brandElement.ValueKind == JsonValueKind.String
                        ? brandElement.GetString() ?? "Unknown"
                        : "Unknown"
                );
                
                var departmentId = GetLookupId(
                    "departments", 
                    participant.TryGetProperty("payingDepartment", out var deptElement) && 
                    deptElement.ValueKind == JsonValueKind.String
                        ? deptElement.GetString() ?? "Unknown"
                        : "Unknown"
                );
                
                var costCentreId = GetLookupId(
                    "cost_centres", 
                    participant.TryGetProperty("costCentre", out var ccElement) && 
                    ccElement.ValueKind == JsonValueKind.String
                        ? ccElement.GetString() ?? "Unknown"
                        : "Unknown"
                );
                
                var roleId = GetLookupId(
                    "participant_roles", 
                    participant.TryGetProperty("role", out var roleElement) && 
                    roleElement.ValueKind == JsonValueKind.String
                        ? roleElement.GetString() ?? "Unknown"
                        : "Unknown"
                );
                
                var photoUrl = participant.TryGetProperty("photo", out var photoElement) && 
                               photoElement.ValueKind == JsonValueKind.String
                    ? photoElement.GetString() ?? ""
                    : "";
                
                await using var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO team_movements.participants (
                        movement_id,
                        employee_id,
                        name,
                        position_id,
                        position_title,
                        banner_id,
                        brand_id,
                        department_id,
                        cost_centre_id,
                        role_id,
                        photo_url
                    ) VALUES (
                        @movementId,
                        @employeeId,
                        @name,
                        @positionId,
                        @positionTitle,
                        @bannerId,
                        @brandId,
                        @departmentId,
                        @costCentreId,
                        @roleId,
                        @photoUrl
                    )
                ", conn);
                insertCmd.Parameters.AddWithValue("@movementId", movementId);
                insertCmd.Parameters.AddWithValue("@employeeId", employeeId);
                insertCmd.Parameters.AddWithValue("@name", name);
                insertCmd.Parameters.AddWithValue("@positionId", positionId);
                insertCmd.Parameters.AddWithValue("@positionTitle", positionTitle);
                insertCmd.Parameters.AddWithValue("@bannerId", bannerId);
                insertCmd.Parameters.AddWithValue("@brandId", brandId);
                insertCmd.Parameters.AddWithValue("@departmentId", departmentId);
                insertCmd.Parameters.AddWithValue("@costCentreId", costCentreId);
                insertCmd.Parameters.AddWithValue("@roleId", roleId);
                insertCmd.Parameters.AddWithValue("@photoUrl", photoUrl);
                
                await insertCmd.ExecuteNonQueryAsync();
            }
        }
    }
    
    private async Task ImportJobInfo(NpgsqlConnection conn, JsonElement root, int movementId, string jobInfoProperty, bool isCurrent)
    {
        // Delete existing job info for this movement and type
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM team_movements.job_info WHERE movement_id = @movementId AND is_current = @isCurrent", conn);
        deleteCmd.Parameters.AddWithValue("@movementId", movementId);
        deleteCmd.Parameters.AddWithValue("@isCurrent", isCurrent);
        await deleteCmd.ExecuteNonQueryAsync();
        
        // Import job info
        if (root.TryGetProperty(jobInfoProperty, out var jobInfoElement) && 
            jobInfoElement.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var cmd = new NpgsqlCommand(@"
                    INSERT INTO team_movements.job_info (
                        movement_id,
                        is_current,
                        working_days_per_week,
                        base_hours,
                        employee_group_id,
                        position_id,
                        position_title,
                        banner_id,
                        brand_id,
                        business_group_id,
                        cost_centre_id,
                        employee_subgroup_id,
                        job_role_id,
                        department_id,
                        salary_amount,
                        salary_min,
                        salary_max,
                        salary_benchmark,
                        discretionary_allowance,
                        sti_target,
                        manager_employee_id,
                        manager_name,
                        manager_position_id,
                        manager_position_title,
                        start_date,
                        end_date,
                        employee_movement_type,
                        sti_scheme,
                        pay_scale_group,
                        pay_scale_level,
                        leave_entitlement,
                        leave_entitlement_name,
                        car_eligibility
                    ) VALUES (
                        @movementId,
                        @isCurrent,
                        @workingDaysPerWeek,
                        @baseHours,
                        @employeeGroupId,
                        @positionId,
                        @positionTitle,
                        @bannerId,
                        @brandId,
                        @businessGroupId,
                        @costCentreId,
                        @employeeSubgroupId,
                        @jobRoleId,
                        @departmentId,
                        @salaryAmount,
                        @salaryMin,
                        @salaryMax,
                        @salaryBenchmark,
                        @discretionaryAllowance,
                        @stiTarget,
                        @managerEmployeeId,
                        @managerName,
                        @managerPositionId,
                        @managerPositionTitle,
                        @startDate,
                        @endDate,
                        @employeeMovementType,
                        @stiScheme,
                        @payScaleGroup,
                        @payScaleLevel,
                        @leaveEntitlement,
                        @leaveEntitlementName,
                        @carEligibility
                    )
                ", conn);
                
                // SAFER PARAMETER HANDLING
                try {
                    cmd.Parameters.AddWithValue("@movementId", movementId);
                    cmd.Parameters.AddWithValue("@isCurrent", isCurrent);
                } catch (Exception ex) {
                    throw new Exception($"Error with basic parameters: {ex.Message}", ex);
                }
                
                // Working days per week - IMPROVED TYPE CONVERSION
                try {
                    decimal? workingDaysPerWeek = null;
                    if (jobInfoElement.TryGetProperty("workingDaysPerWeek", out var wdpwElement))
                    {
                        // Log the actual type and value for debugging
                        var valueType = wdpwElement.ValueKind.ToString();
                        var rawValue = wdpwElement.ToString();
                        
                        if (wdpwElement.ValueKind == JsonValueKind.Number)
                        {
                            try {
                                // Try as decimal first - handles both integers and decimals
                                var daysDecimal = wdpwElement.GetDecimal();
                                workingDaysPerWeek = daysDecimal;
                            }
                            catch {
                                // Try as double as fallback
                                try {
                                    var daysDouble = wdpwElement.GetDouble();
                                    if (!double.IsNaN(daysDouble) && !double.IsInfinity(daysDouble))
                                    {
                                        workingDaysPerWeek = (decimal)daysDouble;
                                    }
                                }
                                catch {
                                    Warnings.Add($"Could not parse workingDaysPerWeek numeric value: '{rawValue}'");
                                }
                            }
                        }
                        else if (wdpwElement.ValueKind == JsonValueKind.String)
                        {
                            var strValue = wdpwElement.GetString() ?? "";
                            // Try as decimal first
                            if (decimal.TryParse(strValue, out var wdpwDecimal))
                            {
                                workingDaysPerWeek = wdpwDecimal;
                            }
                            // For special case values we want to convert to null
                            else if (string.IsNullOrEmpty(strValue) || 
                                     strValue.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                            {
                                workingDaysPerWeek = null;
                            }
                            else
                            {
                                Warnings.Add($"Could not parse workingDaysPerWeek string value: '{strValue}'");
                            }
                        }
                    }
                    cmd.Parameters.AddWithValue("@workingDaysPerWeek", workingDaysPerWeek ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    var valueInfo = "not found";
                    if (jobInfoElement.TryGetProperty("workingDaysPerWeek", out var w)) {
                        valueInfo = $"{w.ValueKind}: {w}";
                    }
                    throw new Exception($"Error with workingDaysPerWeek parameter: {ex.Message} | Value: {valueInfo}", ex);
                }

                // Base hours - IMPROVED TYPE CONVERSION
                try {
                    decimal? baseHours = null;
                    if (jobInfoElement.TryGetProperty("baseHours", out var bhElement))
                    {
                        var valueType = bhElement.ValueKind.ToString();
                        var rawValue = bhElement.ToString();
                        
                        if (bhElement.ValueKind == JsonValueKind.Number)
                        {
                            try {
                                // Try as decimal first - handles both integers and decimals
                                var hoursDecimal = bhElement.GetDecimal();
                                baseHours = hoursDecimal;
                            }
                            catch {
                                // Try as double as fallback
                                try {
                                    var hoursDouble = bhElement.GetDouble();
                                    if (!double.IsNaN(hoursDouble) && !double.IsInfinity(hoursDouble))
                                    {
                                        baseHours = (decimal)hoursDouble;
                                    }
                                }
                                catch {
                                    Warnings.Add($"Could not parse baseHours numeric value: '{rawValue}'");
                                }
                            }
                        }
                        else if (bhElement.ValueKind == JsonValueKind.String)
                        {
                            var strValue = bhElement.GetString() ?? "";
                            // Try as decimal first
                            if (decimal.TryParse(strValue, out var bhDecimal))
                            {
                                baseHours = bhDecimal;
                            }
                            // For special case values we want to convert to null
                            else if (string.IsNullOrEmpty(strValue) || 
                                     strValue.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
                                     strValue.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                            {
                                baseHours = null;
                            }
                            else
                            {
                                Warnings.Add($"Could not parse baseHours string value: '{strValue}'");
                            }
                        }
                    }
                    cmd.Parameters.AddWithValue("@baseHours", baseHours ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    var valueInfo = "not found";
                    if (jobInfoElement.TryGetProperty("baseHours", out var b)) {
                        valueInfo = $"{b.ValueKind}: {b}";
                    }
                    throw new Exception($"Error with baseHours parameter: {ex.Message} | Value: {valueInfo}", ex);
                }
                
                // Employee group
                try {
                    var employeeGroupId = GetLookupId("employee_groups", 
                        jobInfoElement.TryGetProperty("employeeGroup", out var egElement) && 
                        egElement.ValueKind == JsonValueKind.String
                            ? egElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@employeeGroupId", employeeGroupId);
                } catch (Exception ex) {
                    throw new Exception($"Error with employeeGroupId parameter: {ex.Message}", ex);
                }
                
                // Position info
                JsonElement positionElement = default;
                var hasPosition = jobInfoElement.TryGetProperty("position", out positionElement) && 
                                  positionElement.ValueKind == JsonValueKind.Object;
                
                var positionId = "";
                var positionTitle = "";
                
                try {
                    if (hasPosition)
                    {
                        positionId = positionElement.TryGetProperty("positionId", out var posIdElement) && 
                                    posIdElement.ValueKind == JsonValueKind.String
                            ? posIdElement.GetString() ?? ""
                            : "";
                        
                        positionTitle = positionElement.TryGetProperty("title", out var posTitleElement) && 
                                       posTitleElement.ValueKind == JsonValueKind.String
                            ? posTitleElement.GetString() ?? ""
                            : "";
                    }
                    else
                    {
                        // For legacy data where position might be a string ID instead of object
                        positionId = jobInfoElement.TryGetProperty("position", out var posIdElement) && 
                                    posIdElement.ValueKind == JsonValueKind.String
                            ? posIdElement.GetString() ?? ""
                            : "";
                    }
                    
                    cmd.Parameters.AddWithValue("@positionId", positionId);
                    cmd.Parameters.AddWithValue("@positionTitle", positionTitle);
                } catch (Exception ex) {
                    throw new Exception($"Error with position parameters: {ex.Message}", ex);
                }
                
                // Banner, brand, group
                try {
                    var bannerId = GetLookupId("banners", 
                        hasPosition && positionElement.TryGetProperty("banner", out var bannerElement) && 
                        bannerElement.ValueKind == JsonValueKind.String
                            ? bannerElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@bannerId", bannerId);
                } catch (Exception ex) {
                    throw new Exception($"Error with bannerId parameter: {ex.Message}", ex);
                }
                
                try {
                    var brandId = GetLookupId("brands", 
                        hasPosition && positionElement.TryGetProperty("brand", out var brandElement) && 
                        brandElement.ValueKind == JsonValueKind.String
                            ? brandElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@brandId", brandId);
                } catch (Exception ex) {
                    throw new Exception($"Error with brandId parameter: {ex.Message}", ex);
                }
                
                try {
                    var businessGroupId = GetLookupId("business_groups", 
                        hasPosition && positionElement.TryGetProperty("group", out var groupElement) && 
                        groupElement.ValueKind == JsonValueKind.String
                            ? groupElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@businessGroupId", businessGroupId);
                } catch (Exception ex) {
                    throw new Exception($"Error with businessGroupId parameter: {ex.Message}", ex);
                }
                
                // Cost centre
                try {
                    var costCentreId = GetLookupId("cost_centres", 
                        hasPosition && positionElement.TryGetProperty("costCentre", out var ccElement) && 
                        ccElement.ValueKind == JsonValueKind.String
                            ? ccElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@costCentreId", costCentreId);
                } catch (Exception ex) {
                    throw new Exception($"Error with costCentreId parameter: {ex.Message}", ex);
                }
                
                // Employee subgroup
                try {
                    var employeeSubgroupId = GetLookupId("employee_subgroups", 
                        hasPosition && positionElement.TryGetProperty("employeeSubgroup", out var esElement) && 
                        esElement.ValueKind == JsonValueKind.String
                            ? esElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@employeeSubgroupId", employeeSubgroupId);
                } catch (Exception ex) {
                    throw new Exception($"Error with employeeSubgroupId parameter: {ex.Message}", ex);
                }
                
                // Job role
                try {
                    var jobRoleId = GetLookupId("job_roles", 
                        hasPosition && positionElement.TryGetProperty("jobRole", out var jrElement) && 
                        jrElement.ValueKind == JsonValueKind.String
                            ? jrElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@jobRoleId", jobRoleId);
                } catch (Exception ex) {
                    throw new Exception($"Error with jobRoleId parameter: {ex.Message}", ex);
                }
                
                // Department
                try {
                    var departmentId = GetLookupId("departments", 
                        hasPosition && positionElement.TryGetProperty("payingDepartment", out var pdElement) && 
                        pdElement.ValueKind == JsonValueKind.String
                            ? pdElement.GetString() ?? "Unknown"
                            : "Unknown");
                    cmd.Parameters.AddWithValue("@departmentId", departmentId);
                } catch (Exception ex) {
                    throw new Exception($"Error with departmentId parameter: {ex.Message}", ex);
                }
                
                // Salary info - IMPROVED TYPE CONVERSION
                try {
                    decimal? salaryAmount = null;
                    if (jobInfoElement.TryGetProperty("salary", out var salaryElement))
                    {
                        salaryAmount = SafeParseDecimal(salaryElement, "salary");
                    }
                    cmd.Parameters.AddWithValue("@salaryAmount", salaryAmount ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    var salaryValue = "not found";
                    if (jobInfoElement.TryGetProperty("salary", out var s)) {
                        salaryValue = s.ToString();
                    }
                    throw new Exception($"Error with salaryAmount parameter: {ex.Message} | Value: {salaryValue}", ex);
                }
                
                // Salary Min - IMPROVED TYPE CONVERSION
                try {
                    decimal? salaryMin = null;
                    if (hasPosition && positionElement.TryGetProperty("salaryMin", out var salMinElement))
                    {
                        salaryMin = SafeParseDecimal(salMinElement, "salaryMin");
                    }
                    cmd.Parameters.AddWithValue("@salaryMin", salaryMin ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with salaryMin parameter: {ex.Message}", ex);
                }
                
                // Salary Max - IMPROVED TYPE CONVERSION
                try {
                    decimal? salaryMax = null;
                    if (hasPosition && positionElement.TryGetProperty("salaryMax", out var salMaxElement))
                    {
                        salaryMax = SafeParseDecimal(salMaxElement, "salaryMax");
                    }
                    cmd.Parameters.AddWithValue("@salaryMax", salaryMax ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with salaryMax parameter: {ex.Message}", ex);
                }
                
                // Salary Benchmark - IMPROVED TYPE CONVERSION
                try {
                    decimal? salaryBenchmark = null;
                    if (hasPosition && positionElement.TryGetProperty("salaryAwardBenchmark", out var sabElement))
                    {
                        salaryBenchmark = SafeParseDecimal(sabElement, "salaryAwardBenchmark");
                    }
                    cmd.Parameters.AddWithValue("@salaryBenchmark", salaryBenchmark ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with salaryBenchmark parameter: {ex.Message}", ex);
                }
                
                // Discretionary Allowance - IMPROVED TYPE CONVERSION
                try {
                    decimal? discretionaryAllowance = null;
                    if (jobInfoElement.TryGetProperty("discretionaryAllowance", out var daElement))
                    {
                        discretionaryAllowance = SafeParseDecimal(daElement, "discretionaryAllowance");
                    }
                    cmd.Parameters.AddWithValue("@discretionaryAllowance", discretionaryAllowance ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with discretionaryAllowance parameter: {ex.Message}", ex);
                }
                
                // STI Target - IMPROVED TYPE CONVERSION
                try {
                    int? stiTarget = null;
                    if (hasPosition && positionElement.TryGetProperty("stiTarget", out var stiElement))
                    {
                        stiTarget = SafeParseInt32(stiElement, "stiTarget", null);
                    }
                    cmd.Parameters.AddWithValue("@stiTarget", stiTarget ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with stiTarget parameter: {ex.Message}", ex);
                }
                
                // Manager info
                JsonElement managerElement = default;
                var hasManager = jobInfoElement.TryGetProperty("manager", out managerElement) && 
                                 managerElement.ValueKind == JsonValueKind.Object;
                
                try {
                    var managerEmployeeId = 
                        hasManager && managerElement.TryGetProperty("employeeId", out var mEmpIdElement) && 
                        mEmpIdElement.ValueKind == JsonValueKind.String
                            ? mEmpIdElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@managerEmployeeId", managerEmployeeId);
                } catch (Exception ex) {
                    throw new Exception($"Error with managerEmployeeId parameter: {ex.Message}", ex);
                }
                
                try {
                    var managerName = 
                        hasManager && managerElement.TryGetProperty("name", out var mNameElement) && 
                        mNameElement.ValueKind == JsonValueKind.String
                            ? mNameElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@managerName", managerName);
                } catch (Exception ex) {
                    throw new Exception($"Error with managerName parameter: {ex.Message}", ex);
                }
                
                try {
                    var managerPositionId = 
                        hasManager && managerElement.TryGetProperty("position", out var mPosElement) && 
                        mPosElement.ValueKind == JsonValueKind.String
                            ? mPosElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@managerPositionId", managerPositionId);
                } catch (Exception ex) {
                    throw new Exception($"Error with managerPositionId parameter: {ex.Message}", ex);
                }
                
                try {
                    var managerPositionTitle = 
                        hasManager && managerElement.TryGetProperty("positionTitle", out var mPosTitleElement) && 
                        mPosTitleElement.ValueKind == JsonValueKind.String
                            ? mPosTitleElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@managerPositionTitle", managerPositionTitle);
                } catch (Exception ex) {
                    throw new Exception($"Error with managerPositionTitle parameter: {ex.Message}", ex);
                }
                
                // Dates - IMPROVED DATE PARSING
                try {
                    DateTime? startDate = null;
                    if (jobInfoElement.TryGetProperty("startDate", out var startDateElement))
                    {
                        startDate = SafeParseDateTime(startDateElement, "startDate");
                    }
                    
                    cmd.Parameters.AddWithValue("@startDate", startDate ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with startDate parameter: {ex.Message}", ex);
                }
                
                try {
                    DateTime? endDate = null;
                    if (jobInfoElement.TryGetProperty("endDate", out var endDateElement))
                    {
                        endDate = SafeParseDateTime(endDateElement, "endDate");
                    }
                    
                    cmd.Parameters.AddWithValue("@endDate", endDate ?? (object)DBNull.Value);
                } catch (Exception ex) {
                    throw new Exception($"Error with endDate parameter: {ex.Message}", ex);
                }
                
                // Additional info
                try {
                    var employeeMovementType = 
                        jobInfoElement.TryGetProperty("employeeMovementType", out var emtElement) && 
                        emtElement.ValueKind == JsonValueKind.String
                            ? emtElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@employeeMovementType", employeeMovementType);
                } catch (Exception ex) {
                    throw new Exception($"Error with employeeMovementType parameter: {ex.Message}", ex);
                }
                
                try {
                    var stiScheme = 
                        jobInfoElement.TryGetProperty("stiScheme", out var stiSchemeElement) && 
                        stiSchemeElement.ValueKind == JsonValueKind.String
                            ? stiSchemeElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@stiScheme", stiScheme);
                } catch (Exception ex) {
                    throw new Exception($"Error with stiScheme parameter: {ex.Message}", ex);
                }
                
                try {
                    var payScaleGroup = 
                        jobInfoElement.TryGetProperty("payScaleGroup", out var psgElement) && 
                        psgElement.ValueKind == JsonValueKind.String
                            ? psgElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@payScaleGroup", payScaleGroup);
                } catch (Exception ex) {
                    throw new Exception($"Error with payScaleGroup parameter: {ex.Message}", ex);
                }
                
                try {
                    var payScaleLevel = 
                        jobInfoElement.TryGetProperty("payScaleLevel", out var pslElement) && 
                        pslElement.ValueKind == JsonValueKind.String
                            ? pslElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@payScaleLevel", payScaleLevel);
                } catch (Exception ex) {
                    throw new Exception($"Error with payScaleLevel parameter: {ex.Message}", ex);
                }
                
                try {
                    var leaveEntitlement = 
                        hasPosition && positionElement.TryGetProperty("leaveEntitlement", out var leElement) && 
                        leElement.ValueKind == JsonValueKind.String
                            ? leElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@leaveEntitlement", leaveEntitlement);
                } catch (Exception ex) {
                    throw new Exception($"Error with leaveEntitlement parameter: {ex.Message}", ex);
                }
                
                try {
                    var leaveEntitlementName = 
                        hasPosition && positionElement.TryGetProperty("leaveEntitlementName", out var lenElement) && 
                        lenElement.ValueKind == JsonValueKind.String
                            ? lenElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@leaveEntitlementName", leaveEntitlementName);
                } catch (Exception ex) {
                    throw new Exception($"Error with leaveEntitlementName parameter: {ex.Message}", ex);
                }
                
                try {
                    var carEligibility = 
                        hasPosition && positionElement.TryGetProperty("carEligibility", out var ceElement) && 
                        ceElement.ValueKind == JsonValueKind.String
                            ? ceElement.GetString() ?? ""
                            : "";
                    
                    cmd.Parameters.AddWithValue("@carEligibility", carEligibility);
                } catch (Exception ex) {
                    throw new Exception($"Error with carEligibility parameter: {ex.Message}", ex);
                }
                
                // Set the connection
                cmd.Connection = conn;
                
                // Execute the command
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while importing job info. {ex.Message}", ex);
            }
        }
    }
    
    private async Task ImportContract(NpgsqlConnection conn, JsonElement root, int movementId, string contractProperty, bool isCurrent)
    {
        try
        {
            // Delete existing contract for this movement and type
            await using var deleteContractCmd = new NpgsqlCommand(
                "DELETE FROM team_movements.contracts WHERE movement_id = @movementId AND is_current = @isCurrent", conn);
            deleteContractCmd.Parameters.AddWithValue("@movementId", movementId);
            deleteContractCmd.Parameters.AddWithValue("@isCurrent", isCurrent);
            await deleteContractCmd.ExecuteNonQueryAsync();
            
            // Import contract
            if (root.TryGetProperty(contractProperty, out var contractElement) && 
                contractElement.ValueKind == JsonValueKind.Object)
            {
                // Insert contract
                await using var insertContractCmd = new NpgsqlCommand(@"
                    INSERT INTO team_movements.contracts (
                        movement_id,
                        is_current
                    ) VALUES (
                        @movementId,
                        @isCurrent
                    ) RETURNING id
                ", conn);
                insertContractCmd.Parameters.AddWithValue("@movementId", movementId);
                insertContractCmd.Parameters.AddWithValue("@isCurrent", isCurrent);
                
                var contractResult = await insertContractCmd.ExecuteScalarAsync();
                if (contractResult == null || contractResult == DBNull.Value)
                    throw new Exception("Failed to insert contract and get ID");
                    
                var contractId = Convert.ToInt32(contractResult);
                
                // Import mutual flags
                if (contractElement.TryGetProperty("mutualFlags", out var flagsElement) && 
                    flagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var flag in flagsElement.EnumerateArray())
                    {
                        if (flag.ValueKind == JsonValueKind.String)
                        {
                            var flagValue = flag.GetString() ?? "Unknown";
                            var flagId = GetLookupId("mutual_flags", flagValue);
                            
                            try
                            {
                                await using var insertFlagCmd = new NpgsqlCommand(@"
                                    INSERT INTO team_movements.contract_mutual_flags (
                                        contract_id,
                                        flag_id
                                    ) VALUES (
                                        @contractId,
                                        @flagId
                                    ) ON CONFLICT DO NOTHING
                                ", conn);
                                insertFlagCmd.Parameters.AddWithValue("@contractId", contractId);
                                insertFlagCmd.Parameters.AddWithValue("@flagId", flagId);
                                
                                await insertFlagCmd.ExecuteNonQueryAsync();
                            }
                            catch (Exception ex)
                            {
                                Warnings.Add($"Unable to import mutual flag '{flagValue}': {ex.Message}");
                            }
                        }
                    }
                }
                
                // Import weeks
                if (contractElement.TryGetProperty("weeks", out var weeksElement) && 
                    weeksElement.ValueKind == JsonValueKind.Array)
                {
                    var weekIndex = 0;
                    foreach (var week in weeksElement.EnumerateArray())
                    {
                        if (week.ValueKind == JsonValueKind.Object)
                        {
                            try
                            {
                                // Insert week
                                await using var insertWeekCmd = new NpgsqlCommand(@"
                                    INSERT INTO team_movements.contract_weeks (
                                        contract_id,
                                        week_index
                                    ) VALUES (
                                        @contractId,
                                        @weekIndex
                                    ) RETURNING id
                                ", conn);
                                insertWeekCmd.Parameters.AddWithValue("@contractId", contractId);
                                insertWeekCmd.Parameters.AddWithValue("@weekIndex", weekIndex);
                                
                                var weekResult = await insertWeekCmd.ExecuteScalarAsync();
                                if (weekResult == null || weekResult == DBNull.Value)
                                    throw new Exception("Failed to insert week and get ID");
                                    
                                var weekId = Convert.ToInt32(weekResult);
                                
                                // Import days
                                string[] days = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
                                foreach (var day in days)
                                {
                                    if (week.TryGetProperty(day, out var dayElement) && 
                                        dayElement.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var shift in dayElement.EnumerateArray())
                                        {
                                            if (shift.ValueKind == JsonValueKind.Object)
                                            {
                                                try
                                                {
                                                    var startTime = shift.TryGetProperty("start", out var startElement) && 
                                                                    startElement.ValueKind == JsonValueKind.String
                                                        ? startElement.GetString() ?? "00:00"
                                                        : "00:00";
                                                    
                                                    var endTime = shift.TryGetProperty("end", out var endElement) && 
                                                                  endElement.ValueKind == JsonValueKind.String
                                                        ? endElement.GetString() ?? "00:00"
                                                        : "00:00";
                                                    
                                                    // Insert daily schedule with safe time parsing
                                                    await using var insertScheduleCmd = new NpgsqlCommand(@"
                                                        INSERT INTO team_movements.daily_schedules (
                                                            contract_week_id,
                                                            day_of_week,
                                                            start_time,
                                                            end_time
                                                        ) VALUES (
                                                            @weekId,
                                                            @dayOfWeek,
                                                            @startTime,
                                                            @endTime
                                                        ) RETURNING id
                                                    ", conn);
                                                    insertScheduleCmd.Parameters.AddWithValue("@weekId", weekId);
                                                    insertScheduleCmd.Parameters.AddWithValue("@dayOfWeek", day);
                                                    insertScheduleCmd.Parameters.AddWithValue("@startTime", SafeParseTimeSpan(startTime));
                                                    insertScheduleCmd.Parameters.AddWithValue("@endTime", SafeParseTimeSpan(endTime));
                                                    
                                                    var scheduleResult = await insertScheduleCmd.ExecuteScalarAsync();
                                                    if (scheduleResult == null || scheduleResult == DBNull.Value)
                                                        throw new Exception("Failed to insert schedule and get ID");
                                                        
                                                    var scheduleId = Convert.ToInt32(scheduleResult);
                                                    
                                                    // Import breaks
                                                    if (shift.TryGetProperty("breaks", out var breaksElement) && 
                                                        breaksElement.ValueKind == JsonValueKind.Array)
                                                    {
                                                        foreach (var breakItem in breaksElement.EnumerateArray())
                                                        {
                                                            if (breakItem.ValueKind == JsonValueKind.String)
                                                            {
                                                                try
                                                                {
                                                                    var breakType = breakItem.GetString() ?? "Unknown";
                                                                    var breakTypeId = GetLookupId("break_types", breakType);
                                                                    
                                                                    await using var insertBreakCmd = new NpgsqlCommand(@"
                                                                        INSERT INTO team_movements.schedule_breaks (
                                                                            daily_schedule_id,
                                                                            break_type_id
                                                                        ) VALUES (
                                                                            @scheduleId,
                                                                            @breakTypeId
                                                                        )
                                                                    ", conn);
                                                                    insertBreakCmd.Parameters.AddWithValue("@scheduleId", scheduleId);
                                                                    insertBreakCmd.Parameters.AddWithValue("@breakTypeId", breakTypeId);
                                                                    
                                                                    await insertBreakCmd.ExecuteNonQueryAsync();
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    Warnings.Add($"Unable to import break: {ex.Message}");
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Warnings.Add($"Unable to import shift for {day} in week {weekIndex}: {ex.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                weekIndex++;
                            }
                            catch (Exception ex)
                            {
                                Warnings.Add($"Unable to import week {weekIndex}: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Warnings.Add($"Error importing contract {contractProperty}: {ex.Message}");
            throw; // Rethrow to ensure transaction rolls back
        }
    }
    
    private async Task ImportHistory(NpgsqlConnection conn, JsonElement root, int movementId)
    {
        // Delete existing history events for this movement
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM team_movements.history_events WHERE movement_id = @movementId", conn);
        deleteCmd.Parameters.AddWithValue("@movementId", movementId);
        await deleteCmd.ExecuteNonQueryAsync();
        
        // Import history events
        if (root.TryGetProperty("history", out var historyElement) && 
            historyElement.ValueKind == JsonValueKind.Array)
        {
            var eventIndex = 0;
            foreach (var eventObj in historyElement.EnumerateArray())
            {
                if (eventObj.ValueKind == JsonValueKind.Object)
                {
                    // Get the first property, which is the event type
                    foreach (var eventProperty in eventObj.EnumerateObject())
                    {
                        var eventType = eventProperty.Name;
                        var eventTypeId = GetLookupId("history_event_types", eventType);
                        
                        var eventData = eventProperty.Value;
                        if (eventData.ValueKind == JsonValueKind.Object)
                        {
                            // Improved date parsing
                            DateTime? createdDate = null;
                            if (eventData.TryGetProperty("createdDate", out var cdElement))
                            {
                                createdDate = SafeParseDateTime(cdElement, "history.createdDate");
                            }
                            
                            var createdBy = eventData.TryGetProperty("createdBy", out var cbElement) && 
                                            cbElement.ValueKind == JsonValueKind.String
                                ? cbElement.GetString() ?? ""
                                : "";
                            
                            var createdByName = eventData.TryGetProperty("createdByName", out var cbnElement) && 
                                                cbnElement.ValueKind == JsonValueKind.String
                                ? cbnElement.GetString() ?? ""
                                : "";
                            
                            var notes = eventData.TryGetProperty("notes", out var notesElement) && 
                                        notesElement.ValueKind == JsonValueKind.String
                                ? notesElement.GetString() ?? ""
                                : "";
                            
                            // Participant data
                            var participantEmployeeId = "";
                            var participantName = "";
                            var participantPositionId = "";
                            var participantPositionTitle = "";
                            int? participantRoleId = null;
                            
                            if (eventData.TryGetProperty("participant", out var participantElement) && 
                                participantElement.ValueKind == JsonValueKind.Object)
                            {
                                participantEmployeeId = participantElement.TryGetProperty("employeeId", out var peElement) && 
                                                       peElement.ValueKind == JsonValueKind.String
                                    ? peElement.GetString() ?? ""
                                    : "";
                                
                                participantName = participantElement.TryGetProperty("name", out var pnElement) && 
                                                 pnElement.ValueKind == JsonValueKind.String
                                    ? pnElement.GetString() ?? ""
                                    : "";
                                
                                participantPositionId = participantElement.TryGetProperty("position", out var ppElement) && 
                                                       ppElement.ValueKind == JsonValueKind.String
                                    ? ppElement.GetString() ?? ""
                                    : "";
                                
                                participantPositionTitle = participantElement.TryGetProperty("positionTitle", out var pptElement) && 
                                                          pptElement.ValueKind == JsonValueKind.String
                                    ? pptElement.GetString() ?? ""
                                    : "";
                                
                                if (participantElement.TryGetProperty("role", out var prElement) && 
                                    prElement.ValueKind == JsonValueKind.String)
                                {
                                    var roleValue = prElement.GetString() ?? "Unknown";
                                    participantRoleId = GetLookupId("participant_roles", roleValue);
                                }
                            }
                            
                            // Insert history event
                            await using var insertCmd = new NpgsqlCommand(@"
                                INSERT INTO team_movements.history_events (
                                    movement_id,
                                    event_type_id,
                                    event_index,
                                    created_date,
                                    created_by,
                                    created_by_name,
                                    participant_employee_id,
                                    participant_name,
                                    participant_position_id,
                                    participant_position_title,
                                    participant_role_id,
                                    notes,
                                    event_data
                                ) VALUES (
                                    @movementId,
                                    @eventTypeId,
                                    @eventIndex,
                                    @createdDate,
                                    @createdBy,
                                    @createdByName,
                                    @participantEmployeeId,
                                    @participantName,
                                    @participantPositionId,
                                    @participantPositionTitle,
                                    @participantRoleId,
                                    @notes,
                                    @eventData
                                )
                            ", conn);
                            insertCmd.Parameters.AddWithValue("@movementId", movementId);
                            insertCmd.Parameters.AddWithValue("@eventTypeId", eventTypeId);
                            insertCmd.Parameters.AddWithValue("@eventIndex", eventIndex);
                            insertCmd.Parameters.AddWithValue("@createdDate", createdDate ?? (object)DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@createdBy", createdBy);
                            insertCmd.Parameters.AddWithValue("@createdByName", createdByName);
                            insertCmd.Parameters.AddWithValue("@participantEmployeeId", participantEmployeeId);
                            insertCmd.Parameters.AddWithValue("@participantName", participantName);
                            insertCmd.Parameters.AddWithValue("@participantPositionId", participantPositionId);
                            insertCmd.Parameters.AddWithValue("@participantPositionTitle", participantPositionTitle);
                            insertCmd.Parameters.AddWithValue("@participantRoleId", participantRoleId ?? (object)DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@notes", notes);
                            
                            var npgsqlParameter = new NpgsqlParameter("@eventData", NpgsqlDbType.Jsonb)
                            {
                                Value = eventData.GetRawText()
                            };
                            insertCmd.Parameters.Add(npgsqlParameter);
                            
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                        
                        break; // Only process the first property (event type)
                    }
                    
                    eventIndex++;
                }
            }
        }
    }
    
    private async Task ImportTags(NpgsqlConnection conn, JsonElement root, int movementId)
    {
        // Delete existing tags for this movement
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM team_movements.tags WHERE movement_id = @movementId", conn);
        deleteCmd.Parameters.AddWithValue("@movementId", movementId);
        await deleteCmd.ExecuteNonQueryAsync();
        
        // Import tags
        if (root.TryGetProperty("tags", out var tagsElement) && 
            tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    var tagValue = tag.GetString() ?? "";
                    if (!string.IsNullOrEmpty(tagValue))
                    {
                        await using var insertCmd = new NpgsqlCommand(@"
                            INSERT INTO team_movements.tags (
                                movement_id,
                                tag_value
                            ) VALUES (
                                @movementId,
                                @tagValue
                            ) ON CONFLICT DO NOTHING
                        ", conn);
                        insertCmd.Parameters.AddWithValue("@movementId", movementId);
                        insertCmd.Parameters.AddWithValue("@tagValue", tagValue);
                        
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
    }

    private int GetLookupId(string tableName, string value)
    {
        if (string.IsNullOrEmpty(value)) value = "Unknown";
        
        var cacheKey = $"{tableName}:{value}";
        if (_lookupCache.TryGetValue(cacheKey, out var id))
        {
            return id;
        }
        
        // If not in cache, return the ID for 'Unknown'
        return _lookupCache[$"{tableName}:Unknown"];
    }
    
    // NEW HELPER METHODS FOR SAFER TYPE CONVERSION
    
    private DateTime? SafeParseDateTime(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var dateString = element.GetString() ?? "";
            if (string.IsNullOrEmpty(dateString) ||
                dateString.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                dateString.Equals("undefined", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            
            if (DateTime.TryParse(dateString, out var date))
            {
                return date;
            }
            
            // Try multiple date formats
            string[] formats = { 
                "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", 
                "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy/MM/dd", "dd-MM-yyyy", "MM-dd-yyyy" 
            };
            
            if (DateTime.TryParseExact(dateString, formats, 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate;
            }
            
            // Log failed parsing 
            Warnings.Add($"Failed to parse {fieldName} date: '{dateString}'");
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            try
            {
                // Try parsing as Unix timestamp (milliseconds since epoch)
                var timestamp = element.GetInt64();
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
                return dateTime;
            }
            catch
            {
                Warnings.Add($"Failed to parse {fieldName} numeric timestamp: {element}");
            }
        }
        
        return null;
    }
    
    private decimal? SafeParseDecimal(JsonElement element, string fieldName)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    // Try as decimal first
                    return element.GetDecimal();
                }
                catch
                {
                    // Try as double and convert
                    try
                    {
                        var doubleValue = element.GetDouble();
                        if (!double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
                        {
                            return (decimal)doubleValue;
                        }
                    }
                    catch
                    {
                        // Last ditch attempt - stringify and parse
                        var strValue = element.ToString();
                        if (decimal.TryParse(strValue, out var decimalValue))
                        {
                            return decimalValue;
                        }
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var strValue = element.GetString() ?? "";
                
                // Special cases
                if (string.IsNullOrEmpty(strValue) ||
                    strValue.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                
                // Handle currency symbols and number formatting
                var cleanValue = strValue
                    .Replace("$", "")
                    .Replace("€", "")
                    .Replace("£", "")
                    .Replace(",", "")
                    .Trim();
                
                if (decimal.TryParse(cleanValue, out var result))
                {
                    return result;
                }
            }
            
            // Log the value that couldn't be parsed
            Warnings.Add($"Could not parse {fieldName} value: '{element}'");
            return null;
        }
        catch (Exception ex)
        {
            Warnings.Add($"Error parsing {fieldName}: {ex.Message}");
            return null;
        }
    }
    
    private int? SafeParseInt32(JsonElement element, string fieldName, int? defaultValue)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    // Try as int first
                    return element.GetInt32();
                }
                catch
                {
                    // Try as double and convert
                    try
                    {
                        var doubleValue = element.GetDouble();
                        if (!double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
                        {
                            return (int)Math.Round(doubleValue);
                        }
                    }
                    catch
                    {
                        // Last ditch attempt - stringify and parse
                        var strValue = element.ToString();
                        if (int.TryParse(strValue, out var intValue))
                        {
                            return intValue;
                        }
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var strValue = element.GetString() ?? "";
                
                // Special cases
                if (string.IsNullOrEmpty(strValue) ||
                    strValue.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
                    strValue.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    return defaultValue;
                }
                
                // Handle common formatting
                var cleanValue = strValue
                    .Replace(",", "")
                    .Trim();
                
                if (int.TryParse(cleanValue, out var intResult))
                {
                    return intResult;
                }
                
                // Try decimal conversion
                if (decimal.TryParse(cleanValue, out var decimalResult))
                {
                    return (int)Math.Round(decimalResult);
                }
            }
            
            // Log the value that couldn't be parsed
            Warnings.Add($"Could not parse {fieldName} value as integer: '{element}'");
            return defaultValue;
        }
        catch (Exception ex)
        {
            Warnings.Add($"Error parsing {fieldName} as integer: {ex.Message}");
            return defaultValue;
        }
    }
    
    private bool SafeParseBool(JsonElement element, string fieldName, bool defaultValue)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            else if (element.ValueKind == JsonValueKind.False)
            {
                return false;
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var strValue = element.GetString() ?? "";
                
                // Common string boolean representations
                if (string.Equals(strValue, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(strValue, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(strValue, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(strValue, "y", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else if (string.Equals(strValue, "false", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(strValue, "no", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(strValue, "0", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(strValue, "n", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                var intValue = element.GetInt32();
                return intValue != 0;
            }
            
            // Default case
            return defaultValue;
        }
        catch (Exception ex)
        {
            Warnings.Add($"Error parsing {fieldName} as boolean: {ex.Message}");
            return defaultValue;
        }
    }
    
    private async Task ProcessAndStoreCostCentreDetails(NpgsqlConnection conn, JsonElement jobInfoElement)
    {
        // Process manager cost centre with geographic data
        if (jobInfoElement.TryGetProperty("manager", out var managerElement) &&
            managerElement.ValueKind == JsonValueKind.Object)
        {
            if (managerElement.TryGetProperty("costCentre", out var managerCostCentreElement) &&
                managerCostCentreElement.ValueKind == JsonValueKind.Object)
            {
                // Extract detailed cost centre information
                var ccCode = "Unknown";
                var ccName = "Unknown";
                string? addressFormatted = null;
                decimal? latitude = null;
                decimal? longitude = null;
                
                if (managerCostCentreElement.TryGetProperty("costCentre", out var ccCodeEl) &&
                    ccCodeEl.ValueKind == JsonValueKind.String)
                {
                    ccCode = ccCodeEl.GetString() ?? "Unknown";
                }
                
                if (managerCostCentreElement.TryGetProperty("name", out var ccNameEl) &&
                    ccNameEl.ValueKind == JsonValueKind.String)
                {
                    ccName = ccNameEl.GetString() ?? "Unknown";
                }
                
                if (managerCostCentreElement.TryGetProperty("addressFormatted", out var addressEl) &&
                    addressEl.ValueKind == JsonValueKind.String)
                {
                    addressFormatted = addressEl.GetString();
                }
                
                if (managerCostCentreElement.TryGetProperty("lat", out var latEl))
                {
                    latitude = SafeParseDecimal(latEl, "latitude");
                }
                
                if (managerCostCentreElement.TryGetProperty("lng", out var lngEl))
                {
                    longitude = SafeParseDecimal(lngEl, "longitude");
                }
                
                // Update or insert the cost centre with location data
                await UpsertCostCentreWithLocation(conn, ccCode, ccName, addressFormatted, latitude, longitude);
            }
        }
    }

    private async Task UpsertCostCentreWithLocation(NpgsqlConnection conn, string ccCode, string ccName, 
        string? addressFormatted, decimal? latitude, decimal? longitude)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO team_movements.cost_centres (cost_centre_code, cost_centre_name, formatted_address, latitude, longitude)
                VALUES (@code, @name, @formattedAddress, @latitude, @longitude)
                ON CONFLICT (cost_centre_code) 
                DO UPDATE SET 
                    cost_centre_name = EXCLUDED.cost_centre_name,
                    formatted_address = COALESCE(EXCLUDED.formatted_address, team_movements.cost_centres.formatted_address),
                    latitude = COALESCE(EXCLUDED.latitude, team_movements.cost_centres.latitude),
                    longitude = COALESCE(EXCLUDED.longitude, team_movements.cost_centres.longitude)
                WHERE EXCLUDED.formatted_address IS NOT NULL OR EXCLUDED.latitude IS NOT NULL OR EXCLUDED.longitude IS NOT NULL;
            ", conn);
            
            cmd.Parameters.AddWithValue("@code", ccCode);
            cmd.Parameters.AddWithValue("@name", ccName);
            cmd.Parameters.AddWithValue("@formattedAddress", (object?)addressFormatted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@latitude", (object?)latitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@longitude", (object?)longitude ?? DBNull.Value);
            
            await cmd.ExecuteNonQueryAsync();
            
            if (!string.IsNullOrEmpty(addressFormatted) || latitude.HasValue || longitude.HasValue)
            {
                Console.WriteLine($"Updated cost centre {ccCode} with location data: {addressFormatted}");
            }
        }
        catch (Exception ex)
        {
            Warnings.Add($"Failed to update cost centre {ccCode} with location data: {ex.Message}");
        }
    }

    // Also process history events for cost centre data
    private async Task ProcessHistoryEventCostCentres(NpgsqlConnection conn, JsonElement historyElement)
    {
        if (historyElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var eventObj in historyElement.EnumerateArray())
            {
                if (eventObj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var eventProperty in eventObj.EnumerateObject())
                    {
                        var eventData = eventProperty.Value;
                        if (eventData.ValueKind == JsonValueKind.Object)
                        {
                            // Check for newManager with nested cost centre data
                            if (eventData.TryGetProperty("newManager", out var newManagerElement) &&
                                newManagerElement.ValueKind == JsonValueKind.Object)
                            {
                                await ProcessManagerCostCentreInHistory(conn, newManagerElement);
                            }
                            
                            // Check for participant with nested cost centre data
                            if (eventData.TryGetProperty("participant", out var participantElement) &&
                                participantElement.ValueKind == JsonValueKind.Object)
                            {
                                await ProcessManagerCostCentreInHistory(conn, participantElement);
                            }
                        }
                        break; // Only process the first property (event type)
                    }
                }
            }
        }
    }

    private async Task ProcessManagerCostCentreInHistory(NpgsqlConnection conn, JsonElement managerElement)
    {
        if (managerElement.TryGetProperty("costCentre", out var costCentreElement) &&
            costCentreElement.ValueKind == JsonValueKind.Object)
        {
            // Extract detailed cost centre information
            var ccCode = "Unknown";
            var ccName = "Unknown";
            string? addressFormatted = null;
            decimal? latitude = null;
            decimal? longitude = null;
            
            if (costCentreElement.TryGetProperty("costCentre", out var ccCodeEl) &&
                ccCodeEl.ValueKind == JsonValueKind.String)
            {
                ccCode = ccCodeEl.GetString() ?? "Unknown";
            }
            
            if (costCentreElement.TryGetProperty("name", out var ccNameEl) &&
                ccNameEl.ValueKind == JsonValueKind.String)
            {
                ccName = ccNameEl.GetString() ?? "Unknown";
            }
            
            if (costCentreElement.TryGetProperty("addressFormatted", out var addressEl) &&
                addressEl.ValueKind == JsonValueKind.String)
            {
                addressFormatted = addressEl.GetString();
            }
            
            if (costCentreElement.TryGetProperty("lat", out var latEl))
            {
                latitude = SafeParseDecimal(latEl, "latitude");
            }
            
            if (costCentreElement.TryGetProperty("lng", out var lngEl))
            {
                longitude = SafeParseDecimal(lngEl, "longitude");
            }
            
            // Update cost centre with location data
            await UpsertCostCentreWithLocation(conn, ccCode, ccName, addressFormatted, latitude, longitude);
        }
    }
}