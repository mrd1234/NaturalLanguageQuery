# Team Movements Database Schema Guide

## CRITICAL SCHEMA INFORMATION
- 'team_movements' is a SCHEMA name, NOT a table name
- All tables must be referenced with full schema-qualified names, like: team_movements.movements
- NEVER use 'team_movements' alone as a table name - it does not exist as a table

## Table Structure (ALL tables are in the team_movements SCHEMA)
1. team_movements.movement_types (id, type_name)
2. team_movements.movements (id, movement_id, movement_type_id, status_id, start_date, end_date)
3. team_movements.statuses (id, status_name)
4. team_movements.job_info (id, movement_id, is_current, salary_amount, base_hours)
5. team_movements.participants (id, movement_id, role_id, employee_id)
6. team_movements.participant_roles (id, role_name)

## CORRECT EXAMPLE QUERIES

### Count of each movement type
SELECT mt.type_name, COUNT(*) as count
FROM team_movements.movement_types mt
JOIN team_movements.movements m ON mt.id = m.movement_type_id
GROUP BY mt.type_name
ORDER BY count DESC;

### Salary Analysis
SELECT
AVG(new.salary_amount - current.salary_amount) AS avg_increase
FROM
team_movements.job_info new
JOIN
team_movements.job_info current ON new.movement_id = current.movement_id
WHERE
new.is_current = false AND current.is_current = true;

### Average processing time
SELECT
AVG(EXTRACT(EPOCH FROM (completion.created_date - initiation.created_date))/86400) AS avg_days
FROM
team_movements.history_events initiation
JOIN
team_movements.history_events completion ON initiation.movement_id = completion.movement_id
JOIN
team_movements.history_event_types init_type ON initiation.event_type_id = init_type.id
JOIN
team_movements.history_event_types comp_type ON completion.event_type_id = comp_type.id
WHERE
init_type.event_type_name = 'MovementInitiated'
AND comp_type.event_type_name = 'MovementCompleted';