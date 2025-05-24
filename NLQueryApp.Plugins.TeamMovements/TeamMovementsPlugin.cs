using Microsoft.Extensions.Logging;
using NLQueryApp.Core.Models;
using NLQueryApp.Plugins.TeamMovements.Import;

namespace NLQueryApp.Plugins.TeamMovements;

public class TeamMovementsPlugin : IDataSourcePlugin
{
    private readonly ILogger<TeamMovementsPlugin> _logger;
    
    public TeamMovementsPlugin(ILogger<TeamMovementsPlugin> logger)
    {
        _logger = logger;
    }
    
    public string PluginName => "Team Movements";
    public string DataSourceType => "team-movements-postgres";
    
    public async Task InitializeSchemaAsync(string connectionString)
    {
        _logger.LogInformation("Initializing Team Movements schema");
        
        var schemaCreator = new SchemaCreator(connectionString);
        var analyzer = new SchemaAnalyzer();
        
        // Create schema with default lookup values
        await schemaCreator.CreateSchema(analyzer);
        
        _logger.LogInformation("Team Movements schema initialization completed");
    }
    
    public async Task ImportDataAsync(string connectionString, string dataPath)
    {
        _logger.LogInformation("Starting Team Movements data import from {Path}", dataPath);
        
        // Step 1: Analyze files
        var analyzer = new SchemaAnalyzer();
        await analyzer.AnalyzeDirectory(dataPath);
        
        // Step 2: Import data
        var importer = new DataImporter(connectionString);
        await importer.ImportData(dataPath);
        
        // Step 3: Verify results
        var results = await importer.VerifyImportResults();
        
        _logger.LogInformation("Import complete: {FilesProcessed} files processed with {ErrorCount} errors", 
            results.FilesProcessed, results.ErrorCount);
    }
    
    public string GetDefaultSchemaContext()
    {
        return @"# Team Movements Database Schema Guide

## CRITICAL SCHEMA INFORMATION
- 'team_movements' is a SCHEMA name, NOT a table name
- All tables must be referenced with full schema-qualified names, like: team_movements.movements
- NEVER use 'team_movements' alone as a table name - it does not exist as a table

## Overview
This database tracks employee movements between positions, departments, and locations within the organization.

## Key Entities

### team_movements.movements
Main table tracking all employee movements between positions/departments
- movement_id: Unique identifier for the movement
- employee_id: ID of the employee being moved
- movement_type_id: Type of movement (permanent, temporary, secondment)
- status_id: Current status (Completed, Expired, Rejected, etc.)
- start_date/end_date: Movement duration

### team_movements.participants
People involved in movements (employees, managers, HR partners)
- Links to movements table
- Includes role (TeamMember, SendingManager, ReceivingManager, etc.)
- Contains employee details and position information

### team_movements.job_info
Current and new job information for movements
- is_current: true = current position, false = new position
- Contains salary, hours, manager, and position details
- Links to various lookup tables for organization structure

### team_movements.contracts
Contract details including schedules and mutual agreements
- Links to contract_weeks for rotating schedules
- Contains mutual flags for special arrangements

### team_movements.history_events
Audit trail of all actions taken on movements
- Tracks workflow events with timestamps
- Includes actor information and event data

### team_movements.cost_centres
Store/office locations with geographic data
- Contains latitude, longitude, and formatted addresses
- Used for analyzing geographic movement patterns

## Common Query Patterns

1. Movement counts by type or status
2. Active movements (not completed/expired/rejected)
3. Salary impact analysis (comparing current vs new)
4. Manager approval patterns
5. Geographic movement flows
6. Processing time analysis
7. Contract schedule analysis

## Important Notes
- Always join with status and movement_type tables for human-readable values
- The job_info table has is_current flag: true = current position, false = new position
- Participants table includes all people involved (employee, managers, HR partners)
- History events track the complete workflow with timestamps
- Cost centres include geographic data (latitude, longitude, formatted_address)
- Tags can contain movement metadata like 'FromBanner:X' or 'ToBanner:Y'";
    }
    
