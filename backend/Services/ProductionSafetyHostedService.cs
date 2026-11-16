using TradeWithBeast.Configuration;

namespace TradeWithBeast.Services;

public sealed class ProductionSafetyHostedService : BackgroundService
{
    private readonly ProductionSafetyOptions _options;
    private readonly OrderStartupSafetyScanner _scanner;
    private readonly ProductionSafetyStore _store;
    private readonly SqliteDatabaseBackupService _backups;
    private readonly ILogger<ProductionSafetyHostedService> _log;

    public ProductionSafetyHostedService(
        ProductionSafetyOptions options,
        OrderStartupSafetyScanner scanner,
        ProductionSafetyStore store,
        SqliteDatabaseBackupService backups,
        ILogger<ProductionSafetyHostedService> log)
    {
        _options = options;
        _scanner = scanner;
        _store = store;
        _backups = backups;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (_options.StartupOrderAuditEnabled)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.StartupOrderAuditDelaySeconds), stoppingToken);
                var audit = await _scanner.RunAsync(stoppingToken);
                _store.SaveStartupAudit(audit);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Startup order audit failed.");
            }
        }

        var interval = TimeSpan.FromHours(_options.DatabaseBackupIntervalHours);
        var nextBackup = DateTimeOffset.UtcNow;
        if (_options.DatabaseBackupEnabled)
        {
            var last = _store.LastBackup();
            if (last?.CompletedUtc is not null)
            {
                nextBackup = last.CompletedUtc.Value + interval;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.DatabaseBackupEnabled && DateTimeOffset.UtcNow >= nextBackup)
                {
                    await _backups.CreateBackupAsync("Scheduled production backup", stoppingToken);
                    nextBackup = DateTimeOffset.UtcNow + interval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Scheduled database backup cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}