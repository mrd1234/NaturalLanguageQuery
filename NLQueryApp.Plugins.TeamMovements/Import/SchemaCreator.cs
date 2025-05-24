using Npgsql;

namespace NLQueryApp.Plugins.TeamMovements.Import;

public class SchemaCreator
{
    private readonly string _connectionString;
    
    public SchemaCreator(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task CreateSchema(SchemaAnalyzer analyzer)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        
        // Create schema if it doesn't exist
        await using var createSchemaCmd = new NpgsqlCommand(@"
            CREATE SCHEMA IF NOT EXISTS team_movements;
        ", conn);
        await createSchemaCmd.ExecuteNonQueryAsync();
        
        // Create lookup tables and insert default values
        await CreateLookupTables(conn);
        await CreateMainTables(conn);
        await InsertDefaultLookupValues(conn);
    }
    
    private async Task CreateLookupTables(NpgsqlConnection conn)
    {
        // Movement types
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.movement_types (
                id SERIAL PRIMARY KEY,
                type_name VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Statuses
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.statuses (
                id SERIAL PRIMARY KEY,
                status_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // Employee groups
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.employee_groups (
                id SERIAL PRIMARY KEY,
                group_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // Employee subgroups
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.employee_subgroups (
                id SERIAL PRIMARY KEY,
                subgroup_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // Banners
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.banners (
                id SERIAL PRIMARY KEY,
                banner_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // Brands
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.brands (
                id SERIAL PRIMARY KEY,
                brand_code VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Business groups
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.business_groups (
                id SERIAL PRIMARY KEY,
                group_code VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Departments
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.departments (
                id SERIAL PRIMARY KEY,
                department_code VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Cost centres
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.cost_centres (
                id SERIAL PRIMARY KEY,
                cost_centre_code VARCHAR(100) NOT NULL UNIQUE,
                cost_centre_name VARCHAR(200),
                formatted_address TEXT,
                latitude DECIMAL(9,6),
                longitude DECIMAL(9,6)
            );
        ");
        
        // Participant roles
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.participant_roles (
                id SERIAL PRIMARY KEY,
                role_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // Job roles
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.job_roles (
                id SERIAL PRIMARY KEY,
                role_name VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Mutual flags
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.mutual_flags (
                id SERIAL PRIMARY KEY,
                flag_name VARCHAR(100) NOT NULL UNIQUE
            );
        ");
        
        // Break types
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.break_types (
                id SERIAL PRIMARY KEY,
                break_name VARCHAR(50) NOT NULL UNIQUE
            );
        ");
        
        // History event types
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.history_event_types (
                id SERIAL PRIMARY KEY,
                event_type_name VARCHAR(100) NOT NULL UNIQUE
            );
        ");
    }
    
    private async Task CreateMainTables(NpgsqlConnection conn)
    {
        // Movements table
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.movements (
                id SERIAL PRIMARY KEY,
                movement_id VARCHAR(100) NOT NULL UNIQUE,
                employee_id VARCHAR(100) NOT NULL,
                movement_type_id INTEGER NOT NULL REFERENCES team_movements.movement_types(id),
                status_id INTEGER NOT NULL REFERENCES team_movements.statuses(id),
                start_date DATE,
                end_date DATE,
                workflow_definition_id VARCHAR(100),
                workflow_version INTEGER,
                workflow_archived BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        ");
        
        // Participants table
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.participants (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                employee_id VARCHAR(100) NOT NULL,
                name VARCHAR(200),
                position_id VARCHAR(100),
                position_title VARCHAR(200),
                banner_id INTEGER REFERENCES team_movements.banners(id),
                brand_id INTEGER REFERENCES team_movements.brands(id),
                department_id INTEGER REFERENCES team_movements.departments(id),
                cost_centre_id INTEGER REFERENCES team_movements.cost_centres(id),
                role_id INTEGER NOT NULL REFERENCES team_movements.participant_roles(id),
                photo_url TEXT
            );
        ");
        
        // Job info table
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.job_info (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                is_current BOOLEAN NOT NULL,
                working_days_per_week DECIMAL(5,2),
                base_hours DECIMAL(5,2),
                employee_group_id INTEGER REFERENCES team_movements.employee_groups(id),
                position_id VARCHAR(100),
                position_title VARCHAR(200),
                banner_id INTEGER REFERENCES team_movements.banners(id),
                brand_id INTEGER REFERENCES team_movements.brands(id),
                business_group_id INTEGER REFERENCES team_movements.business_groups(id),
                cost_centre_id INTEGER REFERENCES team_movements.cost_centres(id),
                employee_subgroup_id INTEGER REFERENCES team_movements.employee_subgroups(id),
                job_role_id INTEGER REFERENCES team_movements.job_roles(id),
                department_id INTEGER REFERENCES team_movements.departments(id),
                salary_amount DECIMAL(10,2),
                salary_min DECIMAL(10,2),
                salary_max DECIMAL(10,2),
                salary_benchmark DECIMAL(10,2),
                discretionary_allowance DECIMAL(10,2),
                sti_target INTEGER,
                manager_employee_id VARCHAR(100),
                manager_name VARCHAR(200),
                manager_position_id VARCHAR(100),
                manager_position_title VARCHAR(200),
                start_date DATE,
                end_date DATE,
                employee_movement_type VARCHAR(100),
                sti_scheme VARCHAR(100),
                pay_scale_group VARCHAR(100),
                pay_scale_level VARCHAR(100),
                leave_entitlement VARCHAR(100),
                leave_entitlement_name VARCHAR(200),
                car_eligibility VARCHAR(100)
            );
        ");
        
        // Contracts table
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.contracts (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                is_current BOOLEAN NOT NULL
            );
        ");
        
        // Contract mutual flags
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.contract_mutual_flags (
                contract_id INTEGER NOT NULL REFERENCES team_movements.contracts(id) ON DELETE CASCADE,
                flag_id INTEGER NOT NULL REFERENCES team_movements.mutual_flags(id),
                PRIMARY KEY (contract_id, flag_id)
            );
        ");
        
        // Contract weeks
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.contract_weeks (
                id SERIAL PRIMARY KEY,
                contract_id INTEGER NOT NULL REFERENCES team_movements.contracts(id) ON DELETE CASCADE,
                week_index INTEGER NOT NULL
            );
        ");
        
        // Daily schedules
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.daily_schedules (
                id SERIAL PRIMARY KEY,
                contract_week_id INTEGER NOT NULL REFERENCES team_movements.contract_weeks(id) ON DELETE CASCADE,
                day_of_week VARCHAR(3) NOT NULL,
                start_time TIME NOT NULL,
                end_time TIME NOT NULL
            );
        ");
        
        // Schedule breaks
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.schedule_breaks (
                daily_schedule_id INTEGER NOT NULL REFERENCES team_movements.daily_schedules(id) ON DELETE CASCADE,
                break_type_id INTEGER NOT NULL REFERENCES team_movements.break_types(id),
                PRIMARY KEY (daily_schedule_id, break_type_id)
            );
        ");
        
        // History events
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.history_events (
                id SERIAL PRIMARY KEY,
                movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                event_type_id INTEGER NOT NULL REFERENCES team_movements.history_event_types(id),
                event_index INTEGER NOT NULL,
                created_date TIMESTAMP,
                created_by VARCHAR(100),
                created_by_name VARCHAR(200),
                participant_employee_id VARCHAR(100),
                participant_name VARCHAR(200),
                participant_position_id VARCHAR(100),
                participant_position_title VARCHAR(200),
                participant_role_id INTEGER REFERENCES team_movements.participant_roles(id),
                notes TEXT,
                event_data JSONB
            );
        ");
        
        // Tags
        await ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS team_movements.tags (
                movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                tag_value VARCHAR(200) NOT NULL,
                PRIMARY KEY (movement_id, tag_value)
            );
        ");
        
        // Create indexes for better performance
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_movements_employee_id ON team_movements.movements(employee_id);");
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_movements_status_id ON team_movements.movements(status_id);");
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_movements_start_date ON team_movements.movements(start_date);");
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_participants_movement_id ON team_movements.participants(movement_id);");
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_job_info_movement_id ON team_movements.job_info(movement_id);");
        await ExecuteNonQuery(conn, "CREATE INDEX IF NOT EXISTS idx_history_events_movement_id ON team_movements.history_events(movement_id);");
    }
    
    private async Task InsertDefaultLookupValues(NpgsqlConnection conn)
    {
        // Insert default movement types
        await InsertLookupValue(conn, "movement_types", "type_name", "Unknown");
        await InsertLookupValue(conn, "movement_types", "type_name", "Permanent");
        await InsertLookupValue(conn, "movement_types", "type_name", "Temporary");
        await InsertLookupValue(conn, "movement_types", "type_name", "Secondment");
        
        // Insert default statuses
        await InsertLookupValue(conn, "statuses", "status_name", "Unknown");
        await InsertLookupValue(conn, "statuses", "status_name", "Completed");
        await InsertLookupValue(conn, "statuses", "status_name", "Expired");
        await InsertLookupValue(conn, "statuses", "status_name", "Rejected");
        await InsertLookupValue(conn, "statuses", "status_name", "Active");
        await InsertLookupValue(conn, "statuses", "status_name", "Pending");
        
        // Insert default employee groups
        await InsertLookupValue(conn, "employee_groups", "group_name", "Unknown");
        
        // Insert default employee subgroups
        await InsertLookupValue(conn, "employee_subgroups", "subgroup_name", "Unknown");
        
        // Insert default banners
        await InsertLookupValue(conn, "banners", "banner_name", "Unknown");
        
        // Insert default brands
        await InsertLookupValue(conn, "brands", "brand_code", "Unknown");
        
        // Insert default business groups
        await InsertLookupValue(conn, "business_groups", "group_code", "Unknown");
        
        // Insert default departments
        await InsertLookupValue(conn, "departments", "department_code", "Unknown");
        
        // Insert default cost centres
        await InsertLookupValue(conn, "cost_centres", "cost_centre_code", "Unknown", "cost_centre_name", "Unknown");
        
        // Insert default participant roles
        await InsertLookupValue(conn, "participant_roles", "role_name", "Unknown");
        await InsertLookupValue(conn, "participant_roles", "role_name", "TeamMember");
        await InsertLookupValue(conn, "participant_roles", "role_name", "SendingManager");
        await InsertLookupValue(conn, "participant_roles", "role_name", "ReceivingManager");
        await InsertLookupValue(conn, "participant_roles", "role_name", "HRPartner");
        
        // Insert default job roles
        await InsertLookupValue(conn, "job_roles", "role_name", "Unknown");
        
        // Insert default mutual flags
        await InsertLookupValue(conn, "mutual_flags", "flag_name", "Unknown");
        
        // Insert default break types
        await InsertLookupValue(conn, "break_types", "break_name", "Unknown");
        
        // Insert default history event types
        await InsertLookupValue(conn, "history_event_types", "event_type_name", "Unknown");
        await InsertLookupValue(conn, "history_event_types", "event_type_name", "MovementCreated");
        await InsertLookupValue(conn, "history_event_types", "event_type_name", "MovementApproved");
        await InsertLookupValue(conn, "history_event_types", "event_type_name", "MovementRejected");
        await InsertLookupValue(conn, "history_event_types", "event_type_name", "MovementCompleted");
    }
    
    private async Task ExecuteNonQuery(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task InsertLookupValue(NpgsqlConnection conn, string tableName, string columnName, string value, string? secondColumnName = null, string? secondValue = null)
    {
        string sql;
        if (secondColumnName != null && secondValue != null)
        {
            sql = $@"
                INSERT INTO team_movements.{tableName} ({columnName}, {secondColumnName}) 
                VALUES (@value, @secondValue)
                ON CONFLICT ({columnName}) DO NOTHING;
            ";
        }
        else
        {
            sql = $@"
                INSERT INTO team_movements.{tableName} ({columnName}) 
                VALUES (@value)
                ON CONFLICT ({columnName}) DO NOTHING;
            ";
        }
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@value", value);
        if (secondValue != null)
        {
            cmd.Parameters.AddWithValue("@secondValue", secondValue);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}
