namespace Debits.Application.Abstractions;

/// <summary>Names of the EF Core named query filters, for <c>IgnoreQueryFilters([...])</c>.</summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
