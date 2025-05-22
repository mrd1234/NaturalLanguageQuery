namespace NLQueryApp.Core.Models;

public class DatabaseInfo
{
    public string Type { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Version ParsedVersion { get; set; } = new Version();
    public string FullVersionString { get; set; } = string.Empty;
}