    public async Task<List<QueryExample>> GetQueryExamplesAsync()
    {
        await Task.CompletedTask;
        
        return new List<QueryExample>
        {
            new QueryExample
            {
                Title = "Count Movements by Type",
                Category = "Basic Statistics",
                NaturalLanguageQuery = "How many movements of each type do we have?",
                SqlQuery = @"SELECT mt.type_name, COUNT(m.id) AS movement_count
FROM team_movements.movement_types mt
JOIN team_movements.movements m ON mt.id = m.movement_type_id
GROUP BY mt.type_name, mt.id
ORDER BY movement_count DESC;",
                Description = "Shows distribution of movement types"
            },
            new QueryExample
            {
                Title = "Active Movements",
                Category = "Status Tracking",
                NaturalLanguageQuery = "What movements are currently active?",
                SqlQuery = @"SELECT m.movement_id, m.employee_id, m.start_date, s.status_name
FROM team_movements.movements m
JOIN team_movements.statuses s ON m.status_id = s.id
WHERE s.status_name NOT IN ('Completed', 'Expired', 'Rejected')
ORDER BY m.start_date DESC;",
                Description = "Lists all non-completed movements"
            },
            new QueryExample
            {
                Title = "Salary Impact Analysis",
                Category = "Financial Analysis",
                NaturalLanguageQuery = "What's the average salary increase for movements?",
                SqlQuery = @"SELECT
  AVG(new.salary_amount - current.salary_amount) AS avg_increase,
  MIN(new.salary_amount - current.salary_amount) AS min_increase,
  MAX(new.salary_amount - current.salary_amount) AS max_increase
FROM team_movements.job_info new
JOIN team_movements.job_info current ON new.movement_id = current.movement_id
WHERE new.is_current = false AND current.is_current = true
  AND new.salary_amount IS NOT NULL AND current.salary_amount IS NOT NULL;",
                Description = "Analyzes salary changes in movements"
            },
            new QueryExample
            {
                Title = "Manager Approval Patterns",
                Category = "Manager Analysis",
                NaturalLanguageQuery = "Which managers approve the most movements?",
                SqlQuery = @"SELECT 
  p.employee_id AS manager_id,
  p.name AS manager_name,
  COUNT(DISTINCT m.id) AS movements_approved
FROM team_movements.history_events he
JOIN team_movements.history_event_types het ON he.event_type_id = het.id
JOIN team_movements.movements m ON he.movement_id = m.id
JOIN team_movements.participants p ON he.movement_id = p.movement_id
JOIN team_movements.participant_roles pr ON p.role_id = pr.id
WHERE het.event_type_name = 'MovementApproved'
  AND pr.role_name IN ('SendingManager', 'ReceivingManager')
GROUP BY p.employee_id, p.name
ORDER BY movements_approved DESC
LIMIT 20;",
                Description = "Shows managers by approval count"
            },
            new QueryExample
            {
                Title = "Geographic Movement Patterns",
                Category = "Location Analysis",
                NaturalLanguageQuery = "Show movements between different cost centres with locations",
                SqlQuery = @"SELECT 
  cc_from.cost_centre_name AS from_location,
  cc_from.formatted_address AS from_address,
  cc_to.cost_centre_name AS to_location,
  cc_to.formatted_address AS to_address,
  COUNT(*) AS movement_count
FROM team_movements.movements m
JOIN team_movements.job_info ji_current ON m.id = ji_current.movement_id AND ji_current.is_current = true
JOIN team_movements.job_info ji_new ON m.id = ji_new.movement_id AND ji_new.is_current = false
JOIN team_movements.cost_centres cc_from ON ji_current.cost_centre_id = cc_from.id
JOIN team_movements.cost_centres cc_to ON ji_new.cost_centre_id = cc_to.id
WHERE cc_from.id != cc_to.id
GROUP BY cc_from.cost_centre_name, cc_from.formatted_address, 
         cc_to.cost_centre_name, cc_to.formatted_address
ORDER BY movement_count DESC
LIMIT 50;",
                Description = "Shows movement flows between locations"
            }
        };
    }
}
