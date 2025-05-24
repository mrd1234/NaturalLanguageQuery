using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLQueryApp.Core;
using NLQueryApp.Core.Models;
using Npgsql;

namespace NLQueryApp.Data.Providers;

public class TeamMovementsDataSourceProvider : PostgresDataSourceProvider
{
    private readonly IConfiguration _configuration;
    private const string TEAM_MOVEMENTS_SCHEMA = "team_movements";
    
    public TeamMovementsDataSourceProvider(IConfiguration configuration, ILogger<TeamMovementsDataSourceProvider> logger)
        : base(logger)
    {
        _configuration = configuration;
    }

    public override string ProviderType => "team-movements-postgres";

    public override async Task<string> GetSchemaAsync(DataSourceDefinition dataSource)
    {
        // Override to specifically get team_movements schema
        var connectionString = dataSource.GetConnectionString();
        var schemaInfo = new StringBuilder();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        
        var includedSchemas = new[] { TEAM_MOVEMENTS_SCHEMA };
        var excludedTables = Array.Empty<string>();
        
        _logger.LogDebug("Extracting team movements schema");
        
        await ExtractSchemaInfo(connection, includedSchemas, excludedTables, schemaInfo);
        await ExtractKeysInformation(connection, includedSchemas, excludedTables, schemaInfo);
        
        return schemaInfo.ToString();
    }

    public override Task<Dictionary<string, string>> GetEntityDescriptionsAsync(DataSourceDefinition dataSource)
    {
        return Task.FromResult(new Dictionary<string, string>
        {
            ["movements"] = "Main table tracking all employee movements between positions/departments",
            ["participants"] = "People involved in movements (employees, managers, HR partners)",
            ["job_info"] = "Current and new job information for movements",
            ["contracts"] = "Contract details including schedules and mutual agreements",
            ["history_events"] = "Audit trail of all actions taken on movements",
            ["tags"] = "Categorization tags for movements",
            ["movement_types"] = "Types of movements (permanent, temporary, secondment)",
            ["statuses"] = "Movement statuses (Completed, Expired, Rejected, etc.)",
            ["employee_groups"] = "Employee categories (FullTime, PartTime, Casual)",
            ["cost_centres"] = "Store/office locations with geographic data",
            ["participant_roles"] = "Roles in the movement process (TeamMember, Manager, HR)"
        });
    }

    public override Task<TitleGenerationContext> GetTitleGenerationContextAsync(DataSourceDefinition dataSource)
    {
        var context = new TitleGenerationContext
        {
            Abbreviations = new Dictionary<string, string>
            {
                ["HR"] = "Human Resources",
                ["FT"] = "Full Time",
                ["PT"] = "Part Time",
                ["EA"] = "Enterprise Agreement",
                ["STI"] = "Short Term Incentive",
                ["CPP"] = "Culture People Partner",
                ["STR"] = "Short Term Relief",
                ["CC"] = "Cost Centre"
            },
            
            KeyTerms = new List<string>
            {
                "movement", "transfer", "secondment", "promotion", "position", "role",
                "manager", "approval", "workflow", "salary", "contract", "schedule",
                "permanent", "temporary", "expired", "completed", "rejected",
                "employee", "team member", "cost centre", "department", "banner"
            },
            
            MainEntities = new List<(string Entity, string Description)>
            {
                ("movements", "Employee position changes"),
                ("participants", "People in movement workflow"),
                ("job_info", "Position and salary details"),
                ("contracts", "Work schedules and agreements"),
                ("history_events", "Workflow audit trail"),
                ("cost_centres", "Store/office locations"),
                ("managers", "Approvers and supervisors")
            },
            
            ExampleTitles = new List<string>
            {
                "Active Team Movements",
                "Movement Approval Patterns",
                "Salary Change Analysis",
                "Inter-Store Transfers",
                "Manager Approval Activity",
                "Movement Expiration Trends",
                "Contract Schedule Review",
                "Geographic Movement Flow"
            },
            
            AdditionalContext = "Focus on business terminology like 'movements' rather than technical database terms. Include location names when relevant."
        };
        
        return Task.FromResult(context);
    }

