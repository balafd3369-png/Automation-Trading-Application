using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Configuration;

namespace TradeWithBeast.Services;

public static class PersistentStorageValidator
{
    public static void ValidateForProduction(
        IHostEnvironment environment,
        InfrastructureOptions infrastructure,
        DatabaseOptions database,
        ProductionSafetyOptions productionSafety,
        ILogger logger)
    {
        if (!environment.IsProduction() || !infrastructure.RequirePersistentDataMount)
        {
            return;
        }

        var dataRoot = NormalizeRoot(infrastructure.PersistentDataRoot);
        if (string.IsNullOrEmpty(dataRoot))
        {
            throw new InvalidOperationException(
                "DEPLOYMENT CHECK FAIL: Infrastructure:PersistentDataRoot is required when RequirePersistentDataMount=true.");
        }

        if (!DatabaseConnectionResolver.UsesSqliteProvider(database))
        {
            throw new InvalidOperationException(
                "DEPLOYMENT CHECK FAIL: RequirePersistentDataMount requires Database:Provider=Sqlite on single-host production.");
        }

        EnsureUnderRoot(logger, dataRoot, database.SqlitePath, "Database:SqlitePath");
        EnsureUnderRoot(logger, dataRoot, database.TradeHistoryDatabasePath, "Database:TradeHistoryDatabasePath");
        EnsureUnderRoot(logger, dataRoot, productionSafety.DatabaseBackupDirectory, "ProductionSafety:DatabaseBackupDirectory");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!Directory.Exists(dataRoot))
            {
                throw new InvalidOperationException(
                    $"DEPLOYMENT CHECK FAIL: Persistent data root does not exist: {dataRoot}");
            }

            if (!IsLinuxMountPoint(dataRoot))
            {
                throw new InvalidOperationException(
                    $"DEPLOYMENT CHECK FAIL: Persistent data root is not mounted: {dataRoot}");
            }

            logger.LogInformation(
                "DEPLOYMENT CHECK PASS: Persistent data root is mounted at {DataRoot}",
                dataRoot);
        }
        else
        {
            logger.LogWarning(
                "DEPLOYMENT CHECK WARNING: Persistent volume mount check skipped (not Linux). Host={OS}",
                RuntimeInformation.OSDescription);
        }

        foreach (var dir in new[]
                 {
                     Path.GetDirectoryName(database.SqlitePath) ?? dataRoot,
                     productionSafety.DatabaseBackupDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            if (!Directory.Exists(dir))
            {
                throw new InvalidOperationException(
                    $"DEPLOYMENT CHECK FAIL: Required data directory missing: {dir}");
            }
        }
    }

    internal static bool IsLinuxMountPoint(string path)
    {
        if (!File.Exists("/proc/mounts"))
        {
            return false;
        }

        var normalized = NormalizeRoot(path);
        foreach (var line in File.ReadLines("/proc/mounts"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[1], normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureUnderRoot(ILogger logger, string dataRoot, string? configuredPath, string label)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                $"DEPLOYMENT CHECK FAIL: {label} must be set when RequirePersistentDataMount=true.");
        }

        var normalizedPath = configuredPath.Replace('\\', '/').TrimEnd('/');
        var normalizedRoot = dataRoot.TrimEnd('/');
        if (!normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.Ordinal)
            && !string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"DEPLOYMENT CHECK FAIL: {label} must reside under {dataRoot} (got {configuredPath}).");
        }

        logger.LogInformation(
            "DEPLOYMENT CHECK PASS: {Label} under persistent root",
            label);
    }

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return path.Replace('\\', '/').TrimEnd('/');
    }
}