namespace NLQueryApp.Api.Controllers.Import;

/// <summary>
/// Represents a cost centre lookup item with geographic information
/// </summary>
public class CostCentreLookupItem : IEquatable<CostCentreLookupItem>
{
    public string Code { get; }
    public string Name { get; }
    public string? FormattedAddress { get; }
    public decimal? Latitude { get; }
    public decimal? Longitude { get; }
        
    public CostCentreLookupItem(string code, string name, string? formattedAddress = null, 
        decimal? latitude = null, decimal? longitude = null)
    {
        Code = code;
        Name = name;
        FormattedAddress = formattedAddress;
        Latitude = latitude;
        Longitude = longitude;
    }
        
    public bool Equals(CostCentreLookupItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }
        
    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj is CostCentreLookupItem item && Equals(item);
    }
        
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Code);
    }
}