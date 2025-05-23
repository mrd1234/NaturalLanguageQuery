using System.Collections.Concurrent;
using System.Text.Json;

namespace NLQueryApp.Api.Controllers.Import;

/// <summary>
/// Analyzes the JSON structure of team movement files
/// </summary>
public class SchemaAnalyzer
{
    private int _totalFiles;

    // Track the lookup values we find
    public ConcurrentDictionary<string, HashSet<string>> MovementTypes { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> Statuses { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> EmployeeGroups { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> EmployeeSubgroups { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> Banners { get; } = new();
    public ConcurrentDictionary<string, HashSet<LookupItem>> Brands { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> Groups { get; } = new();
    public ConcurrentDictionary<string, HashSet<LookupItem>> Departments { get; } = new();
    public ConcurrentDictionary<string, HashSet<CostCentreLookupItem>> CostCentres { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> ParticipantRoles { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> JobRoles { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> MutualFlags { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> BreakTypes { get; } = new();
    public ConcurrentDictionary<string, HashSet<string>> HistoryEventTypes { get; } = new();
    
    // Track field metadata for schema generation
    public ConcurrentDictionary<string, HashSet<string>> FieldTypes { get; } = new();
    
    // Track errors for reporting
    public ConcurrentBag<string> ProcessingErrors { get; } = new();

    // New property to track warnings
    public List<string> Warnings { get; } = new();

    // Statistics
    public int TotalFiles
    {
        get => _totalFiles;
        private set => _totalFiles = value;
    }

    public async Task AnalyzeDirectory(string directoryPath)
    {
        // Find all matching files recursively
        var files = Directory.GetFiles(directoryPath, "tms_team_movements_team_movement_*.json", 
            SearchOption.AllDirectories);
        
        Console.WriteLine($"Found {files.Length} team movement files to analyze");
        
        // Process files in batches with parallelism
        var batchSize = 100;
        var maxParallelism = Environment.ProcessorCount * 2;
        var fileCount = 0;
        
        for (var i = 0; i < files.Length; i += batchSize)
        {
            var currentBatch = files.Skip(i).Take(batchSize).ToArray();
            
            // Process batch in parallel
            await Parallel.ForEachAsync(
                currentBatch,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                async (file, token) =>
                {
                    await ProcessFile(file);
                    var processed = Interlocked.Increment(ref fileCount);
                    if (processed % 500 == 0)
                    {
                        Console.WriteLine($"Processed {processed} of {files.Length} files");
                    }
                });
        }
        
        TotalFiles = fileCount;
        Console.WriteLine($"Analysis complete: {TotalFiles} files with {ProcessingErrors.Count} errors");
    }
    
    private async Task ProcessFile(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            Interlocked.Increment(ref _totalFiles);
            
            // Extract movement type
            if (root.TryGetProperty("movementType", out var movementTypeElement) &&
                movementTypeElement.ValueKind == JsonValueKind.String)
            {
                var movementType = movementTypeElement.GetString() ?? "Unknown";
                AddToLookup(MovementTypes, movementType);
            }
            
            // Extract status
            if (root.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.String)
            {
                var status = statusElement.GetString() ?? "Unknown";
                AddToLookup(Statuses, status);
            }
            
            // Process current job info
            if (root.TryGetProperty("currentJobInfo", out var currentJobInfoElement) &&
                currentJobInfoElement.ValueKind == JsonValueKind.Object)
            {
                ProcessJobInfo(currentJobInfoElement, "current");
            }
            
            // Process new job info
            if (root.TryGetProperty("newJobInfo", out var newJobInfoElement) &&
                newJobInfoElement.ValueKind == JsonValueKind.Object)
            {
                ProcessJobInfo(newJobInfoElement, "new");
            }
            
            // Process participants
            if (root.TryGetProperty("participants", out var participantsElement) &&
                participantsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var participant in participantsElement.EnumerateArray())
                {
                    ProcessParticipant(participant);
                }
            }
            
            // Process contracts - NEW
            if (root.TryGetProperty("currentContract", out var currentContractElement) &&
                currentContractElement.ValueKind == JsonValueKind.Object)
            {
                ProcessContract(currentContractElement);
            }
            
            if (root.TryGetProperty("newContract", out var newContractElement) &&
                newContractElement.ValueKind == JsonValueKind.Object)
            {
                ProcessContract(newContractElement);
            }
            
            // Process history
            if (root.TryGetProperty("history", out var historyElement) &&
                historyElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var historyEvent in historyElement.EnumerateArray())
                {
                    ProcessHistoryEvent(historyEvent);
                }
            }
            
            // Process tags
            if (root.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagsElement.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String)
                    {
                        var tagValue = tag.GetString() ?? "";
                        if (tagValue.StartsWith("FromEmployeeGroup:"))
                        {
                            AddToLookup(EmployeeGroups, tagValue.Substring("FromEmployeeGroup:".Length));
                        }
                        else if (tagValue.StartsWith("ToEmployeeGroup:"))
                        {
                            AddToLookup(EmployeeGroups, tagValue.Substring("ToEmployeeGroup:".Length));
                        }
                        else if (tagValue.StartsWith("FromEmployeeSubgroup:"))
                        {
                            AddToLookup(EmployeeSubgroups, tagValue.Substring("FromEmployeeSubgroup:".Length));
                        }
                        else if (tagValue.StartsWith("ToEmployeeSubgroup:"))
                        {
                            AddToLookup(EmployeeSubgroups, tagValue.Substring("ToEmployeeSubgroup:".Length));
                        }
                        else if (tagValue.StartsWith("FromBanner:"))
                        {
                            AddToLookup(Banners, tagValue.Substring("FromBanner:".Length));
                        }
                        else if (tagValue.StartsWith("ToBanner:"))
                        {
                            AddToLookup(Banners, tagValue.Substring("ToBanner:".Length));
                        }
                        else if (tagValue.StartsWith("FromGroup:"))
                        {
                            AddToLookup(Groups, tagValue.Substring("FromGroup:".Length));
                        }
                        else if (tagValue.StartsWith("ToGroup:"))
                        {
                            AddToLookup(Groups, tagValue.Substring("ToGroup:".Length));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ProcessingErrors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
        }
    }
    
    private void ProcessJobInfo(JsonElement jobInfoElement, string prefix)
    {
        if (jobInfoElement.TryGetProperty("employeeGroup", out var employeeGroupElement) &&
            employeeGroupElement.ValueKind == JsonValueKind.String)
        {
            var employeeGroup = employeeGroupElement.GetString() ?? "Unknown";
            AddToLookup(EmployeeGroups, employeeGroup);
        }
        
        if (jobInfoElement.TryGetProperty("position", out var positionElement) &&
            positionElement.ValueKind == JsonValueKind.Object)
        {
            if (positionElement.TryGetProperty("employeeSubgroup", out var subgroupElement) &&
                subgroupElement.ValueKind == JsonValueKind.String)
            {
                var subgroup = subgroupElement.GetString() ?? "Unknown";
                AddToLookup(EmployeeSubgroups, subgroup);
            }
            
            if (positionElement.TryGetProperty("banner", out var bannerElement) &&
                bannerElement.ValueKind == JsonValueKind.String)
            {
                var banner = bannerElement.GetString() ?? "Unknown";
                AddToLookup(Banners, banner);
            }
            
            if (positionElement.TryGetProperty("brand", out var brandCodeElement) &&
                brandCodeElement.ValueKind == JsonValueKind.String)
            {
                var brandCode = brandCodeElement.GetString() ?? "Unknown";
                
                var brandName = "Unknown";
                if (positionElement.TryGetProperty("brandName", out var brandNameElement) &&
                    brandNameElement.ValueKind == JsonValueKind.String)
                {
                    brandName = brandNameElement.GetString() ?? "Unknown";
                }
                
                var brandDisplayName = "Unknown";
                if (positionElement.TryGetProperty("brandDisplayName", out var brandDisplayNameElement) &&
                    brandDisplayNameElement.ValueKind == JsonValueKind.String)
                {
                    brandDisplayName = brandDisplayNameElement.GetString() ?? "Unknown";
                }
                
                AddToLookup(Brands, new LookupItem(brandCode, brandName, brandDisplayName));
            }
            
            if (positionElement.TryGetProperty("group", out var groupElement) &&
                groupElement.ValueKind == JsonValueKind.String)
            {
                var group = groupElement.GetString() ?? "Unknown";
                AddToLookup(Groups, group);
            }
            
            if (positionElement.TryGetProperty("payingDepartment", out var deptCodeElement) &&
                deptCodeElement.ValueKind == JsonValueKind.String)
            {
                var deptCode = deptCodeElement.GetString() ?? "Unknown";
                var deptName = "Unknown";
                
                if (positionElement.TryGetProperty("payingDepartmentName", out var deptNameElement) &&
                    deptNameElement.ValueKind == JsonValueKind.String)
                {
                    deptName = deptNameElement.GetString() ?? "Unknown";
                }
                
                AddToLookup(Departments, new LookupItem(deptCode, deptName));
            }
            
            // Basic cost centre from position (no geographic data here)
            if (positionElement.TryGetProperty("costCentre", out var ccCodeElement) &&
                ccCodeElement.ValueKind == JsonValueKind.String)
            {
                var ccCode = ccCodeElement.GetString() ?? "Unknown";
                var ccName = "Unknown";
                
                if (positionElement.TryGetProperty("costCentreName", out var ccNameElement) &&
                    ccNameElement.ValueKind == JsonValueKind.String)
                {
                    ccName = ccNameElement.GetString() ?? "Unknown";
                }
                
                AddToLookup(CostCentres, new CostCentreLookupItem(ccCode, ccName));
            }
            
            if (positionElement.TryGetProperty("jobRole", out var jobRoleElement) &&
                jobRoleElement.ValueKind == JsonValueKind.String)
            {
                var jobRole = jobRoleElement.GetString() ?? "Unknown";
                AddToLookup(JobRoles, jobRole);
            }
        }
        
        // Process manager cost centre with geographic data - THIS IS THE KEY ADDITION
        if (jobInfoElement.TryGetProperty("manager", out var managerElement) &&
            managerElement.ValueKind == JsonValueKind.Object)
        {
            if (managerElement.TryGetProperty("costCentre", out var managerCostCentreElement) &&
                managerCostCentreElement.ValueKind == JsonValueKind.Object)
            {
                // This is the nested cost centre object with geographic data
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
                    if (latEl.ValueKind == JsonValueKind.Number && latEl.TryGetDecimal(out var lat))
                    {
                        latitude = lat;
                    }
                    else if (latEl.ValueKind == JsonValueKind.String && 
                             decimal.TryParse(latEl.GetString(), out var latFromString))
                    {
                        latitude = latFromString;
                    }
                }
                
                if (managerCostCentreElement.TryGetProperty("lng", out var lngEl))
                {
                    if (lngEl.ValueKind == JsonValueKind.Number && lngEl.TryGetDecimal(out var lng))
                    {
                        longitude = lng;
                    }
                    else if (lngEl.ValueKind == JsonValueKind.String && 
                             decimal.TryParse(lngEl.GetString(), out var lngFromString))
                    {
                        longitude = lngFromString;
                    }
                }
                
                AddToLookup(CostCentres, new CostCentreLookupItem(ccCode, ccName, addressFormatted, latitude, longitude));
            }
        }
    }
    
    private void ProcessParticipant(JsonElement participant)
    {
        if (participant.TryGetProperty("role", out var roleElement) &&
            roleElement.ValueKind == JsonValueKind.String)
        {
            var role = roleElement.GetString() ?? "Unknown";
            AddToLookup(ParticipantRoles, role);
        }
        
        if (participant.TryGetProperty("banner", out var bannerElement) &&
            bannerElement.ValueKind == JsonValueKind.String)
        {
            var banner = bannerElement.GetString() ?? "Unknown";
            AddToLookup(Banners, banner);
        }
        
        if (participant.TryGetProperty("payingDepartment", out var deptCodeElement) &&
            deptCodeElement.ValueKind == JsonValueKind.String)
        {
            var deptCode = deptCodeElement.GetString() ?? "Unknown";
            var deptName = "Unknown";
            
            if (participant.TryGetProperty("payingDepartmentName", out var deptNameElement) &&
                deptNameElement.ValueKind == JsonValueKind.String)
            {
                deptName = deptNameElement.GetString() ?? "Unknown";
            }
            
            AddToLookup(Departments, new LookupItem(deptCode, deptName));
        }
        
        // Basic cost centre from participant (no geographic data available here)
        if (participant.TryGetProperty("costCentre", out var ccCodeElement) &&
            ccCodeElement.ValueKind == JsonValueKind.String)
        {
            var ccCode = ccCodeElement.GetString() ?? "Unknown";
            var ccName = "Unknown";
            
            if (participant.TryGetProperty("costCentreName", out var ccNameElement) &&
                ccNameElement.ValueKind == JsonValueKind.String)
            {
                ccName = ccNameElement.GetString() ?? "Unknown";
            }
            
            // Only basic cost centre info available in participants array
            AddToLookup(CostCentres, new CostCentreLookupItem(ccCode, ccName));
        }
    }
    
    // NEW METHOD: Process contract data to extract mutual flags and break types
    private void ProcessContract(JsonElement contractElement)
    {
        // Extract mutual flags
        if (contractElement.TryGetProperty("mutualFlags", out var flagsElement) &&
            flagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var flag in flagsElement.EnumerateArray())
            {
                if (flag.ValueKind == JsonValueKind.String)
                {
                    var flagValue = flag.GetString() ?? "Unknown";
                    AddToLookup(MutualFlags, flagValue);
                }
            }
        }
        
        // Extract break types from weeks
        if (contractElement.TryGetProperty("weeks", out var weeksElement) &&
            weeksElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var week in weeksElement.EnumerateArray())
            {
                if (week.ValueKind == JsonValueKind.Object)
                {
                    // Check each day of the week
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
                                            var breakType = breakItem.GetString() ?? "Unknown";
                                            AddToLookup(BreakTypes, breakType);
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
    
    private void ProcessHistoryEvent(JsonElement historyEvent)
    {
        // History event type is the first property name in the object
        foreach (var property in historyEvent.EnumerateObject())
        {
            AddToLookup(HistoryEventTypes, property.Name);
            
            // Process the event data for cost centre information
            var eventData = property.Value;
            if (eventData.ValueKind == JsonValueKind.Object)
            {
                ProcessHistoryEventData(eventData);
            }
            
            break; // Only need the first property
        }
    }

    private void ProcessHistoryEventData(JsonElement eventData)
    {
        // Check for newManager with nested cost centre data
        if (eventData.TryGetProperty("newManager", out var newManagerElement) &&
            newManagerElement.ValueKind == JsonValueKind.Object)
        {
            ProcessManagerCostCentre(newManagerElement);
        }
        
        // Check for participant with nested cost centre data
        if (eventData.TryGetProperty("participant", out var participantElement) &&
            participantElement.ValueKind == JsonValueKind.Object)
        {
            ProcessManagerCostCentre(participantElement);
        }
        
        // Process contract data in history events
        if (eventData.TryGetProperty("contract", out var contractElement) &&
            contractElement.ValueKind == JsonValueKind.Object)
        {
            ProcessContract(contractElement);
        }
    }

    private void ProcessManagerCostCentre(JsonElement managerElement)
    {
        if (managerElement.TryGetProperty("costCentre", out var costCentreElement) &&
            costCentreElement.ValueKind == JsonValueKind.Object)
        {
            // This is the nested cost centre object with geographic data
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
                if (latEl.ValueKind == JsonValueKind.Number && latEl.TryGetDecimal(out var lat))
                {
                    latitude = lat;
                }
                else if (latEl.ValueKind == JsonValueKind.String && 
                         decimal.TryParse(latEl.GetString(), out var latFromString))
                {
                    latitude = latFromString;
                }
            }
            
            if (costCentreElement.TryGetProperty("lng", out var lngEl))
            {
                if (lngEl.ValueKind == JsonValueKind.Number && lngEl.TryGetDecimal(out var lng))
                {
                    longitude = lng;
                }
                else if (lngEl.ValueKind == JsonValueKind.String && 
                         decimal.TryParse(lngEl.GetString(), out var lngFromString))
                {
                    longitude = lngFromString;
                }
            }
            
            AddToLookup(CostCentres, new CostCentreLookupItem(ccCode, ccName, addressFormatted, latitude, longitude));
        }
    }
    
    private void AddToLookup(ConcurrentDictionary<string, HashSet<string>> lookup, string value)
    {
        if (string.IsNullOrEmpty(value)) value = "Unknown";
        
        lookup.AddOrUpdate(
            "values",
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value },
            (_, set) =>
            {
                lock (set)
                {
                    set.Add(value);
                    return set;
                }
            });
    }
    
    private void AddToLookup<T>(ConcurrentDictionary<string, HashSet<T>> lookup, T value) where T : IEquatable<T>
    {
        lookup.AddOrUpdate(
            "values",
            _ => new HashSet<T> { value },
            (_, set) =>
            {
                lock (set)
                {
                    set.Add(value);
                    return set;
                }
            });
    }
}