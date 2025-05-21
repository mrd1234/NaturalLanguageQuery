namespace NLQueryApp.Api.Controllers.Import;

/// <summary>
/// Represents a lookup item with code and name
/// </summary>
public class LookupItem : IEquatable<LookupItem>
{
    public string Code { get; }
    public string Name { get; }
    public string DisplayName { get; }
        
    public LookupItem(string code, string name)
    {
        Code = code;
        Name = name;
        DisplayName = name;
    }
        
    public LookupItem(string code, string name, string displayName)
    {
        Code = code;
        Name = name;
        DisplayName = displayName;
    }
        
    public bool Equals(LookupItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }
        
    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj is LookupItem item && Equals(item);
    }
        
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Code);
    }
}