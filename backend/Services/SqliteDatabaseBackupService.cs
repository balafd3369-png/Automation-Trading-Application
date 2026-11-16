using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SqliteDatabaseBackupService
{
    private readonly object _gate = new();
    private readonly ProductionSafetyOptions _options;
    private readonly DatabaseOptions _database;
    private readonly IConfiguration _configuration;
    private readonly ProductionSafetyStore _store;
    private readonly ILogger<SqliteDatabaseBackupService> _log;

    public SqliteDatabaseBackupService(
        ProductionSafetyOptions options,
        DatabaseOptions database,
        IConfiguration configuration,
        ProductionSafetyStore store,
        ILogger<SqliteDatabaseBackupService> log)
    {
        _options = options;
        _database = database;
        _configuration = configuration;
        _store = store;
        _log = log;
    }

    public async Task<DatabaseBackupRecord> CreateBackupAsync(string reason, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DatabaseBackupEnabled)
        {
            throw new InvalidOperationException("Database backup is disabled.");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        var sourcePath = DatabaseConnectionResolver.ResolveSqliteDatabasePath(_configuration, _database);
        var backupDir = ResolveBackupDirectory();
        Directory.CreateDirectory(backupDir);
        var fileName = $"trade-history-{started:yyyyMMdd-HHmmss}-{correlationId[..8]}.db";
        var backupPath = Path.Combine(backupDir, fileName);

        var pending = new DatabaseBackupRecord(
            correlationId,
            sourcePath,
            backupPath,
            DatabaseBackupResultStatus.Pending,
            0,
            null,
            false,
            correlationId,
            started,
            null);
        _store.AppendBackup(pending);

        try
        {
            await Task.Run(() => RunConsistentBackup(sourcePath, backupPath), ct);
            var size = new FileInfo(backupPath).Length;
            var verified = !_options.DatabaseBackupVerifyAfterCreate || VerifyBackupFile(backupPath);
            var status = verified
                ? DatabaseBackupResultStatus.Verified
                : DatabaseBackupResultStatus.VerificationFailed;
            var completed = new DatabaseBackupRecord(
                pending.Id,
                sourcePath,
                backupPath,
                status,
                size,
                verified ? null : "Backup integrity verification failed.",
                verified,
                correlationId,
                started,
                DateTimeOffset.UtcNow);
            _store.UpdateBackup(completed);
            if (!verified)
            {
                _log.LogWarning("Backup {BackupId} failed verification.", completed.Id);
            }
            else
            {
                _log.LogInformation("Database backup {BackupId} completed ({Size} bytes).", completed.Id, size);
            }

            PruneOldBackups(backupDir);
            return completed;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Database backup failed.");
            var failed = new DatabaseBackupRecord(
                pending.Id,
                sourcePath,
                backupPath,
                DatabaseBackupResultStatus.Failed,
                0,
                ex.Message,
                false,
                correlationId,
                started,
                DateTimeOffset.UtcNow);
            _store.UpdateBackup(failed);
            return failed;
        }
    }

    public bool VerifyBackupFile(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar()?.ToString();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Backup verification failed for {Path}.", backupPath);
            return false;
        }
    }

    private void RunConsistentBackup(string sourcePath, string backupPath)
    {
        lock (_gate)
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            using var source = new SqliteConnection(DatabaseConnectionResolver.BuildSqliteConnectionString(sourcePath));
            source.Open();
            using (var checkpoint = source.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }

            using var destination = new SqliteConnection($"Data Source={backupPath}");
            destination.Open();
            source.BackupDatabase(destination);
        }
    }

    private string ResolveBackupDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.DatabaseBackupDirectory))
        {
            return Path.GetFullPath(_options.DatabaseBackupDirectory);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TradeWithBeast", "backups");
    }

    private void PruneOldBackups(string directory)
    {
        if (_options.DatabaseBackupRetentionCount <= 0 || !Directory.Exists(directory))
        {
            return;
        }

        var files = Directory.GetFiles(directory, "trade-history-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        foreach (var file in files.Skip(_options.DatabaseBackupRetentionCount))
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to prune backup {File}.", file.FullName);
            }
        }
    }
}