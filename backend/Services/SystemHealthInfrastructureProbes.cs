using System.Diagnostics;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemHealthInfrastructureProbes
{
    private readonly SystemMonitoringOptions _options;
    private readonly BrokerConnectionManager _brokers;
    private readonly ISystemHealthBoardProbeSource _board;
    private readonly SignalRServerHealthTracker _signalR;
    private readonly ChartLiveStreamService _chartStreams;
    private readonly MStockOptions _mstock;
    private readonly ILogger<SystemHealthInfrastructureProbes> _log;
    private int _brokerProbeBusy;
    private int _marketProbeBusy;
    private int _signalRProbeBusy;
    private BrokerConnectivityHealthSnapshot _lastBroker = UnknownBroker();
    private MarketDataHealthSnapshot _lastMarket = UnknownMarket();
    private SignalRServerHealthSnapshot _lastSignalR = UnknownSignalR();
    private int _brokerConsecutiveFailures;
    private int _marketConsecutiveFailures;
    private DateTimeOffset? _brokerLastSuccess;
    private DateTimeOffset? _brokerLastFailure;
    private DateTimeOffset? _marketLastFailure;

    public SystemHealthInfrastructureProbes(
        SystemMonitoringOptions options,
        BrokerConnectionManager brokers,
        ISystemHealthBoardProbeSource board,
        SignalRServerHealthTracker signalR,
        ChartLiveStreamService chartStreams,
        MStockOptions mstock,
        ILogger<SystemHealthInfrastructureProbes> log)
    {
        _options = options;
        _brokers = brokers;
        _board = board;
        _signalR = signalR;
        _chartStreams = chartStreams;
        _mstock = mstock;
        _log = log;
    }

    public BrokerConnectivityHealthSnapshot LastBroker => _lastBroker;

    public MarketDataHealthSnapshot LastMarket => _lastMarket;

    public SignalRServerHealthSnapshot LastSignalR => _lastSignalR;

    public async Task<BrokerConnectivityHealthSnapshot> ProbeBrokerAsync(CancellationToken cancellationToken)
    {
        if (!_options.BrokerMonitoringEnabled)
        {
            return _lastBroker with { State = ComponentHealthState.Unknown };
        }

        if (Interlocked.Exchange(ref _brokerProbeBusy, 1) == 1)
        {
            return _lastBroker;
        }

        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var connections = new List<BrokerConnectionHealthItem>();
            foreach (var provider in _brokers.Providers)
            {
                var sessionPresent = _brokers.HasSession
                    && string.Equals(provider.Id, BrokerIds.MStock, StringComparison.OrdinalIgnoreCase);
                var verified = false;
                string? error = null;
                if (!sessionPresent
                    && NseCashSession.IsLiveIntradayWindowNow()
                    && AppConfigurationBinder.BrokerCredentialsConfigured(_mstock))
                {
                    error = "AUTH: Credentials configured but no active broker session.";
                }

                if (sessionPresent)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(_options.BrokerTimeoutSeconds));
                        var quote = await _brokers.Market.GetBoardQuoteAsync("NIFTY", timeout.Token);
                        verified = quote is not null && quote.Last > 0;
                        if (!verified)
                        {
                            error = "Broker session present but read-only NIFTY quote probe returned no data.";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        error = $"Broker quote probe timed out after {_options.BrokerTimeoutSeconds}s.";
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }

                var itemState = ResolveBrokerItemState(sessionPresent, verified, error);
                connections.Add(new BrokerConnectionHealthItem(
                    provider.Id,
                    provider.DisplayName,
                    sessionPresent,
                    verified,
                    verified ? started : _brokerLastSuccess,
                    string.IsNullOrWhiteSpace(error) ? null : started,
                    string.IsNullOrWhiteSpace(error) ? 0 : 1,
                    error,
                    itemState));
            }

            var anySession = connections.Any(c => c.SessionPresent);
            var anyVerified = connections.Any(c => c.CommunicationVerified);
            string? probeError = null;
            ComponentHealthState state;
            if (!anySession)
            {
                state = NseCashSession.IsLiveIntradayWindowNow()
                    ? ComponentHealthState.Degraded
                    : ComponentHealthState.Healthy;
                probeError = anySession ? null : "No active broker session.";
                _brokerConsecutiveFailures = 0;
            }
            else if (anyVerified)
            {
                state = ComponentHealthState.Healthy;
                _brokerConsecutiveFailures = 0;
                _brokerLastSuccess = started;
            }
            else
            {
                probeError = connections.FirstOrDefault(c => c.LastError is not null)?.LastError
                    ?? "Broker communication probe failed.";
                _brokerConsecutiveFailures++;
                _brokerLastFailure = started;
                state = _brokerConsecutiveFailures >= _options.BrokerFailureThreshold
                    ? ComponentHealthState.Degraded
                    : ComponentHealthState.Degraded;
            }

            var snapshot = new BrokerConnectivityHealthSnapshot(
                state,
                connections,
                anyVerified ? started : _brokerLastSuccess,
                _brokerLastFailure,
                _brokerConsecutiveFailures,
                probeError,
                started,
                stopwatch.ElapsedMilliseconds);
            _lastBroker = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Broker connectivity probe failed.");
            _brokerConsecutiveFailures++;
            _brokerLastFailure = started;
            var snapshot = new BrokerConnectivityHealthSnapshot(
                ComponentHealthState.Degraded,
                [],
                _brokerLastSuccess,
                _brokerLastFailure,
                _brokerConsecutiveFailures,
                ex.Message,
                started,
                stopwatch.ElapsedMilliseconds);
            _lastBroker = snapshot;
            return snapshot;
        }
        finally
        {
            Interlocked.Exchange(ref _brokerProbeBusy, 0);
        }
    }

    public Task<MarketDataHealthSnapshot> ProbeMarketDataAsync(CancellationToken cancellationToken)
    {
        if (!_options.MarketDataMonitoringEnabled)
        {
            return Task.FromResult(_lastMarket with { State = ComponentHealthState.Unknown });
        }

        if (Interlocked.Exchange(ref _marketProbeBusy, 1) == 1)
        {
            return Task.FromResult(_lastMarket);
        }

        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var board = _board.Snapshot();
            var adminBrokerConnected = _brokers.HasSession;
            var feedSource = adminBrokerConnected ? BrokerIds.MStock : "mock";
            var tradingDay = NseCashSession.IsTradingDay(NseCashSession.IstToday());
            var intradayOpen = NseCashSession.IsLiveIntradayWindowNow();
            var staleThreshold = TimeSpan.FromSeconds(_options.MarketDataStaleThresholdSeconds);
            var watchedIntervals = IndexUniverse.IndexNames.ToList();
            var instruments = new List<MarketDataInstrumentHealth>();

            foreach (var symbol in watchedIntervals)
            {
                var row = board.Symbols.FirstOrDefault(r =>
                    string.Equals(r.Name, symbol, StringComparison.OrdinalIgnoreCase));
                var lastUpdate = row?.DataTimestamp ?? board.UpdatedAt;
                var updatesExpected = intradayOpen && tradingDay && adminBrokerConnected;
                var subscriptionActive = _chartStreams.HasWatchers(symbol);
                var stale = updatesExpected && started - lastUpdate > staleThreshold;
                instruments.Add(new MarketDataInstrumentHealth(
                    symbol,
                    board.UpdatedAt,
                    lastUpdate,
                    row?.Last,
                    row?.QuoteVerified ?? false,
                    subscriptionActive,
                    updatesExpected,
                    stale,
                    stale ? (long)(started - lastUpdate).TotalSeconds : 0));
            }

            var anyStale = instruments.Any(i => i.IsStale);
            var state = ComponentHealthState.Healthy;
            string? error = null;
            if (!tradingDay || !intradayOpen)
            {
                state = ComponentHealthState.Healthy;
            }
            else if (anyStale)
            {
                state = ComponentHealthState.Degraded;
                error = "One or more monitored instruments have stale board prices during market hours.";
                _marketConsecutiveFailures++;
                _marketLastFailure = started;
            }
            else
            {
                _marketConsecutiveFailures = 0;
            }

            var snapshot = new MarketDataHealthSnapshot(
                state,
                feedSource,
                adminBrokerConnected,
                board.UpdatedAt,
                intradayOpen,
                tradingDay,
                CountActiveChartWatches(),
                instruments,
                _marketConsecutiveFailures,
                error,
                started,
                stopwatch.ElapsedMilliseconds);
            _lastMarket = snapshot;
            return Task.FromResult(snapshot);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Market data health probe failed.");
            _marketConsecutiveFailures++;
            _marketLastFailure = started;
            var snapshot = new MarketDataHealthSnapshot(
                ComponentHealthState.Degraded,
                "unknown",
                false,
                null,
                NseCashSession.IsLiveIntradayWindowNow(),
                NseCashSession.IsTradingDay(NseCashSession.IstToday()),
                0,
                [],
                _marketConsecutiveFailures,
                ex.Message,
                started,
                stopwatch.ElapsedMilliseconds);
            _lastMarket = snapshot;
            return Task.FromResult(snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _marketProbeBusy, 0);
        }
    }

    public Task<SignalRServerHealthSnapshot> ProbeSignalRAsync(CancellationToken cancellationToken)
    {
        if (!_options.SignalRMonitoringEnabled)
        {
            return Task.FromResult(_lastSignalR with { State = ComponentHealthState.Unknown });
        }

        if (Interlocked.Exchange(ref _signalRProbeBusy, 1) == 1)
        {
            return Task.FromResult(_lastSignalR);
        }

        try
        {
            var snapshot = _signalR.BuildSnapshot(DateTimeOffset.UtcNow, enabled: true);
            _lastSignalR = snapshot;
            return Task.FromResult(snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _signalRProbeBusy, 0);
        }
    }

    private int CountActiveChartWatches()
    {
        var count = 0;
            foreach (var symbol in IndexUniverse.IndexNames)
        {
            if (_chartStreams.HasWatchers(symbol))
            {
                count++;
            }
        }

        return count;
    }

    private static ComponentHealthState ResolveBrokerItemState(bool sessionPresent, bool verified, string? error)
    {
        if (!sessionPresent)
        {
            return ComponentHealthState.Unknown;
        }

        if (verified)
        {
            return ComponentHealthState.Healthy;
        }

        return string.IsNullOrWhiteSpace(error)
            ? ComponentHealthState.Degraded
            : ComponentHealthState.Degraded;
    }

    private static BrokerConnectivityHealthSnapshot UnknownBroker() =>
        new(ComponentHealthState.Unknown, [], null, null, 0, null, DateTimeOffset.MinValue, 0);

    private static MarketDataHealthSnapshot UnknownMarket() =>
        new(ComponentHealthState.Unknown, "unknown", false, null, false, false, 0, [], 0, null, DateTimeOffset.MinValue, 0);

    private static SignalRServerHealthSnapshot UnknownSignalR() =>
        new(ComponentHealthState.Unknown, 0, 0, 0, 0, null, null, null, null, "Not probed.", DateTimeOffset.MinValue, 0);
}