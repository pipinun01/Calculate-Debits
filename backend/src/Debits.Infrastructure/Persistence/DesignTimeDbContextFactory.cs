using Debits.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Debits.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string comes from <c>ConnectionStrings__Default</c> or the dev default.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DebitsDbContext>
{
    public DebitsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=debits;Username=debits;Password=debits";

        var options = new DbContextOptionsBuilder<DebitsDbContext>();
        DebitsDbContextOptions.Configure(options, connectionString);
        return new DebitsDbContext(options.Options, new TenantContext(), TimeProvider.System);
    }
}