    public override Task<List<QueryExample>> GetQueryExamplesAsync(DataSourceDefinition dataSource)
    {
        return Task.FromResult(new List<QueryExample>
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
        });
    }

    public override Task<string> GetPromptEnhancementsAsync(DataSourceDefinition dataSource)
    {
        return Task.FromResult(@"## Team Movements Specific Guidance:

- When analyzing movements, always join with status and movement_type tables for human-readable values
- The job_info table has is_current flag: true = current position, false = new position
- Participants table includes all people involved (employee, managers, HR partners)
- History events track the complete workflow with timestamps
- Cost centres include geographic data (latitude, longitude, formatted_address)
- Contract information includes schedules and mutual agreement flags
- Tags can contain movement metadata like 'FromBanner:X' or 'ToBanner:Y'
- Always consider using date ranges for temporal analysis
- Include appropriate ORDER BY clauses for consistent results");
    }

    public override async Task<bool> SetupSchemaAsync(DataSourceDefinition dataSource, bool dropIfExists = false)
    {
        try
        {
            await using var connection = new NpgsqlConnection(dataSource.GetConnectionString());
            await connection.OpenAsync();
            
            if (dropIfExists)
            {
                await DropExistingSchema(connection);
            }
            
            // Create schema
            await using var schemaCmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}", connection);
            await schemaCmd.ExecuteNonQueryAsync();
            
            // Create tables
            await CreateLookupTables(connection);
            await CreateMainTables(connection);
            
            // Insert default lookup values
            await InsertDefaultLookupValues(connection);
            
            _logger.LogInformation("Team movements schema setup completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup team movements schema");
            throw;
        }
    }

    private async Task DropExistingSchema(NpgsqlConnection connection)
    {
        await using var dropCmd = new NpgsqlCommand($@"
            DROP SCHEMA IF EXISTS {TEAM_MOVEMENTS_SCHEMA} CASCADE;
        ", connection);
        
        await dropCmd.ExecuteNonQueryAsync();
    }

    private async Task CreateLookupTables(NpgsqlConnection connection)
    {
        var createTablesScript = $@"
            -- Movement Types
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.movement_types (
                id SERIAL PRIMARY KEY,
                type_name VARCHAR(100) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Statuses
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.statuses (
                id SERIAL PRIMARY KEY,
                status_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Employee Groups
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.employee_groups (
                id SERIAL PRIMARY KEY,
                group_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Employee Subgroups
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.employee_subgroups (
                id SERIAL PRIMARY KEY,
                subgroup_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Banners
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.banners (
                id SERIAL PRIMARY KEY,
                banner_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Brands
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.brands (
                id SERIAL PRIMARY KEY,
                brand_code VARCHAR(20) UNIQUE NOT NULL,
                brand_name VARCHAR(100) NOT NULL,
                brand_display_name VARCHAR(100) NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Business Groups
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.business_groups (
                id SERIAL PRIMARY KEY,
                group_code VARCHAR(20) UNIQUE NOT NULL,
                group_name VARCHAR(100),
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Departments
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.departments (
                id SERIAL PRIMARY KEY,
                department_code VARCHAR(20) UNIQUE NOT NULL,
                department_name VARCHAR(100) NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Cost Centres with Geographic Data
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.cost_centres (
                id SERIAL PRIMARY KEY,
                cost_centre_code VARCHAR(20) UNIQUE NOT NULL,
                cost_centre_name VARCHAR(100) NOT NULL,
                formatted_address TEXT,
                latitude DECIMAL(9,6),
                longitude DECIMAL(9,6),
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Participant Roles
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.participant_roles (
                id SERIAL PRIMARY KEY,
                role_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Job Roles
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.job_roles (
                id SERIAL PRIMARY KEY,
                role_name VARCHAR(100) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Mutual Flags
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.mutual_flags (
                id SERIAL PRIMARY KEY,
                flag_name VARCHAR(100) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Break Types
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.break_types (
                id SERIAL PRIMARY KEY,
                break_name VARCHAR(50) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- History Event Types
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.history_event_types (
                id SERIAL PRIMARY KEY,
                event_type_name VARCHAR(100) UNIQUE NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
        ";
        
        await using var cmd = new NpgsqlCommand(createTablesScript, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateMainTables(NpgsqlConnection connection)
    {
        var createTablesScript = $@"
            -- Movements table (main table)
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.movements (
                id SERIAL PRIMARY KEY,
                movement_id VARCHAR(100) UNIQUE NOT NULL,
                employee_id VARCHAR(50) NOT NULL,
                movement_type_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movement_types(id),
                status_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.statuses(id),
                start_date DATE,
                end_date DATE,
                workflow_definition_id VARCHAR(100),
                workflow_version INTEGER,
                workflow_archived BOOLEAN,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Participants table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.participants (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movements(id) ON DELETE CASCADE,
                employee_id VARCHAR(50) NOT NULL,
                name VARCHAR(255),
                position_id VARCHAR(50),
                position_title VARCHAR(255),
                banner_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.banners(id),
                brand_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.brands(id),
                department_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.departments(id),
                cost_centre_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.cost_centres(id),
                role_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.participant_roles(id),
                photo_url TEXT,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Job Info table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.job_info (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movements(id) ON DELETE CASCADE,
                is_current BOOLEAN NOT NULL,
                working_days_per_week DECIMAL(5,2),
                base_hours DECIMAL(5,2),
                employee_group_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.employee_groups(id),
                position_id VARCHAR(50),
                position_title VARCHAR(255),
                banner_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.banners(id),
                brand_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.brands(id),
                business_group_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.business_groups(id),
                cost_centre_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.cost_centres(id),
                employee_subgroup_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.employee_subgroups(id),
                job_role_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.job_roles(id),
                department_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.departments(id),
                salary_amount DECIMAL(10,2),
                salary_min DECIMAL(10,2),
                salary_max DECIMAL(10,2),
                salary_benchmark DECIMAL(10,2),
                discretionary_allowance DECIMAL(10,2),
                sti_target INTEGER,
                manager_employee_id VARCHAR(50),
                manager_name VARCHAR(255),
                manager_position_id VARCHAR(50),
                manager_position_title VARCHAR(255),
                start_date DATE,
                end_date DATE,
                employee_movement_type VARCHAR(50),
                sti_scheme VARCHAR(50),
                pay_scale_group VARCHAR(100),
                pay_scale_level VARCHAR(100),
                leave_entitlement VARCHAR(50),
                leave_entitlement_name VARCHAR(255),
                car_eligibility VARCHAR(100),
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Contracts table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.contracts (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movements(id) ON DELETE CASCADE,
                is_current BOOLEAN NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Contract Mutual Flags table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.contract_mutual_flags (
                id SERIAL PRIMARY KEY,
                contract_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.contracts(id) ON DELETE CASCADE,
                flag_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.mutual_flags(id),
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(contract_id, flag_id)
            );
            
            -- Contract Weeks table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.contract_weeks (
                id SERIAL PRIMARY KEY,
                contract_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.contracts(id) ON DELETE CASCADE,
                week_index INTEGER NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(contract_id, week_index)
            );
            
            -- Daily Schedules table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.daily_schedules (
                id SERIAL PRIMARY KEY,
                contract_week_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.contract_weeks(id) ON DELETE CASCADE,
                day_of_week VARCHAR(3) NOT NULL CHECK (day_of_week IN ('mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun')),
                start_time TIME NOT NULL,
                end_time TIME NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- Schedule Breaks table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.schedule_breaks (
                id SERIAL PRIMARY KEY,
                daily_schedule_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.daily_schedules(id) ON DELETE CASCADE,
                break_type_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.break_types(id),
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            
            -- History Events table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.history_events (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movements(id) ON DELETE CASCADE,
                event_type_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.history_event_types(id),
                event_index INTEGER NOT NULL,
                created_date TIMESTAMP WITH TIME ZONE,
                created_by VARCHAR(50),
                created_by_name VARCHAR(255),
                participant_employee_id VARCHAR(50),
                participant_name VARCHAR(255),
                participant_position_id VARCHAR(50),
                participant_position_title VARCHAR(255),
                participant_role_id INTEGER REFERENCES {TEAM_MOVEMENTS_SCHEMA}.participant_roles(id),
                notes TEXT,
                event_data JSONB,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(movement_id, event_index)
            );
            
            -- Tags table
            CREATE TABLE IF NOT EXISTS {TEAM_MOVEMENTS_SCHEMA}.tags (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES {TEAM_MOVEMENTS_SCHEMA}.movements(id) ON DELETE CASCADE,
                tag_value VARCHAR(255) NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(movement_id, tag_value)
            );
            
            -- Create indexes for performance
            CREATE INDEX IF NOT EXISTS idx_movements_employee_id ON {TEAM_MOVEMENTS_SCHEMA}.movements(employee_id);
            CREATE INDEX IF NOT EXISTS idx_movements_movement_type_id ON {TEAM_MOVEMENTS_SCHEMA}.movements(movement_type_id);
            CREATE INDEX IF NOT EXISTS idx_movements_status_id ON {TEAM_MOVEMENTS_SCHEMA}.movements(status_id);
            CREATE INDEX IF NOT EXISTS idx_movements_start_date ON {TEAM_MOVEMENTS_SCHEMA}.movements(start_date);
            
            CREATE INDEX IF NOT EXISTS idx_participants_movement_id ON {TEAM_MOVEMENTS_SCHEMA}.participants(movement_id);
            CREATE INDEX IF NOT EXISTS idx_participants_employee_id ON {TEAM_MOVEMENTS_SCHEMA}.participants(employee_id);
            CREATE INDEX IF NOT EXISTS idx_participants_role_id ON {TEAM_MOVEMENTS_SCHEMA}.participants(role_id);
            
            CREATE INDEX IF NOT EXISTS idx_job_info_movement_id ON {TEAM_MOVEMENTS_SCHEMA}.job_info(movement_id);
            CREATE INDEX IF NOT EXISTS idx_job_info_is_current ON {TEAM_MOVEMENTS_SCHEMA}.job_info(is_current);
            
            CREATE INDEX IF NOT EXISTS idx_history_events_movement_id ON {TEAM_MOVEMENTS_SCHEMA}.history_events(movement_id);
            CREATE INDEX IF NOT EXISTS idx_history_events_event_type_id ON {TEAM_MOVEMENTS_SCHEMA}.history_events(event_type_id);
            
            CREATE INDEX IF NOT EXISTS idx_tags_movement_id ON {TEAM_MOVEMENTS_SCHEMA}.tags(movement_id);
            CREATE INDEX IF NOT EXISTS idx_tags_tag_value ON {TEAM_MOVEMENTS_SCHEMA}.tags(tag_value);
        ";
        
        await using var cmd = new NpgsqlCommand(createTablesScript, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertDefaultLookupValues(NpgsqlConnection connection)
    {
        var insertScript = $@"
            -- Insert default 'Unknown' values in all lookup tables
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.movement_types (type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.statuses (status_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.employee_groups (group_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.employee_subgroups (subgroup_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.banners (banner_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.brands (brand_code, brand_name, brand_display_name) 
                VALUES ('Unknown', 'Unknown', 'Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.business_groups (group_code, group_name) 
                VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.departments (department_code, department_name) 
                VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.cost_centres (cost_centre_code, cost_centre_name) 
                VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.participant_roles (role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.job_roles (role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.mutual_flags (flag_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.break_types (break_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.history_event_types (event_type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            
            -- Insert common lookup values
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.movement_types (type_name) VALUES 
                ('ContractAndPositionPermanent'),
                ('ContractAndPositionSecondment'),
                ('ContractAndPositionShortTermRelief'),
                ('ContractTemporary'),
                ('ContractPermanent')
            ON CONFLICT DO NOTHING;
            
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.statuses (status_name) VALUES 
                ('Completed'),
                ('Expired'),
                ('Rejected'),
                ('Active'),
                ('Pending'),
                ('Draft')
            ON CONFLICT DO NOTHING;
            
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.employee_groups (group_name) VALUES 
                ('FullTime'),
                ('PartTime'),
                ('Casual')
            ON CONFLICT DO NOTHING;
            
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.employee_subgroups (subgroup_name) VALUES 
                ('Salaried'),
                ('EnterpriseAgreement')
            ON CONFLICT DO NOTHING;
            
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.participant_roles (role_name) VALUES 
                ('TeamMember'),
                ('SendingManager'),
                ('ReceivingManager'),
                ('AdditionalReceivingManager'),
                ('CulturePeoplePartner'),
                ('HigherCulturePeoplePartner'),
                ('HigherSendingManager')
            ON CONFLICT DO NOTHING;
            
            INSERT INTO {TEAM_MOVEMENTS_SCHEMA}.history_event_types (event_type_name) VALUES 
                ('MovementInitiated'),
                ('MovementEdited'),
                ('MovementApproved'),
                ('MovementRejected'),
                ('MovementExpired'),
                ('MovementCompleted')
            ON CONFLICT DO NOTHING;
        ";
        
        await using var cmd = new NpgsqlCommand(insertScript, connection);
        await cmd.ExecuteNonQueryAsync();
    }
}
