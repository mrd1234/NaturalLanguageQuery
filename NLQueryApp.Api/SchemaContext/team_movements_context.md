# Team Movements Schema Context Enhancement

## Domain Overview

The Team Movements system tracks employee position and contract changes within Woolworths Group. These "movements" include:

- **Permanent position transfers**: When an employee permanently changes positions within the company
- **Secondments**: Temporary assignments to different positions with an end date
- **Contract changes**: Modifications to hours, schedules, or salary adjustments
- **Employment type changes**: Transitions between full-time, part-time, and casual employment status

Each movement follows an approval workflow involving multiple participants (employees) in different roles.

## Key Entities and Relationships

### Movement Record (`team_movements.movement`)
- Central entity representing a requested employment change
- **Important:** A movement can be either position-based, contract-based, or both (indicated by `movement_type`)
- **Status progression:** Typically follows Initiated → Approved → Completed (or alternatively Rejected/Expired)
- **Implicit rule:** `end_date` only applies to temporary movements (secondments)
- **Tags:** Provide useful categorization with From/To pairs for various attributes

### Participants (`team_movements.participant`)
- **Key relationship:** Each movement involves multiple employees in different roles
- **Role significance:**
    - `TeamMember` - The employee being moved (the subject of the movement)
    - `SendingManager` - Current manager approving the move
    - `ReceivingManager` - New manager approving the move
    - `CulturePeoplePartner` - HR representative involved in approval workflow
    - Other specialized roles like `AdditionalReceivingManager`, `HigherSendingManager`
- **Important:** The same employee may appear in multiple movement records with different roles

### Job Information (`team_movements.job_info`)
- Holds both current (`is_current=true`) and new (`is_current=false`) job details
- **Implicit relationship:** `position_id` links to positions but actual role information may differ
- **Special considerations:**
    - Salary changes are important business metrics
    - Base hours changes impact employment classification
    - Discretionary allowances represent additional compensation

### Contracts (`team_movements.contract` and related tables)
- Represents work schedules across multiple weeks
- **Complex pattern:** A contract can have multiple week patterns that rotate
- **Business significance:** Mutual flags indicate special conditions like `OutsideOrdinaryHours` that may trigger allowances
- Shifts are stored with start and end times plus breaks, organized by days of week

### Movement History (`team_movements.movement_history`)
- Chronological record of all actions on a movement
- Critical for audit and troubleshooting
- **Pattern:** Most queries should include approvers and timestamps
- `additional_data` contains event-specific JSON data that varies by event type

## Common Query Patterns

When analyzing team movements data, these patterns are most valuable:

1. **Movement Analytics by Type:**
    - Count/analyze movements by type, status, and time period
    - Example: "How many permanent transfers occurred in Q2 2024?"
    - Example: "Show all expired movements from April 2023"

2. **Salary Impact Analysis:**
    - Compare old vs. new salaries across movements
    - Calculate total salary impact by department or cost center
    - Example: "What was the average salary increase for employees moving to Assistant Store Manager?"
    - Example: "Total salary cost impact of all completed movements in March 2024"

3. **Workflow Efficiency:**
    - Time between initiation and completion
    - Identify bottlenecks in approval process
    - Example: "What's the average approval time for store-to-store transfers?"
    - Example: "Which managers take longest to approve movements?"

4. **Organization Structure Changes:**
    - Track department, cost center, or banner changes
    - Monitor manager-subordinate relationship changes
    - Example: "Show all employees who moved from BigW to FoodGroup in 2023"
    - Example: "List all cost centers that lost more than 5 employees in 2024"

5. **Contract Pattern Analysis:**
    - Changes in working hours or schedules
    - Prevalence of special conditions (flags)
    - Example: "How many employees had their hours reduced in the past month?"
    - Example: "Which stores have the most employees working outside ordinary hours?"

## Data Characteristics

- **Employee IDs:** 6-digit numeric strings stored as VARCHAR (e.g., "105016", "107021")
- **Position IDs:** 8-digit numeric strings typically starting with '4' stored as VARCHAR (e.g., "40872170", "42004341")
- **Movement IDs:** Format "team_movement:[employee_id]:[timestamp]" (e.g., "team_movement:105016:1658137858125")
- **Dates:** All dates stored without time components in ISO format (YYYY-MM-DD)
- **Cost Centers:** Numeric or alphanumeric codes with business significance (e.g., "3807", "P4062")
- **Position Titles:** Not standardized; similar roles may have slightly different titles
- **Salaries:** Stored as numeric values without currency symbols (e.g., 66742, 83399)

## Important Implicit Relationships

1. Participants in the same movement are connected through the `movement_id`
2. Managers can be identified by their `role_id` in the participant table
3. The employee being moved can be found as the participant with `role` = 'TeamMember'
4. Tags provide quick categorization (From/To pairs for various attributes)
5. Most recent history entry indicates current state of the movement
6. The `banner`, `brand`, and `department` fields show organizational hierarchy
7. Cost centers are associated with specific physical locations

## Query Optimization Notes

- When analyzing movements over time, filter on `start_date` rather than creation dates
- Join movement to participants to get all involved employees efficiently
- For salary analysis, compare `job_info` records with `is_current=true` vs `is_current=false`
- Reporting patterns typically group by banner, department, cost center, or position type
- Use participant roles to differentiate between the subject employee and approvers
- The movement history table can be large - filter by date ranges when practical
- When querying for specific employee data, filtering on employee_id is most efficient

## Example Business Questions

- "How many employees moved from part-time to full-time in Q1 2024?"
- "What's the total increase in salary costs from all completed movements this year?"
- "Which stores have the highest number of expired movement requests?"
- "What's the average time between movement initiation and completion for BigW employees?"
- "How many employees are currently on secondment with an end date in the next 30 days?"
- "Which managers have approved the most employee transfers this quarter?"
- "Show me all movements where the base hours increased by more than 10 hours per week"
- "How many movements have been rejected by Culture & People Partners in 2024?"