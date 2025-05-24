using System.Text.Json;

namespace NLQueryApp.Plugins.TeamMovements.Import;

public class SchemaAnalyzer
{
    private readonly HashSet<string> _movementTypes = new();
    private readonly HashSet<string> _statuses = new();
    private readonly HashSet<string> _employeeGroups = new();
    private readonly HashSet<string> _employeeSubgroups = new();
    private readonly HashSet<string> _banners = new();
    private readonly HashSet<string> _brands = new();
    private readonly HashSet<string> _businessGroups = new();
    private readonly HashSet<string> _departments = new();
    private readonly HashSet<string> _costCentres = new();
    private readonly HashSet<string> _participantRoles = new();
    private readonly HashSet<string> _jobRoles = new();
    private readonly HashSet<string> _mutualFlags = new();
    private readonly HashSet<string> _breakTypes = new();
    private readonly HashSet<string> _historyEventTypes = new();
    
    public IReadOnlySet<string> MovementTypes => _movementTypes;
    public IReadOnlySet<string> Statuses => _statuses;
    public IReadOnlySet<string> EmployeeGroups => _employeeGroups;
    public IReadOnlySet<string> EmployeeSubgroups => _employeeSubgroups;
    public IReadOnlySet<string> Banners => _banners;
    public IReadOnlySet<string> Brands => _brands;
    public IReadOnlySet<string> BusinessGroups => _businessGroups;
    public IReadOnlySet<string> Departments => _departments;
    public IReadOnlySet<string> CostCentres => _costCentres;
    public IReadOnlySet<string> ParticipantRoles => _participantRoles;
    public IReadOnlySet<string> JobRoles => _jobRoles;
    public IReadOnlySet<string> MutualFlags => _mutualFlags;
    public IReadOnlySet<string> BreakTypes => _breakTypes;
    public IReadOnlySet<string> HistoryEventTypes => _historyEventTypes;
    
    public async Task AnalyzeDirectory(string directoryPath)
    {
        // Find all matching JSON files
        var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json", 
            SearchOption.AllDirectories);
        
        Console.WriteLine($"Analyzing {files.Length} team movement files for schema information...");
        
