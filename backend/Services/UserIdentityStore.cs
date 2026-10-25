using System.Globalization;
using Microsoft.Data.Sqlite;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class UserIdentityStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly SystemIdentityOptions _options;
    private readonly ILogger<UserIdentityStore> _log;

    public UserIdentityStore(
        IConfiguration configuration,
        DatabaseOptions database,
        SystemIdentityOptions options,
        ILogger<UserIdentityStore> log)
    {
        _options = options;
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
        EnsureSeedAdmin();
    }

    public UserRecord? FindByEmail(string email)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,email,password_hash,display_name,role,created_at,last_login_at,is_active
                FROM users WHERE lower(email)=lower($email) LIMIT 1;
                """;
            Add(command, "$email", email.Trim());
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }
    }

    public UserRecord? FindById(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,email,password_hash,display_name,role,created_at,last_login_at,is_active
                FROM users WHERE id=$id LIMIT 1;
                """;
            Add(command, "$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }
    }

    public string? GetPasswordHash(string userId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT password_hash FROM users WHERE id=$id LIMIT 1;";
            Add(command, "$id", userId);
            return command.ExecuteScalar() as string;
        }
    }

    public UserRecord CreateUser(string email, string passwordHash, string displayName, string role)
    {
        var user = new UserRecord(
            Guid.NewGuid().ToString("N"),
            email.Trim().ToLowerInvariant(),
            displayName.Trim(),
            role,
            DateTimeOffset.UtcNow,
            null,
            true);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO users(id,email,password_hash,display_name,role,created_at,last_login_at,is_active)
                VALUES($id,$email,$hash,$name,$role,$created,NULL,1);
                """;
            Add(command, "$id", user.Id);
            Add(command, "$email", user.Email);
            Add(command, "$hash", passwordHash);
            Add(command, "$name", user.DisplayName);
            Add(command, "$role", user.Role);
            Add(command, "$created", Utc(user.CreatedAt));
            command.ExecuteNonQuery();
            CreatePaperAccount(connection, user.Id, _options.DefaultPaperCapital);
        }

        return user;
    }

    public void UpdatePassword(string userId, string passwordHash)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE users SET password_hash=$hash WHERE id=$id;";
            Add(command, "$hash", passwordHash);
            Add(command, "$id", userId);
            command.ExecuteNonQuery();
        }
    }

    public UserSession CreateSession(string userId, TimeSpan lifetime, string? ipAddress)
    {
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var created = DateTimeOffset.UtcNow;
        var expires = created.Add(lifetime);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO user_sessions(token,user_id,created_at,expires_at,last_seen_at,ip_address)
                VALUES($token,$user,$created,$expires,$seen,$ip);
                """;
            Add(command, "$token", token);
            Add(command, "$user", userId);
            Add(command, "$created", Utc(created));
            Add(command, "$expires", Utc(expires));
            Add(command, "$seen", Utc(created));
            Add(command, "$ip", ipAddress);
            command.ExecuteNonQuery();
        }

        return new UserSession(token, userId, created, expires);
    }

    public (UserRecord User, UserSession Session)? ResolveSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT u.id,u.email,u.password_hash,u.display_name,u.role,u.created_at,u.last_login_at,u.is_active,
                       s.token,s.user_id,s.created_at,s.expires_at
                FROM user_sessions s
                JOIN users u ON u.id=s.user_id
                WHERE s.token=$token AND s.expires_at > $now AND u.is_active=1
                LIMIT 1;
                """;
            Add(command, "$token", token.Trim());
            Add(command, "$now", Utc(DateTimeOffset.UtcNow));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var user = ReadUser(reader);
            var session = new UserSession(
                reader.GetString(8),
                reader.GetString(9),
                DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture));
            TouchSession(connection, token);
            return (user, session);
        }
    }

    public void RevokeSession(string token)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM user_sessions WHERE token=$token;";
            Add(command, "$token", token);
            command.ExecuteNonQuery();
        }
    }

    public void RecordLogin(string userId, string email, string? ipAddress)
    {
        var at = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var login = connection.CreateCommand();
            login.CommandText =
                "INSERT INTO user_login_history(id,user_id,email,at,ip_address) VALUES($id,$user,$email,$at,$ip);";
            Add(login, "$id", Guid.NewGuid().ToString("N"));
            Add(login, "$user", userId);
            Add(login, "$email", email);
            Add(login, "$at", Utc(at));
            Add(login, "$ip", ipAddress);
            login.ExecuteNonQuery();

            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE users SET last_login_at=$at WHERE id=$id;";
            Add(update, "$at", Utc(at));
            Add(update, "$id", userId);
            update.ExecuteNonQuery();

            TouchActivity(connection, userId, at);
        }
    }

    public void TouchActivity(string userId)
    {
        lock (_gate)
        {
            using var connection = Open();
            TouchActivity(connection, userId, DateTimeOffset.UtcNow);
        }
    }

    public PaperAccountRecord GetPaperAccount(string userId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT user_id,starting_capital,available_capital,realized_pnl,unrealized_pnl,total_trades,updated_at
                FROM paper_accounts WHERE user_id=$user LIMIT 1;
                """;
            Add(command, "$user", userId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                CreatePaperAccount(connection, userId, _options.DefaultPaperCapital);
                return GetPaperAccount(userId);
            }

            return ReadPaperAccount(reader);
        }
    }

    public void UpdatePaperAccount(PaperAccountRecord account)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE paper_accounts
                SET available_capital=$available,realized_pnl=$realized,unrealized_pnl=$unrealized,total_trades=$trades,updated_at=$at
                WHERE user_id=$user;
                """;
            Add(command, "$available", DecimalText(account.AvailableCapital));
            Add(command, "$realized", DecimalText(account.RealizedPnL));
            Add(command, "$unrealized", DecimalText(account.UnrealizedPnL));
            Add(command, "$trades", account.TotalTrades);
            Add(command, "$at", Utc(account.UpdatedAt));
            Add(command, "$user", account.UserId);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<UserRecord> ListUsers()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,email,password_hash,display_name,role,created_at,last_login_at,is_active
                FROM users ORDER BY created_at ASC;
                """;
            var list = new List<UserRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadUser(reader));
            }

            return list;
        }
    }

    public int CountOnlineUsers(TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow.Subtract(window);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(DISTINCT user_id) FROM user_activity WHERE last_activity_at >= $since;";
            Add(command, "$since", Utc(since));
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<UserLoginHistoryEntry> ListLoginHistory(int limit = 100)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,user_id,email,at,ip_address
                FROM user_login_history ORDER BY at DESC LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 500));
            var list = new List<UserLoginHistoryEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return list;
        }
    }

    public IReadOnlyList<AdminUserActivityRow> ListUserActivity()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT u.id,u.email,u.display_name,u.role,u.last_login_at,a.last_activity_at
                FROM users u
                LEFT JOIN user_activity a ON a.user_id=u.id
                ORDER BY COALESCE(a.last_activity_at,u.last_login_at,u.created_at) DESC;
                """;
            var list = new List<AdminUserActivityRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    0,
                    0));
            }

            return list;
        }
    }

    public string StrategyEngineUserId => _options.StrategyEngineUserId;

    private void EnsureSeedAdmin()
    {
        if (FindByEmail(_options.AdminEmail) is not null)
        {
            return;
        }

        var admin = CreateUser(
            _options.AdminEmail,
            PasswordHasher.Hash(_options.AdminPassword),
            _options.AdminDisplayName,
            UserRoles.Admin);
        _log.LogInformation("Seeded admin user {Email} (id {Id})", admin.Email, admin.Id);
    }

    private static void TouchSession(SqliteConnection connection, string token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE user_sessions SET last_seen_at=$seen WHERE token=$token;";
        Add(command, "$seen", Utc(DateTimeOffset.UtcNow));
        Add(command, "$token", token);
        command.ExecuteNonQuery();
    }

    private static void TouchActivity(SqliteConnection connection, string userId, DateTimeOffset at)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO user_activity(user_id,last_activity_at)
            VALUES($user,$at)
            ON CONFLICT(user_id) DO UPDATE SET last_activity_at=excluded.last_activity_at;
            """;
        Add(command, "$user", userId);
        Add(command, "$at", Utc(at));
        command.ExecuteNonQuery();
    }

    private static void CreatePaperAccount(SqliteConnection connection, string userId, decimal startingCapital)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO paper_accounts(user_id,starting_capital,available_capital,realized_pnl,unrealized_pnl,total_trades,updated_at)
            VALUES($user,$start,$available,0,0,0,$at);
            """;
        Add(command, "$user", userId);
        Add(command, "$start", DecimalText(startingCapital));
        Add(command, "$available", DecimalText(startingCapital));
        Add(command, "$at", Utc(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    private static UserRecord ReadUser(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            reader.GetInt32(7) == 1);

    private static PaperAccountRecord ReadPaperAccount(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            ParseDecimal(reader.GetString(1)),
            ParseDecimal(reader.GetString(2)),
            ParseDecimal(reader.GetString(3)),
            ParseDecimal(reader.GetString(4)),
            reader.GetInt32(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS users (
                    id TEXT PRIMARY KEY,
                    email TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    role TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    last_login_at TEXT,
                    is_active INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS user_sessions (
                    token TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    ip_address TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_user_sessions_user ON user_sessions(user_id);
                CREATE TABLE IF NOT EXISTS paper_accounts (
                    user_id TEXT PRIMARY KEY,
                    starting_capital TEXT NOT NULL,
                    available_capital TEXT NOT NULL,
                    realized_pnl TEXT NOT NULL,
                    unrealized_pnl TEXT NOT NULL,
                    total_trades INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS user_login_history (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    email TEXT NOT NULL,
                    at TEXT NOT NULL,
                    ip_address TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_user_login_history_at ON user_login_history(at DESC);
                CREATE TABLE IF NOT EXISTS user_activity (
                    user_id TEXT PRIMARY KEY,
                    last_activity_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string DecimalText(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string text) =>
        decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
}
