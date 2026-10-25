namespace TradeWithBeast.Configuration;

/// <summary>
/// Database settings. SQLite is the current store; PostgreSQL is reserved for a future migration.
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Sqlite (default) or PostgreSQL (future).</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>SQLite file path when Provider is Sqlite and ConnectionStrings:DefaultConnection is not set.</summary>
    public string SqlitePath { get; set; } = "";

    /// <summary>Legacy key — same as SqlitePath when set.</summary>
    public string TradeHistoryDatabasePath { get; set; } = "";
}
