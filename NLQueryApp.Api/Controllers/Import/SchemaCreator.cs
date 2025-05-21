using System.Collections.Concurrent;
using Npgsql;

namespace NLQueryApp.Api.Controllers.Import;

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
            
        // Create schemas
        await ExecuteNonQuery(conn, @"
                CREATE SCHEMA IF NOT EXISTS team_movements;
            ");
            
        // Create lookup tables
        await CreateLookupTables(conn);
            
        // Create main tables
        await CreateMainTables(conn);
            
        // Insert lookup values
        await InsertLookupValues(conn, analyzer);
    }
        
    private async Task CreateLookupTables(NpgsqlConnection conn)
    {
        // Movement Types
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.movement_types (
                    id SERIAL PRIMARY KEY,
                    type_name VARCHAR(100) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.movement_types IS 'Lookup table for movement types (e.g. ContractAndPositionPermanent, ContractAndPositionShortTermRelief)';
                COMMENT ON COLUMN team_movements.movement_types.type_name IS 'Name of the movement type';
            ");
            
        // Statuses
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.statuses (
                    id SERIAL PRIMARY KEY,
                    status_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.statuses IS 'Lookup table for movement statuses (e.g. Completed, Expired, Rejected)';
                COMMENT ON COLUMN team_movements.statuses.status_name IS 'Name of the status';
            ");
            
        // Employee Groups
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.employee_groups (
                    id SERIAL PRIMARY KEY,
                    group_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.employee_groups IS 'Lookup table for employee groups (e.g. FullTime, PartTime)';
                COMMENT ON COLUMN team_movements.employee_groups.group_name IS 'Name of the employee group';
            ");
            
        // Employee Subgroups
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.employee_subgroups (
                    id SERIAL PRIMARY KEY,
                    subgroup_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.employee_subgroups IS 'Lookup table for employee subgroups (e.g. Salaried, EnterpriseAgreement)';
                COMMENT ON COLUMN team_movements.employee_subgroups.subgroup_name IS 'Name of the employee subgroup';
            ");
            
        // Banners
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.banners (
                    id SERIAL PRIMARY KEY,
                    banner_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.banners IS 'Lookup table for banners/business units (e.g. FoodGroup, BigW)';
                COMMENT ON COLUMN team_movements.banners.banner_name IS 'Name of the banner';
            ");
            
        // Brands
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.brands (
                    id SERIAL PRIMARY KEY,
                    brand_code VARCHAR(20) UNIQUE NOT NULL,
                    brand_name VARCHAR(100) NOT NULL,
                    brand_display_name VARCHAR(100) NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.brands IS 'Lookup table for brands (e.g. Supermarkets, BIG W)';
                COMMENT ON COLUMN team_movements.brands.brand_code IS 'Code of the brand';
                COMMENT ON COLUMN team_movements.brands.brand_name IS 'Name of the brand';
                COMMENT ON COLUMN team_movements.brands.brand_display_name IS 'Display name of the brand';
            ");
            
        // Groups
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.business_groups (
                    id SERIAL PRIMARY KEY,
                    group_code VARCHAR(20) UNIQUE NOT NULL,
                    group_name VARCHAR(100),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.business_groups IS 'Lookup table for business groups (e.g. B2C Food, More Everyday)';
                COMMENT ON COLUMN team_movements.business_groups.group_code IS 'Code of the business group';
                COMMENT ON COLUMN team_movements.business_groups.group_name IS 'Name of the business group';
            ");
            
        // Departments
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.departments (
                    id SERIAL PRIMARY KEY,
                    department_code VARCHAR(20) UNIQUE NOT NULL,
                    department_name VARCHAR(100) NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.departments IS 'Lookup table for departments (e.g. Store Management, Grocery)';
                COMMENT ON COLUMN team_movements.departments.department_code IS 'Code of the department';
                COMMENT ON COLUMN team_movements.departments.department_name IS 'Name of the department';
            ");
            
        // Cost Centres
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.cost_centres (
                    id SERIAL PRIMARY KEY,
                    cost_centre_code VARCHAR(20) UNIQUE NOT NULL,
                    cost_centre_name VARCHAR(100) NOT NULL,
                    formatted_address TEXT,
                    latitude DECIMAL(9,6),
                    longitude DECIMAL(9,6),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.cost_centres IS 'Lookup table for cost centres (e.g. stores, regional offices)';
                COMMENT ON COLUMN team_movements.cost_centres.cost_centre_code IS 'Code of the cost centre';
                COMMENT ON COLUMN team_movements.cost_centres.cost_centre_name IS 'Name of the cost centre';
                COMMENT ON COLUMN team_movements.cost_centres.formatted_address IS 'Full address of the cost centre';
                COMMENT ON COLUMN team_movements.cost_centres.latitude IS 'Latitude coordinate of the cost centre';
                COMMENT ON COLUMN team_movements.cost_centres.longitude IS 'Longitude coordinate of the cost centre';
            ");
            
        // Participant Roles
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.participant_roles (
                    id SERIAL PRIMARY KEY,
                    role_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.participant_roles IS 'Lookup table for participant roles (e.g. TeamMember, SendingManager, ReceivingManager)';
                COMMENT ON COLUMN team_movements.participant_roles.role_name IS 'Name of the participant role';
            ");
            
        // Job Roles
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.job_roles (
                    id SERIAL PRIMARY KEY,
                    role_name VARCHAR(100) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.job_roles IS 'Lookup table for job roles (e.g. TeamMember, StoreManager, DepartmentManager)';
                COMMENT ON COLUMN team_movements.job_roles.role_name IS 'Name of the job role';
            ");
            
        // Mutual Flags
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.mutual_flags (
                    id SERIAL PRIMARY KEY,
                    flag_name VARCHAR(100) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.mutual_flags IS 'Lookup table for mutual flags (e.g. OutsideOrdinaryHours, NonConsecutiveWeekendDaysOff)';
                COMMENT ON COLUMN team_movements.mutual_flags.flag_name IS 'Name of the mutual flag';
            ");
            
        // Break Types
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.break_types (
                    id SERIAL PRIMARY KEY,
                    break_name VARCHAR(50) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.break_types IS 'Lookup table for break types (e.g. Unpaid30Mins, Unpaid60Mins)';
                COMMENT ON COLUMN team_movements.break_types.break_name IS 'Name of the break type';
            ");
            
        // History Event Types
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.history_event_types (
                    id SERIAL PRIMARY KEY,
                    event_type_name VARCHAR(100) UNIQUE NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.history_event_types IS 'Lookup table for history event types (e.g. MovementInitiated, MovementApproved)';
                COMMENT ON COLUMN team_movements.history_event_types.event_type_name IS 'Name of the history event type';
            ");
    }
        
    private async Task CreateMainTables(NpgsqlConnection conn)
    {
        // Movements table (main table) - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.movements (
                    id SERIAL PRIMARY KEY,
                    movement_id VARCHAR(100) UNIQUE NOT NULL,
                    employee_id VARCHAR(50) NOT NULL,
                    movement_type_id INTEGER REFERENCES team_movements.movement_types(id),
                    status_id INTEGER REFERENCES team_movements.statuses(id),
                    start_date DATE,
                    end_date DATE,
                    workflow_definition_id VARCHAR(100),
                    workflow_version INTEGER,
                    workflow_archived BOOLEAN,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.movements IS 'Main table for team movements';
                COMMENT ON COLUMN team_movements.movements.movement_id IS 'Unique identifier for the movement';
                COMMENT ON COLUMN team_movements.movements.employee_id IS 'ID of the employee being moved';
                COMMENT ON COLUMN team_movements.movements.movement_type_id IS 'Type of movement (foreign key to team_movements.movement_types)';
                COMMENT ON COLUMN team_movements.movements.status_id IS 'Current status of the movement (foreign key to team_movements.statuses)';
                COMMENT ON COLUMN team_movements.movements.start_date IS 'Start date of the movement';
                COMMENT ON COLUMN team_movements.movements.end_date IS 'End date of the movement (if applicable)';
                COMMENT ON COLUMN team_movements.movements.workflow_definition_id IS 'Workflow definition ID';
                COMMENT ON COLUMN team_movements.movements.workflow_version IS 'Workflow version';
                COMMENT ON COLUMN team_movements.movements.workflow_archived IS 'Whether the workflow is archived';
            ");
            
        // Participants table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.participants (
                    id SERIAL PRIMARY KEY,
                    movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                    employee_id VARCHAR(50) NOT NULL,
                    name VARCHAR(255),
                    position_id VARCHAR(50),
                    position_title VARCHAR(255),
                    banner_id INTEGER REFERENCES team_movements.banners(id),
                    brand_id INTEGER REFERENCES team_movements.brands(id),
                    department_id INTEGER REFERENCES team_movements.departments(id),
                    cost_centre_id INTEGER REFERENCES team_movements.cost_centres(id),
                    role_id INTEGER REFERENCES team_movements.participant_roles(id),
                    photo_url TEXT,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.participants IS 'Participants involved in a movement (employee, managers, etc.)';
                COMMENT ON COLUMN team_movements.participants.movement_id IS 'Movement this participant is associated with';
                COMMENT ON COLUMN team_movements.participants.employee_id IS 'Employee ID of the participant';
                COMMENT ON COLUMN team_movements.participants.name IS 'Full name of the participant';
                COMMENT ON COLUMN team_movements.participants.position_id IS 'Position ID of the participant';
                COMMENT ON COLUMN team_movements.participants.position_title IS 'Title of the participant''s position';
                COMMENT ON COLUMN team_movements.participants.banner_id IS 'Banner the participant belongs to';
                COMMENT ON COLUMN team_movements.participants.brand_id IS 'Brand the participant belongs to';
                COMMENT ON COLUMN team_movements.participants.department_id IS 'Department the participant belongs to';
                COMMENT ON COLUMN team_movements.participants.cost_centre_id IS 'Cost centre the participant belongs to';
                COMMENT ON COLUMN team_movements.participants.role_id IS 'Role of the participant in this movement';
                COMMENT ON COLUMN team_movements.participants.photo_url IS 'URL to the participant''s photo';
            ");
            
        // Job Info table - corrected schema name and changed problematic columns to DECIMAL
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.job_info (
                    id SERIAL PRIMARY KEY,
                    movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                    is_current BOOLEAN NOT NULL,
                    working_days_per_week DECIMAL(5,2),
                    base_hours DECIMAL(5,2),
                    employee_group_id INTEGER REFERENCES team_movements.employee_groups(id),
                    position_id VARCHAR(50),
                    position_title VARCHAR(255),
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
                COMMENT ON TABLE team_movements.job_info IS 'Job information for current and new positions';
                COMMENT ON COLUMN team_movements.job_info.movement_id IS 'Movement this job info is associated with';
                COMMENT ON COLUMN team_movements.job_info.is_current IS 'Whether this is the current (true) or new (false) job info';
                COMMENT ON COLUMN team_movements.job_info.working_days_per_week IS 'Number of working days per week';
                COMMENT ON COLUMN team_movements.job_info.base_hours IS 'Base hours per week';
                COMMENT ON COLUMN team_movements.job_info.employee_group_id IS 'Employee group (Full-time, Part-time, etc.)';
                COMMENT ON COLUMN team_movements.job_info.position_id IS 'Position ID';
                COMMENT ON COLUMN team_movements.job_info.position_title IS 'Position title';
                COMMENT ON COLUMN team_movements.job_info.banner_id IS 'Banner of the position';
                COMMENT ON COLUMN team_movements.job_info.brand_id IS 'Brand of the position';
                COMMENT ON COLUMN team_movements.job_info.business_group_id IS 'Business group of the position';
                COMMENT ON COLUMN team_movements.job_info.cost_centre_id IS 'Cost centre of the position';
                COMMENT ON COLUMN team_movements.job_info.employee_subgroup_id IS 'Employee subgroup';
                COMMENT ON COLUMN team_movements.job_info.job_role_id IS 'Job role';
                COMMENT ON COLUMN team_movements.job_info.department_id IS 'Department';
                COMMENT ON COLUMN team_movements.job_info.salary_amount IS 'Actual salary amount';
                COMMENT ON COLUMN team_movements.job_info.salary_min IS 'Minimum salary for the position';
                COMMENT ON COLUMN team_movements.job_info.salary_max IS 'Maximum salary for the position';
                COMMENT ON COLUMN team_movements.job_info.salary_benchmark IS 'Benchmark salary for the position';
                COMMENT ON COLUMN team_movements.job_info.discretionary_allowance IS 'Discretionary allowance amount';
                COMMENT ON COLUMN team_movements.job_info.sti_target IS 'Short-term incentive target percentage';
                COMMENT ON COLUMN team_movements.job_info.manager_employee_id IS 'Employee ID of the manager';
                COMMENT ON COLUMN team_movements.job_info.manager_name IS 'Name of the manager';
                COMMENT ON COLUMN team_movements.job_info.manager_position_id IS 'Position ID of the manager';
                COMMENT ON COLUMN team_movements.job_info.manager_position_title IS 'Position title of the manager';
                COMMENT ON COLUMN team_movements.job_info.start_date IS 'Start date of the job';
                COMMENT ON COLUMN team_movements.job_info.end_date IS 'End date of the job (if applicable)';
                COMMENT ON COLUMN team_movements.job_info.employee_movement_type IS 'Type of employee movement';
                COMMENT ON COLUMN team_movements.job_info.sti_scheme IS 'Short-term incentive scheme';
                COMMENT ON COLUMN team_movements.job_info.pay_scale_group IS 'Pay scale group';
                COMMENT ON COLUMN team_movements.job_info.pay_scale_level IS 'Pay scale level';
                COMMENT ON COLUMN team_movements.job_info.leave_entitlement IS 'Leave entitlement code';
                COMMENT ON COLUMN team_movements.job_info.leave_entitlement_name IS 'Leave entitlement description';
                COMMENT ON COLUMN team_movements.job_info.car_eligibility IS 'Car eligibility';
            ");
            
        // Contracts table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.contracts (
                    id SERIAL PRIMARY KEY,
                    movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                    is_current BOOLEAN NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.contracts IS 'Contracts for current and new positions';
                COMMENT ON COLUMN team_movements.contracts.movement_id IS 'Movement this contract is associated with';
                COMMENT ON COLUMN team_movements.contracts.is_current IS 'Whether this is the current (true) or new (false) contract';
            ");
            
        // Contract Mutual Flags table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.contract_mutual_flags (
                    id SERIAL PRIMARY KEY,
                    contract_id INTEGER NOT NULL REFERENCES team_movements.contracts(id) ON DELETE CASCADE,
                    flag_id INTEGER NOT NULL REFERENCES team_movements.mutual_flags(id),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(contract_id, flag_id)
                );
                COMMENT ON TABLE team_movements.contract_mutual_flags IS 'Mutual flags for contracts';
                COMMENT ON COLUMN team_movements.contract_mutual_flags.contract_id IS 'Contract associated with this flag';
                COMMENT ON COLUMN team_movements.contract_mutual_flags.flag_id IS 'Flag associated with this contract';
            ");
            
        // Contract Weeks table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.contract_weeks (
                    id SERIAL PRIMARY KEY,
                    contract_id INTEGER NOT NULL REFERENCES team_movements.contracts(id) ON DELETE CASCADE,
                    week_index INTEGER NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(contract_id, week_index)
                );
                COMMENT ON TABLE team_movements.contract_weeks IS 'Weeks for contracts (rotating rosters)';
                COMMENT ON COLUMN team_movements.contract_weeks.contract_id IS 'Contract associated with this week';
                COMMENT ON COLUMN team_movements.contract_weeks.week_index IS 'Index of this week in the rotating schedule';
            ");
            
        // Daily Schedules table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.daily_schedules (
                    id SERIAL PRIMARY KEY,
                    contract_week_id INTEGER NOT NULL REFERENCES team_movements.contract_weeks(id) ON DELETE CASCADE,
                    day_of_week VARCHAR(3) NOT NULL CHECK (day_of_week IN ('mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun')),
                    start_time TIME NOT NULL,
                    end_time TIME NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.daily_schedules IS 'Daily schedules for contract weeks';
                COMMENT ON COLUMN team_movements.daily_schedules.contract_week_id IS 'Contract week associated with this schedule';
                COMMENT ON COLUMN team_movements.daily_schedules.day_of_week IS 'Day of the week (mon, tue, wed, thu, fri, sat, sun)';
                COMMENT ON COLUMN team_movements.daily_schedules.start_time IS 'Start time of the shift';
                COMMENT ON COLUMN team_movements.daily_schedules.end_time IS 'End time of the shift';
            ");
            
        // Schedule Breaks table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.schedule_breaks (
                    id SERIAL PRIMARY KEY,
                    daily_schedule_id INTEGER NOT NULL REFERENCES team_movements.daily_schedules(id) ON DELETE CASCADE,
                    break_type_id INTEGER NOT NULL REFERENCES team_movements.break_types(id),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                COMMENT ON TABLE team_movements.schedule_breaks IS 'Breaks for daily schedules';
                COMMENT ON COLUMN team_movements.schedule_breaks.daily_schedule_id IS 'Daily schedule associated with this break';
                COMMENT ON COLUMN team_movements.schedule_breaks.break_type_id IS 'Type of break';
            ");
            
        // History Events table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.history_events (
                    id SERIAL PRIMARY KEY,
                    movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                    event_type_id INTEGER NOT NULL REFERENCES team_movements.history_event_types(id),
                    event_index INTEGER NOT NULL,
                    created_date TIMESTAMP WITH TIME ZONE,
                    created_by VARCHAR(50),
                    created_by_name VARCHAR(255),
                    participant_employee_id VARCHAR(50),
                    participant_name VARCHAR(255),
                    participant_position_id VARCHAR(50),
                    participant_position_title VARCHAR(255),
                    participant_role_id INTEGER REFERENCES team_movements.participant_roles(id),
                    notes TEXT,
                    event_data JSONB,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(movement_id, event_index)
                );
                COMMENT ON TABLE team_movements.history_events IS 'History events for movements';
                COMMENT ON COLUMN team_movements.history_events.movement_id IS 'Movement associated with this event';
                COMMENT ON COLUMN team_movements.history_events.event_type_id IS 'Type of event';
                COMMENT ON COLUMN team_movements.history_events.event_index IS 'Index of this event in the history';
                COMMENT ON COLUMN team_movements.history_events.created_date IS 'Date the event was created';
                COMMENT ON COLUMN team_movements.history_events.created_by IS 'Employee ID of the creator';
                COMMENT ON COLUMN team_movements.history_events.created_by_name IS 'Name of the creator';
                COMMENT ON COLUMN team_movements.history_events.participant_employee_id IS 'Employee ID of the participant';
                COMMENT ON COLUMN team_movements.history_events.participant_name IS 'Name of the participant';
                COMMENT ON COLUMN team_movements.history_events.participant_position_id IS 'Position ID of the participant';
                COMMENT ON COLUMN team_movements.history_events.participant_position_title IS 'Position title of the participant';
                COMMENT ON COLUMN team_movements.history_events.participant_role_id IS 'Role of the participant';
                COMMENT ON COLUMN team_movements.history_events.notes IS 'Notes for the event';
                COMMENT ON COLUMN team_movements.history_events.event_data IS 'Additional JSON data for the event';
            ");
            
        // Tags table - corrected schema name
        await ExecuteNonQuery(conn, @"
                CREATE TABLE IF NOT EXISTS team_movements.tags (
                    id SERIAL PRIMARY KEY,
                    movement_id INTEGER NOT NULL REFERENCES team_movements.movements(id) ON DELETE CASCADE,
                    tag_value VARCHAR(255) NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(movement_id, tag_value)
                );
                COMMENT ON TABLE team_movements.tags IS 'Tags for movements';
                COMMENT ON COLUMN team_movements.tags.movement_id IS 'Movement associated with this tag';
                COMMENT ON COLUMN team_movements.tags.tag_value IS 'Value of the tag';
            ");
            
        // Indexes for performance
        await ExecuteNonQuery(conn, @"
                CREATE INDEX IF NOT EXISTS idx_movements_employee_id ON team_movements.movements(employee_id);
                CREATE INDEX IF NOT EXISTS idx_movements_movement_type_id ON team_movements.movements(movement_type_id);
                CREATE INDEX IF NOT EXISTS idx_movements_status_id ON team_movements.movements(status_id);
                CREATE INDEX IF NOT EXISTS idx_movements_start_date ON team_movements.movements(start_date);
                
                CREATE INDEX IF NOT EXISTS idx_participants_movement_id ON team_movements.participants(movement_id);
                CREATE INDEX IF NOT EXISTS idx_participants_employee_id ON team_movements.participants(employee_id);
                CREATE INDEX IF NOT EXISTS idx_participants_role_id ON team_movements.participants(role_id);
                
                CREATE INDEX IF NOT EXISTS idx_job_info_movement_id ON team_movements.job_info(movement_id);
                CREATE INDEX IF NOT EXISTS idx_job_info_is_current ON team_movements.job_info(is_current);
                
                CREATE INDEX IF NOT EXISTS idx_contracts_movement_id ON team_movements.contracts(movement_id);
                CREATE INDEX IF NOT EXISTS idx_contracts_is_current ON team_movements.contracts(is_current);
                
                CREATE INDEX IF NOT EXISTS idx_contract_mutual_flags_contract_id ON team_movements.contract_mutual_flags(contract_id);
                
                CREATE INDEX IF NOT EXISTS idx_contract_weeks_contract_id ON team_movements.contract_weeks(contract_id);
                
                CREATE INDEX IF NOT EXISTS idx_daily_schedules_contract_week_id ON team_movements.daily_schedules(contract_week_id);
                CREATE INDEX IF NOT EXISTS idx_daily_schedules_day_of_week ON team_movements.daily_schedules(day_of_week);
                
                CREATE INDEX IF NOT EXISTS idx_schedule_breaks_daily_schedule_id ON team_movements.schedule_breaks(daily_schedule_id);
                
                CREATE INDEX IF NOT EXISTS idx_history_events_movement_id ON team_movements.history_events(movement_id);
                CREATE INDEX IF NOT EXISTS idx_history_events_event_type_id ON team_movements.history_events(event_type_id);
                
                CREATE INDEX IF NOT EXISTS idx_tags_movement_id ON team_movements.tags(movement_id);
                CREATE INDEX IF NOT EXISTS idx_tags_tag_value ON team_movements.tags(tag_value);
            ");
    }
        
    private async Task InsertLookupValues(NpgsqlConnection conn, SchemaAnalyzer analyzer)
    {
        // Insert default 'Unknown' value in all lookup tables
        await ExecuteNonQuery(conn, @"
                INSERT INTO team_movements.movement_types (type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.statuses (status_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.employee_groups (group_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.employee_subgroups (subgroup_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.banners (banner_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.brands (brand_code, brand_name, brand_display_name) 
                    VALUES ('Unknown', 'Unknown', 'Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.business_groups (group_code, group_name) 
                    VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.departments (department_code, department_name) 
                    VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.cost_centres (cost_centre_code, cost_centre_name) 
                    VALUES ('Unknown', 'Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.participant_roles (role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.job_roles (role_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.mutual_flags (flag_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.break_types (break_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
                INSERT INTO team_movements.history_event_types (event_type_name) VALUES ('Unknown') ON CONFLICT DO NOTHING;
            ");
            
        // Insert values from analyzer
        await InsertLookupValuesFromSet(conn, "movement_types", "type_name", analyzer.MovementTypes);
        await InsertLookupValuesFromSet(conn, "statuses", "status_name", analyzer.Statuses);
        await InsertLookupValuesFromSet(conn, "employee_groups", "group_name", analyzer.EmployeeGroups);
        await InsertLookupValuesFromSet(conn, "employee_subgroups", "subgroup_name", analyzer.EmployeeSubgroups);
        await InsertLookupValuesFromSet(conn, "banners", "banner_name", analyzer.Banners);
        await InsertBrands(conn, analyzer.Brands);
        await InsertLookupValuesFromSet(conn, "business_groups", "group_code", analyzer.Groups);
        await InsertDepartments(conn, analyzer.Departments);
        await InsertCostCentres(conn, analyzer.CostCentres);
        await InsertLookupValuesFromSet(conn, "participant_roles", "role_name", analyzer.ParticipantRoles);
        await InsertLookupValuesFromSet(conn, "job_roles", "role_name", analyzer.JobRoles);
        await InsertLookupValuesFromSet(conn, "mutual_flags", "flag_name", analyzer.MutualFlags);
        await InsertLookupValuesFromSet(conn, "break_types", "break_name", analyzer.BreakTypes);
        await InsertLookupValuesFromSet(conn, "history_event_types", "event_type_name", analyzer.HistoryEventTypes);
    }
        
    private async Task InsertLookupValuesFromSet(NpgsqlConnection conn, string tableName, string columnName, 
        ConcurrentDictionary<string, HashSet<string>> values)
    {
        if (values.TryGetValue("values", out var valueSet))
        {
            foreach (var value in valueSet)
            {
                if (string.IsNullOrEmpty(value)) continue;
                    
                await ExecuteNonQuery(conn, $@"
                        INSERT INTO team_movements.{tableName} ({columnName}) 
                        VALUES (@value)
                        ON CONFLICT DO NOTHING;
                    ", new NpgsqlParameter("@value", value));
            }
        }
    }
        
    private async Task InsertBrands(NpgsqlConnection conn, ConcurrentDictionary<string, HashSet<LookupItem>> brands)
    {
        if (brands.TryGetValue("values", out var brandSet))
        {
            foreach (var brand in brandSet)
            {
                await ExecuteNonQuery(conn, @"
                        INSERT INTO team_movements.brands (brand_code, brand_name, brand_display_name)
                        VALUES (@code, @name, @displayName)
                        ON CONFLICT (brand_code) DO NOTHING;
                    ", 
                    new NpgsqlParameter("@code", brand.Code),
                    new NpgsqlParameter("@name", brand.Name),
                    new NpgsqlParameter("@displayName", brand.DisplayName));
            }
        }
    }
        
    private async Task InsertDepartments(NpgsqlConnection conn, ConcurrentDictionary<string, HashSet<LookupItem>> departments)
    {
        if (departments.TryGetValue("values", out var deptSet))
        {
            foreach (var dept in deptSet)
            {
                await ExecuteNonQuery(conn, @"
                        INSERT INTO team_movements.departments (department_code, department_name)
                        VALUES (@code, @name)
                        ON CONFLICT (department_code) DO NOTHING;
                    ", 
                    new NpgsqlParameter("@code", dept.Code),
                    new NpgsqlParameter("@name", dept.Name));
            }
        }
    }
        
    private async Task InsertCostCentres(NpgsqlConnection conn, ConcurrentDictionary<string, HashSet<LookupItem>> costCentres)
    {
        if (costCentres.TryGetValue("values", out var ccSet))
        {
            foreach (var cc in ccSet)
            {
                await ExecuteNonQuery(conn, @"
                        INSERT INTO team_movements.cost_centres (cost_centre_code, cost_centre_name)
                        VALUES (@code, @name)
                        ON CONFLICT (cost_centre_code) DO NOTHING;
                    ", 
                    new NpgsqlParameter("@code", cc.Code),
                    new NpgsqlParameter("@name", cc.Name));
            }
        }
    }
        
    private async Task<int> ExecuteNonQuery(NpgsqlConnection conn, string sql, params NpgsqlParameter[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (parameters != null)
        {
            cmd.Parameters.AddRange(parameters);
        }
        return await cmd.ExecuteNonQueryAsync();
    }
}