namespace TradeWithBeast.Configuration;

/// <summary>Production readiness: backups, startup order audit, incident reporting. Does not auto-resubmit orders or bypass emergency gates.</summary>
public sealed class ProductionSafetyOptions
{
    public const string Section = "ProductionSafety";

    public bool Enabled { get; set; } = true;

    public bool DatabaseBackupEnabled { get; set; } = true;

    public int DatabaseBackupIntervalHours { get; set; } = 24;

    public int DatabaseBackupRetentionCount { get; set; } = 14;

    /// <summary>Relative to application data or absolute path.</summary>
    public string DatabaseBackupDirectory { get; set; } = "";

    public bool DatabaseBackupVerifyAfterCreate { get; set; } = true;

    public bool StartupOrderAuditEnabled { get; set; } = true;

    public int StartupOrderAuditDelaySeconds { get; set; } = 15;

    public bool ReconciliationIncidentsEnabled { get; set; } = true;

    public void Clamp()
    {
        DatabaseBackupIntervalHours = Math.Clamp(DatabaseBackupIntervalHours, 1, 168);
        DatabaseBackupRetentionCount = Math.Clamp(DatabaseBackupRetentionCount, 1, 90);
        StartupOrderAuditDelaySeconds = Math.Clamp(StartupOrderAuditDelaySeconds, 5, 120);
    }
}