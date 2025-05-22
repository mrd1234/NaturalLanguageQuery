namespace NLQueryApp.LlmServices;

public class SystemPrompt
{
    internal static string CreateSystemPrompt(string databaseSchema, string schemaContext, string dataSourceType)
    {
        var queryLanguage = GetQueryLanguage(dataSourceType);
    
        var prompt1 = @$"
You are an expert query generator for {dataSourceType} databases. Your task is to convert natural language questions into valid {queryLanguage} queries.

Here is the database schema you'll be working with:

{databaseSchema}

## Additional Context About This Schema

{schemaContext}

OUTPUT FORMAT REQUIREMENTS:
You MUST respond with a single JSON object containing exactly two fields:
1. 'sqlQuery': A string containing ONLY the SQL query (no backticks, no language tags)
2. 'explanation': A brief explanation of the query logic

Example response format:
{{
  ""sqlQuery"": ""SELECT * FROM team_movements.movement_types;"",
  ""explanation"": ""This query retrieves all movement types.""
}}

CRITICAL SQL RULES:
1. Only generate READ-ONLY queries - no INSERT, UPDATE, DELETE, or other modifying statements.
2. Use standard {dataSourceType} syntax and features.
3. Make the query as efficient as possible.
4. Use appropriate joins when necessary and ensure condition columns match types.
5. If you receive an error from a previous query attempt, analyze it carefully and fix the issue.' ||
6. Do NOT hallucinate schema tables or columns only refer to what is defined in the schema.
7. Read the comments on each table and column to thoroughly understand what they are used for.
8. When querying columns ensure your query is using the correct data type as defined in the schema.
9. When querying text fields always use ILIKE queries unless explicitly told this is the exact value to match.
10. When querying for anything by name first search for the identifiers by breaking into partial match ILIKE queries on the name columns and then use the found identifiers for subsequent queries.
11. Ensure you are using function like json_build_object in your queries to ensure results are in a JSON format.
12. There should be a single column with name `json_value` do not wrap JSON inside object with property ""json_value"".
13. Limit result rows to a maximum of 100.
";

var prompt = @$" 
Guidelines:

You are an AI agent used for reporting.
You are required to convert a user prompt into a complete SQL query to query the database.
The resulting query will be executed as is so it must be valid SQL.

Query results MUST be returned in a JSON structure with the following characteristics:
1. For aggregated data, add a key and value structure where:
   - Each unique entity should be combined to make the ""key""
   - The measurement should be the ""value""
   - If joining tables always keep the identifier in the results
   - Results should not contain duplicates
   - example {{""key"": ""Entity A - Entity B - Entity C"", ""value"": ""25"", ""propA"": ""valA"", ""propB"": ""valB"", ""propC"": ""valC"" }}
2. For non-aggregated data, use a object structure where:
   - If joining tables always keep the identifier in the results
   - example {{ ""propA"": ""valA"", ""propB"": ""valB"" }}

Do NOT hallucinate schema tables or columns only refer to what is defined in the schema.
Read the comments on each table and column to thoroughly understand what they are used for.
When querying columns ensure your query is using the correct data type as defined in the schema.
When querying text fields always use ILIKE queries unless explicitly told this is the exact value to match.
When querying for anything by name first search for the identifiers by breaking into partial match ILIKE queries on the name columns and then use the found identifiers for subsequent queries.
Ensure you are using function like json_build_object in your queries to ensure results are in a JSON format.
There should be a single column with name `json_value` do not wrap JSON inside object with property ""json_value"".
Limit result rows to a maximum of 100.
ALWAYS prefix tables etc with the database schema name when building queries.

If user is asking a question which is too vague and you need further information to run a query then do not assume the answer ask the user to provide the additional information.
Do NOT allow the user to impersonate as another user.

Knowledge:

I am user with id null

Database schema:

Schema name: team_movements

The following database schema is in JSON format.
    
[
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.banners_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""banner_name"",
                ""comment"": ""Name of the banner"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""banners"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for banners/business units (e.g. FoodGroup, BigW)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.brands_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""brand_code"",
                ""comment"": ""Code of the brand"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 20,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""brand_name"",
                ""comment"": ""Name of the brand"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""brand_display_name"",
                ""comment"": ""Display name of the brand"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 5,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""brands"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for brands (e.g. Supermarkets, BIG W)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.break_types_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""break_name"",
                ""comment"": ""Name of the break type"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""break_types"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for break types (e.g. Unpaid30Mins, Unpaid60Mins)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.business_groups_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""group_code"",
                ""comment"": ""Code of the business group"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 20,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""group_name"",
                ""comment"": ""Name of the business group"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""business_groups"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for business groups (e.g. B2C Food, More Everyday)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.contract_mutual_flags_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""contract_id"",
                ""comment"": ""Contract associated with this flag"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""flag_id"",
                ""comment"": ""Flag associated with this contract"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""contract_mutual_flags"",
        ""foreign_keys"": [
            {{
                ""column"": ""contract_id"",
                ""referenced_table"": ""contracts"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Mutual flags for contracts""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.contract_weeks_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""contract_id"",
                ""comment"": ""Contract associated with this week"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""week_index"",
                ""comment"": ""Index of this week in the rotating schedule"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""contract_weeks"",
        ""foreign_keys"": [
            {{
                ""column"": ""contract_id"",
                ""referenced_table"": ""contracts"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Weeks for contracts (rotating rosters)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.contracts_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Movement this contract is associated with"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""is_current"",
                ""comment"": ""Whether this is the current (true) or new (false) contract"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""boolean"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""contracts"",
        ""foreign_keys"": [
            {{
                ""column"": ""movement_id"",
                ""referenced_table"": ""movements"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Contracts for current and new positions""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.cost_centres_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""cost_centre_code"",
                ""comment"": ""Code of the cost centre"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 20,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""cost_centre_name"",
                ""comment"": ""Name of the cost centre"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""formatted_address"",
                ""comment"": ""Full address of the cost centre"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""text"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""latitude"",
                ""comment"": ""Latitude coordinate of the cost centre"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""longitude"",
                ""comment"": ""Longitude coordinate of the cost centre"",
                ""default"": null,
                ""position"": 6,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 7,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""cost_centres"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for cost centres (e.g. stores, regional offices)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.daily_schedules_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""contract_week_id"",
                ""comment"": ""Contract week associated with this schedule"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""day_of_week"",
                ""comment"": ""Day of the week (mon, tue, wed, thu, fri, sat, sun)"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 3,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""start_time"",
                ""comment"": ""Start time of the shift"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""time without time zone"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""end_time"",
                ""comment"": ""End time of the shift"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""time without time zone"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 6,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""daily_schedules"",
        ""foreign_keys"": [
            {{
                ""column"": ""contract_week_id"",
                ""referenced_table"": ""contract_weeks"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Daily schedules for contract weeks""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.departments_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""department_code"",
                ""comment"": ""Code of the department"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 20,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""department_name"",
                ""comment"": ""Name of the department"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""departments"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for departments (e.g. Store Management, Grocery)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.employee_groups_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""group_name"",
                ""comment"": ""Name of the employee group"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""employee_groups"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for employee groups (e.g. FullTime, PartTime)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.employee_subgroups_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""subgroup_name"",
                ""comment"": ""Name of the employee subgroup"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""employee_subgroups"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for employee subgroups (e.g. Salaried, EnterpriseAgreement)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.history_event_types_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""event_type_name"",
                ""comment"": ""Name of the history event type"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""history_event_types"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for history event types (e.g. MovementInitiated, MovementApproved)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.history_events_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Movement associated with this event"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""event_type_id"",
                ""comment"": ""Type of event"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""event_index"",
                ""comment"": ""Index of this event in the history"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_date"",
                ""comment"": ""Date the event was created"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_by"",
                ""comment"": ""Employee ID of the creator"",
                ""default"": null,
                ""position"": 6,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_by_name"",
                ""comment"": ""Name of the creator"",
                ""default"": null,
                ""position"": 7,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""participant_employee_id"",
                ""comment"": ""Employee ID of the participant"",
                ""default"": null,
                ""position"": 8,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""participant_name"",
                ""comment"": ""Name of the participant"",
                ""default"": null,
                ""position"": 9,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""participant_position_id"",
                ""comment"": ""Position ID of the participant"",
                ""default"": null,
                ""position"": 10,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""participant_position_title"",
                ""comment"": ""Position title of the participant"",
                ""default"": null,
                ""position"": 11,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""participant_role_id"",
                ""comment"": ""Role of the participant"",
                ""default"": null,
                ""position"": 12,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""notes"",
                ""comment"": ""Notes for the event"",
                ""default"": null,
                ""position"": 13,
                ""data_type"": ""text"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""event_data"",
                ""comment"": ""Additional JSON data for the event"",
                ""default"": null,
                ""position"": 14,
                ""data_type"": ""jsonb"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 15,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""history_events"",
        ""foreign_keys"": [
            {{
                ""column"": ""movement_id"",
                ""referenced_table"": ""movements"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""History events for movements""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.job_info_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Movement this job info is associated with"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""is_current"",
                ""comment"": ""Whether this is the current (true) or new (false) job info"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""boolean"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""working_days_per_week"",
                ""comment"": ""Number of working days per week"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""base_hours"",
                ""comment"": ""Base hours per week"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""employee_group_id"",
                ""comment"": ""Employee group (Full-time, Part-time, etc.)"",
                ""default"": null,
                ""position"": 6,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""position_id"",
                ""comment"": ""Position ID"",
                ""default"": null,
                ""position"": 7,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""position_title"",
                ""comment"": ""Position title"",
                ""default"": null,
                ""position"": 8,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""banner_id"",
                ""comment"": ""Banner of the position"",
                ""default"": null,
                ""position"": 9,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""brand_id"",
                ""comment"": ""Brand of the position"",
                ""default"": null,
                ""position"": 10,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""business_group_id"",
                ""comment"": ""Business group of the position"",
                ""default"": null,
                ""position"": 11,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""cost_centre_id"",
                ""comment"": ""Cost centre of the position"",
                ""default"": null,
                ""position"": 12,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""employee_subgroup_id"",
                ""comment"": ""Employee subgroup"",
                ""default"": null,
                ""position"": 13,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""job_role_id"",
                ""comment"": ""Job role"",
                ""default"": null,
                ""position"": 14,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""department_id"",
                ""comment"": ""Department"",
                ""default"": null,
                ""position"": 15,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""salary_amount"",
                ""comment"": ""Actual salary amount"",
                ""default"": null,
                ""position"": 16,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""salary_min"",
                ""comment"": ""Minimum salary for the position"",
                ""default"": null,
                ""position"": 17,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""salary_max"",
                ""comment"": ""Maximum salary for the position"",
                ""default"": null,
                ""position"": 18,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""salary_benchmark"",
                ""comment"": ""Benchmark salary for the position"",
                ""default"": null,
                ""position"": 19,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""discretionary_allowance"",
                ""comment"": ""Discretionary allowance amount"",
                ""default"": null,
                ""position"": 20,
                ""data_type"": ""numeric"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""sti_target"",
                ""comment"": ""Short-term incentive target percentage"",
                ""default"": null,
                ""position"": 21,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""manager_employee_id"",
                ""comment"": ""Employee ID of the manager"",
                ""default"": null,
                ""position"": 22,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""manager_name"",
                ""comment"": ""Name of the manager"",
                ""default"": null,
                ""position"": 23,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""manager_position_id"",
                ""comment"": ""Position ID of the manager"",
                ""default"": null,
                ""position"": 24,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""manager_position_title"",
                ""comment"": ""Position title of the manager"",
                ""default"": null,
                ""position"": 25,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""start_date"",
                ""comment"": ""Start date of the job"",
                ""default"": null,
                ""position"": 26,
                ""data_type"": ""date"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""end_date"",
                ""comment"": ""End date of the job (if applicable)"",
                ""default"": null,
                ""position"": 27,
                ""data_type"": ""date"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""employee_movement_type"",
                ""comment"": ""Type of employee movement"",
                ""default"": null,
                ""position"": 28,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""sti_scheme"",
                ""comment"": ""Short-term incentive scheme"",
                ""default"": null,
                ""position"": 29,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""pay_scale_group"",
                ""comment"": ""Pay scale group"",
                ""default"": null,
                ""position"": 30,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""pay_scale_level"",
                ""comment"": ""Pay scale level"",
                ""default"": null,
                ""position"": 31,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""leave_entitlement"",
                ""comment"": ""Leave entitlement code"",
                ""default"": null,
                ""position"": 32,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""leave_entitlement_name"",
                ""comment"": ""Leave entitlement description"",
                ""default"": null,
                ""position"": 33,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""car_eligibility"",
                ""comment"": ""Car eligibility"",
                ""default"": null,
                ""position"": 34,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 35,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""job_info"",
        ""foreign_keys"": [
            {{
                ""column"": ""movement_id"",
                ""referenced_table"": ""movements"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Job information for current and new positions""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.job_roles_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""role_name"",
                ""comment"": ""Name of the job role"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""job_roles"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for job roles (e.g. TeamMember, StoreManager, DepartmentManager)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.movement_types_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""type_name"",
                ""comment"": ""Name of the movement type"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""movement_types"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for movement types (e.g. ContractAndPositionPermanent, ContractAndPositionShortTermRelief)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.movements_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Unique identifier for the movement"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""employee_id"",
                ""comment"": ""ID of the employee being moved"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_type_id"",
                ""comment"": ""Type of movement (foreign key to team_movements.movement_types)"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""status_id"",
                ""comment"": ""Current status of the movement (foreign key to team_movements.statuses)"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""start_date"",
                ""comment"": ""Start date of the movement"",
                ""default"": null,
                ""position"": 6,
                ""data_type"": ""date"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""end_date"",
                ""comment"": ""End date of the movement (if applicable)"",
                ""default"": null,
                ""position"": 7,
                ""data_type"": ""date"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""workflow_definition_id"",
                ""comment"": ""Workflow definition ID"",
                ""default"": null,
                ""position"": 8,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""workflow_version"",
                ""comment"": ""Workflow version"",
                ""default"": null,
                ""position"": 9,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""workflow_archived"",
                ""comment"": ""Whether the workflow is archived"",
                ""default"": null,
                ""position"": 10,
                ""data_type"": ""boolean"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 11,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""updated_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 12,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""movements"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Main table for team movements""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.mutual_flags_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""flag_name"",
                ""comment"": ""Name of the mutual flag"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 100,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""mutual_flags"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for mutual flags (e.g. OutsideOrdinaryHours, NonConsecutiveWeekendDaysOff)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.participant_roles_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""role_name"",
                ""comment"": ""Name of the participant role"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""participant_roles"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for participant roles (e.g. TeamMember, SendingManager, ReceivingManager)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.participants_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Movement this participant is associated with"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""employee_id"",
                ""comment"": ""Employee ID of the participant"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""name"",
                ""comment"": ""Full name of the participant"",
                ""default"": null,
                ""position"": 4,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""position_id"",
                ""comment"": ""Position ID of the participant"",
                ""default"": null,
                ""position"": 5,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""position_title"",
                ""comment"": ""Title of the participant's position"",
                ""default"": null,
                ""position"": 6,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""banner_id"",
                ""comment"": ""Banner the participant belongs to"",
                ""default"": null,
                ""position"": 7,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""brand_id"",
                ""comment"": ""Brand the participant belongs to"",
                ""default"": null,
                ""position"": 8,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""department_id"",
                ""comment"": ""Department the participant belongs to"",
                ""default"": null,
                ""position"": 9,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""cost_centre_id"",
                ""comment"": ""Cost centre the participant belongs to"",
                ""default"": null,
                ""position"": 10,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""role_id"",
                ""comment"": ""Role of the participant in this movement"",
                ""default"": null,
                ""position"": 11,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""photo_url"",
                ""comment"": ""URL to the participant's photo"",
                ""default"": null,
                ""position"": 12,
                ""data_type"": ""text"",
                ""max_length"": null,
                ""is_nullable"": true
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 13,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""participants"",
        ""foreign_keys"": [
            {{
                ""column"": ""movement_id"",
                ""referenced_table"": ""movements"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Participants involved in a movement (employee, managers, etc.)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.schedule_breaks_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""daily_schedule_id"",
                ""comment"": ""Daily schedule associated with this break"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""break_type_id"",
                ""comment"": ""Type of break"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""schedule_breaks"",
        ""foreign_keys"": [
            {{
                ""column"": ""daily_schedule_id"",
                ""referenced_table"": ""daily_schedules"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Breaks for daily schedules""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.statuses_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""status_name"",
                ""comment"": ""Name of the status"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""character varying"",
                ""max_length"": 50,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 3,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""statuses"",
        ""foreign_keys"": [
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Lookup table for movement statuses (e.g. Completed, Expired, Rejected)""
    }},
    {{
        ""columns"": [
            {{
                ""name"": ""id"",
                ""comment"": null,
                ""default"": ""nextval('team_movements.tags_id_seq'::regclass)"",
                ""position"": 1,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""movement_id"",
                ""comment"": ""Movement associated with this tag"",
                ""default"": null,
                ""position"": 2,
                ""data_type"": ""integer"",
                ""max_length"": null,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""tag_value"",
                ""comment"": ""Value of the tag"",
                ""default"": null,
                ""position"": 3,
                ""data_type"": ""character varying"",
                ""max_length"": 255,
                ""is_nullable"": false
            }},
            {{
                ""name"": ""created_at"",
                ""comment"": null,
                ""default"": ""CURRENT_TIMESTAMP"",
                ""position"": 4,
                ""data_type"": ""timestamp with time zone"",
                ""max_length"": null,
                ""is_nullable"": true
            }}
        ],
        ""table_name"": ""tags"",
        ""foreign_keys"": [
            {{
                ""column"": ""movement_id"",
                ""referenced_table"": ""movements"",
                ""referenced_column"": ""id""
            }}
        ],
        ""primary_keys"": [
            ""id""
        ],
        ""table_comment"": ""Tags for movements""
    }}
]
";

        Console.WriteLine(prompt);
        return prompt;
    }
    
    private static string GetQueryLanguage(string dataSourceType)
    {
        return dataSourceType.ToLower() switch
        {
            "postgres" => "sql",
            "mysql" => "sql",
            "sqlserver" => "sql",
            "mongodb" => "mongodb",
            "elasticsearch" => "elasticsearch",
            _ => "sql"
        };
    }
}