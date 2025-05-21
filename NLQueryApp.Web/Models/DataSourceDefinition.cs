using System.Collections.Generic;

namespace NLQueryApp.Web.Models;

public class DataSourceDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "postgres";
    public Dictionary<string, string> ConnectionParameters { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime LastUpdated { get; set; }
}