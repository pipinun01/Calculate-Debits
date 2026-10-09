using Microsoft.EntityFrameworkCore;

namespace Debits.Infrastructure.Persistence;

public static class DebitsDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(DebitsDbContext).Assembly.FullName)
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention();
    }
}
