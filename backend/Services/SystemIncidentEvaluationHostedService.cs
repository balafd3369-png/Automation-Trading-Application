using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentEvaluationHostedService : IHostedService, IDisposable
{
    private readonly SystemMonitoringOptions _options;
    private readonly SystemHealthService _health;
    private readonly SystemIncidentService _incidents;
    private readonly SystemLeaderElectionService _leaders;
    private readonly ILogger<SystemIncidentEvaluationHostedService> _log;
    private Timer? _timer;
    private int _busy;

    public SystemIncidentEvaluationHostedService(
        SystemMonitoringOptions options,
        SystemHealthService health,
        SystemIncidentService incidents,
        SystemLeaderElectionService leaders,
        ILogger<SystemIncidentEvaluationHostedService> log)
    {
        _options = options;
        _health = health;
        _incidents = incidents;
        _leaders = leaders;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IncidentManagementEnabled)
        {
            _log.LogInformation("Incident management evaluation is disabled.");
            return Task.CompletedTask;
        }

        _options.ClampIntervals();
        var interval = TimeSpan.FromSeconds(_options.IncidentEvaluationIntervalSeconds);
        _timer = new Timer(_ => _ = RunEvaluationAsync(), null, TimeSpan.FromSeconds(8), interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task RunEvaluationAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_leaders.IsLeader(SystemLeaderRoles.IncidentEvaluation))
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _options.IncidentEvaluationIntervalSeconds - 1)));
            await _incidents.EvaluateAsync(_health, cts.Token);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Incident evaluation cycle failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}