        // Process files in parallel for performance
        var maxParallelism = Environment.ProcessorCount;
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
            async (file, token) =>
            {
                await AnalyzeFile(file);
            });
        
        Console.WriteLine($"Analysis complete. Found:");
        Console.WriteLine($"  - {_movementTypes.Count} movement types");
        Console.WriteLine($"  - {_statuses.Count} statuses");
        Console.WriteLine($"  - {_employeeGroups.Count} employee groups");
        Console.WriteLine($"  - {_banners.Count} banners");
        Console.WriteLine($"  - {_brands.Count} brands");
        Console.WriteLine($"  - {_departments.Count} departments");
        Console.WriteLine($"  - {_costCentres.Count} cost centres");
        Console.WriteLine($"  - {_participantRoles.Count} participant roles");
        Console.WriteLine($"  - {_historyEventTypes.Count} history event types");
    }
    
    private async Task AnalyzeFile(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Extract movement type
            if (root.TryGetProperty("movementType", out var movementTypeElement) && 
                movementTypeElement.ValueKind == JsonValueKind.String)
            {
                var movementType = movementTypeElement.GetString();
                if (!string.IsNullOrEmpty(movementType))
                {
                    lock (_movementTypes)
                    {
                        _movementTypes.Add(movementType);
                    }
                }
            }
            
            // Extract status
            if (root.TryGetProperty("status", out var statusElement) && 
                statusElement.ValueKind == JsonValueKind.String)
            {
                var status = statusElement.GetString();
                if (!string.IsNullOrEmpty(status))
                {
                    lock (_statuses)
                    {
                        _statuses.Add(status);
                    }
                }
            }
            
            // Analyze participants
            if (root.TryGetProperty("participants", out var participantsElement) && 
                participantsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var participant in participantsElement.EnumerateArray())
                {
                    AnalyzeParticipant(participant);
                }
            }
            
            // Analyze job info
            if (root.TryGetProperty("currentJobInfo", out var currentJobInfoElement))
            {
                AnalyzeJobInfo(currentJobInfoElement);
            }
            
            if (root.TryGetProperty("newJobInfo", out var newJobInfoElement))
            {
                AnalyzeJobInfo(newJobInfoElement);
            }
            
            // Analyze contracts
            if (root.TryGetProperty("currentContract", out var currentContractElement))
            {
                AnalyzeContract(currentContractElement);
            }
            
            if (root.TryGetProperty("newContract", out var newContractElement))
            {
                AnalyzeContract(newContractElement);
            }
            
            // Analyze history
            if (root.TryGetProperty("history", out var historyElement) && 
                historyElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var eventObj in historyElement.EnumerateArray())
                {
                    if (eventObj.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var eventProperty in eventObj.EnumerateObject())
                        {
                            var eventType = eventProperty.Name;
                            lock (_historyEventTypes)
                            {
                                _historyEventTypes.Add(eventType);
                            }
                            break; // Only process the first property (event type)
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error analyzing file {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }
    
    private void AnalyzeParticipant(JsonElement participant)
    {
        // Extract banner
        if (participant.TryGetProperty("banner", out var bannerElement) && 
            bannerElement.ValueKind == JsonValueKind.String)
        {
            var banner = bannerElement.GetString();
            if (!string.IsNullOrEmpty(banner))
            {
                lock (_banners)
                {
                    _banners.Add(banner);
                }
            }
        }
        
        // Extract brand
        if (participant.TryGetProperty("brandDisplayName", out var brandElement) && 
            brandElement.ValueKind == JsonValueKind.String)
        {
            var brand = brandElement.GetString();
            if (!string.IsNullOrEmpty(brand))
            {
                lock (_brands)
                {
                    _brands.Add(brand);
                }
            }
        }
        
        // Extract department
        if (participant.TryGetProperty("payingDepartment", out var deptElement) && 
            deptElement.ValueKind == JsonValueKind.String)
        {
            var dept = deptElement.GetString();
            if (!string.IsNullOrEmpty(dept))
            {
                lock (_departments)
                {
                    _departments.Add(dept);
                }
            }
        }
        
        // Extract cost centre
        if (participant.TryGetProperty("costCentre", out var ccElement) && 
            ccElement.ValueKind == JsonValueKind.String)
        {
            var cc = ccElement.GetString();
            if (!string.IsNullOrEmpty(cc))
            {
                lock (_costCentres)
                {
                    _costCentres.Add(cc);
                }
            }
        }
        
        // Extract role
        if (participant.TryGetProperty("role", out var roleElement) && 
            roleElement.ValueKind == JsonValueKind.String)
        {
            var role = roleElement.GetString();
            if (!string.IsNullOrEmpty(role))
            {
                lock (_participantRoles)
                {
                    _participantRoles.Add(role);
                }
            }
        }
    }
    
    private void AnalyzeJobInfo(JsonElement jobInfo)
    {
        // Extract employee group
        if (jobInfo.TryGetProperty("employeeGroup", out var egElement) && 
            egElement.ValueKind == JsonValueKind.String)
        {
            var eg = egElement.GetString();
            if (!string.IsNullOrEmpty(eg))
            {
                lock (_employeeGroups)
                {
                    _employeeGroups.Add(eg);
                }
            }
        }
        
        // Analyze position
        if (jobInfo.TryGetProperty("position", out var positionElement) && 
            positionElement.ValueKind == JsonValueKind.Object)
        {
            // Extract banner
            if (positionElement.TryGetProperty("banner", out var bannerElement) && 
                bannerElement.ValueKind == JsonValueKind.String)
            {
                var banner = bannerElement.GetString();
                if (!string.IsNullOrEmpty(banner))
                {
                    lock (_banners)
                    {
                        _banners.Add(banner);
                    }
                }
            }
            
            // Extract brand
            if (positionElement.TryGetProperty("brand", out var brandElement) && 
                brandElement.ValueKind == JsonValueKind.String)
            {
                var brand = brandElement.GetString();
                if (!string.IsNullOrEmpty(brand))
                {
                    lock (_brands)
                    {
                        _brands.Add(brand);
                    }
                }
            }
            
            // Extract business group
            if (positionElement.TryGetProperty("group", out var groupElement) && 
                groupElement.ValueKind == JsonValueKind.String)
            {
                var group = groupElement.GetString();
                if (!string.IsNullOrEmpty(group))
                {
                    lock (_businessGroups)
                    {
                        _businessGroups.Add(group);
                    }
                }
            }
            
            // Extract cost centre
            if (positionElement.TryGetProperty("costCentre", out var ccElement) && 
                ccElement.ValueKind == JsonValueKind.String)
            {
                var cc = ccElement.GetString();
                if (!string.IsNullOrEmpty(cc))
                {
                    lock (_costCentres)
                    {
                        _costCentres.Add(cc);
                    }
                }
            }
            
            // Extract employee subgroup
            if (positionElement.TryGetProperty("employeeSubgroup", out var esElement) && 
                esElement.ValueKind == JsonValueKind.String)
            {
                var es = esElement.GetString();
                if (!string.IsNullOrEmpty(es))
                {
                    lock (_employeeSubgroups)
                    {
                        _employeeSubgroups.Add(es);
                    }
                }
            }
            
            // Extract job role
            if (positionElement.TryGetProperty("jobRole", out var jrElement) && 
                jrElement.ValueKind == JsonValueKind.String)
            {
                var jr = jrElement.GetString();
                if (!string.IsNullOrEmpty(jr))
                {
                    lock (_jobRoles)
                    {
                        _jobRoles.Add(jr);
                    }
                }
            }
            
            // Extract department
            if (positionElement.TryGetProperty("payingDepartment", out var pdElement) && 
                pdElement.ValueKind == JsonValueKind.String)
            {
                var pd = pdElement.GetString();
                if (!string.IsNullOrEmpty(pd))
                {
                    lock (_departments)
                    {
                        _departments.Add(pd);
                    }
                }
            }
        }
    }
    
    private void AnalyzeContract(JsonElement contract)
    {
        // Extract mutual flags
        if (contract.TryGetProperty("mutualFlags", out var flagsElement) && 
            flagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var flag in flagsElement.EnumerateArray())
            {
                if (flag.ValueKind == JsonValueKind.String)
                {
                    var flagValue = flag.GetString();
                    if (!string.IsNullOrEmpty(flagValue))
                    {
                        lock (_mutualFlags)
                        {
                            _mutualFlags.Add(flagValue);
                        }
                    }
                }
            }
        }
        
        // Analyze weeks for break types
        if (contract.TryGetProperty("weeks", out var weeksElement) && 
            weeksElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var week in weeksElement.EnumerateArray())
            {
                if (week.ValueKind == JsonValueKind.Object)
                {
                    string[] days = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
                    foreach (var day in days)
                    {
                        if (week.TryGetProperty(day, out var dayElement) && 
                            dayElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var shift in dayElement.EnumerateArray())
                            {
                                if (shift.ValueKind == JsonValueKind.Object &&
                                    shift.TryGetProperty("breaks", out var breaksElement) && 
                                    breaksElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var breakItem in breaksElement.EnumerateArray())
                                    {
                                        if (breakItem.ValueKind == JsonValueKind.String)
                                        {
                                            var breakType = breakItem.GetString();
                                            if (!string.IsNullOrEmpty(breakType))
                                            {
                                                lock (_breakTypes)
                                                {
                                                    _breakTypes.Add(breakType);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
