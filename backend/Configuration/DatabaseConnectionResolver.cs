using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace TradeWithBeast.Configuration;

public static class DatabaseConnectionResolver
{
    public static string ResolveSqliteDatabasePath(IConfiguration configuration, DatabaseOptions database)
    {
        var fromConnectionString = TryParseSqliteDataSource(configuration.GetConnectionString("DefaultConnection"));
        if (!string.IsNullOrWhiteSpace(fromConnectionString))
        {
            return fromConnectionString;
        }

        var legacy = configuration["TradeHistory:DatabasePath"];
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            return legacy.Trim();
        }

        if (!string.IsNullOrWhiteSpace(database.TradeHistoryDatabasePath))
        {
            return database.TradeHistoryDatabasePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(database.SqlitePath))
        {
            return database.SqlitePath.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TradeWithBeast",
            "trade-history.db");
    }

    public static string BuildSqliteConnectionString(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
    }

    public static bool UsesSqliteProvider(DatabaseOptions database) =>
        !string.Equals(database.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);

    private static string? TryParseSqliteDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource) ? null : builder.DataSource;
        }
        catch
        {
            return null;
        }
    }
}
