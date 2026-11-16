using Microsoft.Data.Sqlite;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class SqliteNativeRuntimeVersionTests
{
    [Fact]
    public void Loaded_native_sqlite_is_at_least_3_50_2()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var versionText = (string?)command.ExecuteScalar();
        Assert.False(string.IsNullOrWhiteSpace(versionText));
        Assert.True(
            Version.TryParse(versionText, out var version),
            $"Could not parse sqlite_version(): {versionText}");
        var minimum = new Version(3, 50, 2);
        Assert.True(
            version >= minimum,
            $"SQLite {versionText} is below minimum {minimum} (GHSA-2m69-gcr7-jv3q fix).");
    }
}