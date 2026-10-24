using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface IPaperProposalSource
{
    PaperAgentSnapshot Snapshot();
    void CompleteExternalProposal(string proposalId, string reason);
    void ReleasePendingProposal(string proposalId, string reason);
    void SetAutoAdmitTrades(bool enabled);
}

public sealed class PaperTradingAgent : IHostedService, IDisposable, IPaperProposalSource
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private readonly object _gate = new();
    private readonly MarketContextService _marketContext;
    private readonly BrokerConnectionManager _brokers;
    private readonly TradeHistoryStore _history;
    private readonly PaperDecisionEngine _decisions;
    private readonly PortfolioRiskManager _risk;
    private readonly ZoneSessionTracker _zoneSessions;
    private readonly ZoneBoardSessionTracker _zoneBoardSessions;
    private readonly StrategyRecommendationEngine _strategyFeed;
    private readonly Lazy<IZoneLiveAutoGateway> _zoneLive;
    private readonly ILogger<PaperTradingAgent> _log;
    private string? _deferZoneLiveProposalId;
    private readonly Dictionary<string, decimal> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Position> _positions = [];
    private readonly List<PaperAgentMessage> _messages = [];
    private readonly List<RankedDecisionSnapshot> _recentDecisions = [];
    private readonly Dictionary<string, DateTimeOffset> _signalNotifiedAt = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MarketContextSnapshot> _latestContext = [];
    private Timer? _timer;
    private PaperAgentConfig? _config = new(1_000_000m, 10_000m, 5_000m, 30, true);
    private PaperTradeProposal? _pending;
    private bool _running;
    private bool _userStoppedPaper;
    private decimal? _availableFunds;
    private decimal? _utilizedFunds;
    private decimal _realizedPnl;
    private int _closedTrades;
    private int _winningTrades;
    private int _losingTrades;
    private int _targetHits;
    private int _stopLossHits;
    private int _trailingStopHits;
    private int _breakevenTrades;
    private DateTime _sessionDate = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).Date;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastFundsRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDecisionScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCatalogAutoAdmit = DateTimeOffset.MinValue;
    private readonly HashSet<string> _catalogSignalsAdmitted = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _catalogAdmitDay;
    private int _busy;

    public PaperTradingAgent(
        MarketContextService marketContext,
        BrokerConnectionManager brokers,
        TradeHistoryStore history,
        PaperDecisionEngine decisions,
        PortfolioRiskManager risk,
        ZoneSessionTracker zoneSessions,
        ZoneBoardSessionTracker zoneBoardSessions,
        StrategyRecommendationEngine strategyFeed,
        Lazy<IZoneLiveAutoGateway> zoneLive,
        ILogger<PaperTradingAgent> log)
    {
        _marketContext = marketContext;
        _brokers = brokers;
        _history = history;
        _decisions = decisions;
        _risk = risk;
        _zoneSessions = zoneSessions;
        _zoneBoardSessions = zoneBoardSessions;
        _strategyFeed = strategyFeed;
        _zoneLive = zoneLive;
        _log = log;
        using (history.RunAsUser(history.StrategyEngineUserId))
        {
            foreach (var trade in history.RecoverOpenTrades())
            {
                _positions.Add(Position.FromRecovered(trade));
            }
        }
        AddMessage(
            "assistant",
            _positions.Count == 0
                ? "Paper agent ready with ₹10,00,000 allocated. Auto-admit ON — zone setups on all four indices (no confirm)."
                : $"Paper agent recovered {_positions.Count} open multi-leg paper position(s).");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _running = true;
        }
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    public async Task<PaperAgentSnapshot> HandleCommandAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message)) return Snapshot();
        AddMessage("user", message.Trim());
        var parsed = PaperAgentCommandParser.Parse(message);
        if (parsed.StopRequested)
        {
            lock (_gate)
            {
                CloseAllUnsafe("agent stopped");
                CancelPendingUnsafe("CANCELLED", "agent stopped");
                _running = false;
                _userStoppedPaper = true;
                AddMessageUnsafe("assistant", "Paper automation stopped and all paper positions were closed.");
                return SnapshotUnsafe();
            }
        }
        if (parsed.ConfirmRequested)
        {
            IReadOnlyList<MarketContextSnapshot> fresh;
            try
            {
                fresh = await _marketContext.BuildAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Confirmation market refresh failed.");
                AddMessage("assistant", "Fresh market data was unavailable, so no paper fill was made.");
                return Snapshot();
            }
            lock (_gate)
            {
                ConfirmPendingUnsafe(fresh);
                return SnapshotUnsafe();
            }
        }
        if (parsed.RejectRequested)
        {
            lock (_gate)
            {
                if (_pending is null) AddMessageUnsafe("assistant", "There is no pending trade to reject.");
                else
                {
                    var symbol = _pending.Symbol;
                    CancelPendingUnsafe("REJECTED", "rejected by user");
                    AddMessageUnsafe("assistant", $"{symbol} paper trade rejected. I will continue scanning.");
                }
                return SnapshotUnsafe();
            }
        }
        if (parsed.Intent == PaperAgentIntent.Greeting)
        {
            AddMessage("assistant", "Hello. I scan deterministic paper strategies, show their evidence and risk, and wait for confirmation.");
            return Snapshot();
        }
        if (parsed.Intent == PaperAgentIntent.Help)
        {
            AddMessage("assistant", "Try: “Use ₹1 lakh, target ₹1,000, max loss ₹500.” Commands: STATUS, CONFIRM, REJECT, STOP.");
            return Snapshot();
        }
        if (parsed.Intent == PaperAgentIntent.Status)
        {
            lock (_gate)
            {
                AddMessageUnsafe("assistant", _running
                    ? $"Paper scanning is active with {_positions.Count} open position(s), total P&L ₹{_realizedPnl + UnrealizedPnlUnsafe():N2}."
                    : "Paper automation is stopped.");
                return SnapshotUnsafe();
            }
        }
        if (parsed.Intent != PaperAgentIntent.ConfigureAndStart)
        {
            AddMessage("assistant", "I did not start trading because that was not an explicit trading command. Say HELP.");
            return Snapshot();
        }

        PaperAgentConfig? existing;
        lock (_gate) existing = _config;
        var capital = parsed.AllocatedCapital ?? existing?.AllocatedCapital;
        var target = parsed.ProfitTarget ?? existing?.ProfitTarget;
        var loss = parsed.MaxDailyLoss ?? existing?.MaxDailyLoss;
        var missing = new List<string>();
        if (capital is null) missing.Add("amount to use");
        if (target is null) missing.Add("profit target");
        if (loss is null) missing.Add("maximum daily loss");
        if (missing.Count > 0)
        {
            AddMessage("assistant", $"Please provide: {string.Join(", ", missing)}.");
            return Snapshot();
        }
        if (capital <= 0 || target <= 0 || loss <= 0 || loss >= capital)
        {
            AddMessage("assistant", "Amounts must be positive, and maximum daily loss must be lower than allocated capital.");
            return Snapshot();
        }
        await RefreshFundsAsync(ct, true);
        lock (_gate)
        {
            if (_availableFunds is > 0 && capital > _availableFunds)
            {
                AddMessageUnsafe("assistant", $"Cannot allocate ₹{capital:N0}; m.Stock reports ₹{_availableFunds:N0} available.");
                return SnapshotUnsafe();
            }
            CloseAllUnsafe("agent reconfigured");
            CancelPendingUnsafe("CANCELLED", "agent configuration changed");
            _config = new(capital!.Value, target!.Value, loss!.Value, 3, true);
            _running = true;
            _userStoppedPaper = false;
            _realizedPnl = 0;
            _closedTrades = 0;
            _winningTrades = 0;
            _losingTrades = 0;
            _targetHits = 0;
            _stopLossHits = 0;
            _trailingStopHits = 0;
            _breakevenTrades = 0;
            _previous.Clear();
            AddMessageUnsafe(
                "assistant",
                $"Paper automation started: capital ₹{capital:N0}, target ₹{target:N0}, daily loss ₹{loss:N0}. "
                + "Scores are deterministic rule evidence, not probabilities. "
                + "Paper trades auto-admit when a signal passes risk; arming live trading pauses auto-admit. "
                + "Track positive trades, target hits, and stop-loss exits in the Paper performance panel.");
            return SnapshotUnsafe();
        }
    }

    public PaperAgentSnapshot Snapshot()
    {
        lock (_gate) return SnapshotUnsafe();
    }

    public decimal AllocatedCapitalPerTrade()
    {
        lock (_gate)
        {
            return _config?.AllocatedCapital ?? 1_000_000m;
        }
    }

    public void CompleteExternalProposal(string proposalId, string reason)
    {
        lock (_gate)
        {
            if (_pending is null || !_pending.Id.Equals(proposalId, StringComparison.Ordinal)) return;
            var underlying = _pending.Underlying;
            CancelPendingUnsafe("CANCELLED", reason);
            AddMessageUnsafe("assistant", $"{underlying} was routed to verified live execution.");
        }
    }

    public void ReleasePendingProposal(string proposalId, string reason)
    {
        lock (_gate)
        {
            if (_pending is null || !_pending.Id.Equals(proposalId, StringComparison.Ordinal)) return;
            var underlying = _pending.Underlying;
            CancelPendingUnsafe("CANCELLED", reason);
            AddMessageUnsafe("assistant", $"{underlying} live entry cleared: {reason}");
        }
    }

    public void SetAutoAdmitTrades(bool enabled)
    {
        lock (_gate)
        {
            if (_config is null) return;
            _config = _config with { AutoAdmitTrades = enabled };
        }
    }

    /// <summary>Operations emergency flatten — closes open paper positions without altering strategy engines.</summary>
    public PaperAgentSnapshot EmergencyFlattenOpenPositions(string reason)
    {
        lock (_gate)
        {
            CloseAllUnsafe(reason);
            return SnapshotUnsafe();
        }
    }

    public PaperAgentSnapshot RequestStrategyPaperTrade(string strategy, string underlying)
    {
        lock (_gate)
        {
            if (_config is null)
            {
                _config = new PaperAgentConfig(1_000_000m, 10_000m, 5_000m, 30, true);
            }

            _running = true;
            if (_latestContext.Count == 0)
            {
                AddMessageUnsafe("assistant", "Market data is still loading — try the paper trade again in a few seconds.");
                return SnapshotUnsafe();
            }

            var strategyCode = StrategyRecommendationEngine.StrategyCodeFromCatalog(
                StrategyRecommendationEngine.MapStrategyCode(strategy));

            StrategyLiveRecommendation? feedRec = null;
            try
            {
                feedRec = _strategyFeed.BuildFeed(CancellationToken.None).LiveNow.FirstOrDefault(r =>
                    StrategyRecommendationEngine.StrategyCodeFromCatalog(
                        StrategyRecommendationEngine.MapStrategyCode(r.StrategyId))
                        .Equals(strategyCode, StringComparison.OrdinalIgnoreCase)
                    && r.Symbol.Equals(underlying, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Strategy feed unavailable for take-strategy.");
            }

            var candidate = ResolveCandidateForFeed(feedRec, strategyCode, underlying)
                ?? _decisions.Rank(_latestContext, _previous).FirstOrDefault(c =>
                    c.Strategy.Equals(strategyCode, StringComparison.OrdinalIgnoreCase)
                    && c.Underlying.Equals(underlying, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                AddMessageUnsafe(
                    "assistant",
                    $"No active {strategy} setup on {underlying} for the agent right now. The signal may have expired.");
                return SnapshotUnsafe();
            }

            var assessment = AssessPaperRisk(candidate, DateTimeOffset.UtcNow);
            if (!assessment.Accepted)
            {
                AddMessageUnsafe(
                    "assistant",
                    $"Paper trade blocked for {underlying}: {string.Join("; ", assessment.Rejections)}");
                return SnapshotUnsafe();
            }

            if (_pending is not null)
            {
                CancelPendingUnsafe("CANCELLED", "replaced by strategy paper trade");
            }

            CreateProposalUnsafe(candidate, assessment, DateTimeOffset.UtcNow, _latestContext);
            AddMessageUnsafe(
                "assistant",
                $"Strategy paper trade requested: {candidate.Strategy} on {underlying}. "
                + $"Why: {string.Join(" · ", candidate.Evidence)}");
            return SnapshotUnsafe();
        }
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            using (_history.RunAsUser(_history.StrategyEngineUserId))
            {
                await TickCoreAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task TickCoreAsync()
    {
        string? zoneLiveProposalId = null;
        try
        {
            await RefreshFundsAsync(CancellationToken.None);
            var context = await _marketContext.BuildAsync(CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            var nowIst = TimeZoneInfo.ConvertTime(now, Ist);
            lock (_gate)
            {
                _latestContext = context;
                if (_sessionDate != nowIst.Date)
                {
                    CloseAllUnsafe("new trading day");
                    CancelPendingUnsafe("EXPIRED", "new trading day");
                    _realizedPnl = 0;
                    _closedTrades = 0;
                    _winningTrades = 0;
                    _losingTrades = 0;
                    _targetHits = 0;
                    _stopLossHits = 0;
                    _trailingStopHits = 0;
                    _breakevenTrades = 0;
                    _sessionDate = nowIst.Date;
                    _previous.Clear();
                    _catalogSignalsAdmitted.Clear();
                    _catalogAdmitDay = default;
                    if (!_userStoppedPaper && _config is not null)
                    {
                        _running = true;
                    }
                }
                UpdatePositionsUnsafe(context, nowIst);
                if (_pending is not null && _pending.ExpiresAt <= now)
                {
                    var symbol = _pending.Symbol;
                    CancelPendingUnsafe("EXPIRED", "confirmation timed out");
                    AddMessageUnsafe("assistant", $"{symbol} proposal expired.");
                }
                ApplySessionLimitsUnsafe(nowIst);
                if (_running && _config is not null && _pending is null
                    && now - _lastDecisionScan >= TimeSpan.FromSeconds(5))
                {
                    ScanEntriesUnsafe(context, now);
                    _lastDecisionScan = now;
                }
                if (_running && _config?.AutoAdmitTrades == true && _pending is null
                    && now - _lastCatalogAutoAdmit >= TimeSpan.FromSeconds(8))
                {
                    AutoAdmitCatalogStrategiesUnsafe(context, now);
                    _lastCatalogAutoAdmit = now;
                }
                foreach (var item in context.Where(x => x.InstrumentType != "OPTION")) _previous[item.Symbol] = item.Last;
                var istDay = DateOnly.FromDateTime(nowIst.DateTime);
                foreach (var item in context.Where(x => IndexUniverse.IsIndex(x.Symbol)))
                {
                    if (ZoneRejectionPattern.TryReadZoneFact(item.Evidence.Facts, "B1=", out var b1)
                        && ZoneRejectionPattern.TryReadZoneFact(item.Evidence.Facts, "S1=", out var s1)
                        && ZoneRejectionPattern.TryReadZoneFact(item.Evidence.Facts, "S2=", out var s2))
                    {
                        _zoneBoardSessions.Update(item.Symbol, item.Last, b1, s1, s2, istDay);
                        _zoneSessions.Update(item.Symbol, item.Last, b1, istDay);
                    }
                }
                _updatedAt = now;
                zoneLiveProposalId = _deferZoneLiveProposalId;
                _deferZoneLiveProposalId = null;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Paper trading agent tick failed.");
        }

        if (zoneLiveProposalId is not null)
        {
            try
            {
                await _zoneLive.Value.ExecuteZoneProposalAsync(zoneLiveProposalId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Zone auto-live execution failed for proposal {ProposalId}.", zoneLiveProposalId);
            }
        }
    }

    private void ApplySessionLimitsUnsafe(DateTimeOffset nowIst)
    {
        if (!_running || _config is null) return;
        // Paper mode: no daily profit target or loss limit — agent keeps scanning until EOD square-off.
        if (nowIst.TimeOfDay >= new TimeSpan(15, 20, 0))
        {
            CloseAllUnsafe("15:20 intraday square-off");
            CancelPendingUnsafe("EXPIRED", "15:20 intraday square-off");
            _running = false;
        }
    }

    private static bool IsCatalogStrategy(string strategy) =>
        strategy is TenAmIntradayPattern.HighFadeStrategy
            or TenAmIntradayPattern.LowBreakStrategy
            or ZoneRejectionPattern.B1RejectionToS2;

    private RiskAssessment AssessPaperRisk(StrategyCandidate candidate, DateTimeOffset now) =>
        _risk.Assess(
            candidate,
            _config!,
            _realizedPnl,
            _positions.Sum(x => x.MaxRisk),
            _positions.Count,
            _positions.Select(x => new PortfolioExposure(x.Underlying, x.Direction, x.MaxRisk, x.Strategy)).ToList(),
            now,
            paperTrading: true,
            brokerAvailableFunds: _availableFunds);

    private bool HasOpenPaperSetup(string strategy, string underlying) =>
        _positions.Any(p =>
            p.Strategy.Equals(strategy, StringComparison.OrdinalIgnoreCase)
            && p.Underlying.Equals(underlying, StringComparison.OrdinalIgnoreCase));

    private void AutoAdmitCatalogStrategiesUnsafe(
        IReadOnlyList<MarketContextSnapshot> context,
        DateTimeOffset now)
    {
        if (_config is null || !_running || _pending is not null
            || _positions.Count >= _config.MaxOpenPositions)
        {
            return;
        }

        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Ist).DateTime);
        if (_catalogAdmitDay != istDay)
        {
            _catalogAdmitDay = istDay;
            _catalogSignalsAdmitted.Clear();
        }

        StrategyFeedResponse feed;
        try
        {
            feed = _strategyFeed.BuildFeed(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Strategy feed unavailable for catalog auto-admit.");
            return;
        }

        foreach (var rec in feed.LiveNow.Where(r => r.CanPaperTrade))
        {
            if (_catalogSignalsAdmitted.Contains(rec.Id))
            {
                continue;
            }

            var strategyCode = StrategyRecommendationEngine.StrategyCodeFromCatalog(
                StrategyRecommendationEngine.MapStrategyCode(rec.StrategyId));

            if (HasOpenPaperSetup(strategyCode, rec.Symbol))
            {
                _catalogSignalsAdmitted.Add(rec.Id);
                continue;
            }

            var candidate = ResolveCandidateForFeed(rec, strategyCode);
            if (candidate is null)
            {
                continue;
            }

            var assessment = AssessPaperRisk(candidate, now);
            if (!assessment.Accepted)
            {
                _log.LogDebug(
                    "Auto-admit skipped {Symbol} {Strategy}: {Rejections}",
                    rec.Symbol,
                    strategyCode,
                    string.Join("; ", assessment.Rejections));
                continue;
            }

            _catalogSignalsAdmitted.Add(rec.Id);
            CreateProposalUnsafe(candidate, assessment, now, context);
            AddMessageUnsafe(
                "assistant",
                $"Auto paper trade — {rec.StrategyName} on {rec.Symbol}. "
                + $"Why: {string.Join(" · ", candidate.Evidence)}. "
                + $"Live capital about ₹{assessment.CapitalRequired:N0}, max loss ₹{assessment.MaxRisk:N0}, max profit ₹{assessment.MaxReward:N0}.");
            if (_positions.Count >= _config!.MaxOpenPositions || _pending is not null)
            {
                break;
            }
        }
    }

    private StrategyCandidate? ResolveCandidateForFeed(
        StrategyLiveRecommendation rec,
        string strategyCode) =>
        ResolveCandidateForFeed(rec, strategyCode, rec.Symbol);

    private StrategyCandidate? ResolveCandidateForFeed(
        StrategyLiveRecommendation? rec,
        string strategyCode,
        string underlying)
    {
        if (_latestContext.Count == 0)
        {
            return null;
        }

        var fromEngine = _decisions.Rank(_latestContext, _previous).FirstOrDefault(c =>
            c.Strategy.Equals(strategyCode, StringComparison.OrdinalIgnoreCase)
            && c.Underlying.Equals(underlying, StringComparison.OrdinalIgnoreCase));
        if (fromEngine is not null)
        {
            return fromEngine;
        }

        if (rec is null)
        {
            return null;
        }

        return FeedPaperCandidateBuilder.FromRecommendation(rec, _latestContext);
    }

    private void ScanEntriesUnsafe(IReadOnlyList<MarketContextSnapshot> context, DateTimeOffset now)
    {
        if (_config is null || _positions.Count >= _config.MaxOpenPositions) return;
        StrategyCandidate? proposalCandidate = null;
        RiskAssessment? proposalAssessment = null;
        var autoAdmitAll = _config.AutoAdmitTrades;
        foreach (var candidate in _decisions.Rank(context, _previous))
        {
            var assessment = AssessPaperRisk(candidate, now);
            var id = Guid.NewGuid().ToString("N");
            var decision = new RankedDecisionSnapshot(
                id, candidate.Strategy, candidate.Underlying, candidate.Direction, candidate.EvidenceScore,
                assessment.Accepted, candidate.Evidence, assessment.Rejections, assessment.MaxRisk,
                assessment.MaxReward,
                assessment.MaxRisk > 0 ? assessment.MaxReward / assessment.MaxRisk : 0,
                now);
            RememberDecisionUnsafe(decision);
            _history.SaveDecision(decision);
            if (!assessment.Accepted) continue;
            if (IsUniverseUnderlying(candidate.Underlying))
            {
                MaybeNotifySignalUnsafe(candidate, assessment, now);
            }
                if (autoAdmitAll)
            {
                if (_pending is not null || _positions.Count >= _config.MaxOpenPositions)
                {
                    break;
                }
                if (HasOpenPaperSetup(candidate.Strategy, candidate.Underlying))
                {
                    continue;
                }
                if ((IsZoneBoardStrategy(candidate.Strategy)
                        || candidate.Strategy == ZoneRejectionPattern.B1RejectionToS2)
                    && !IndexUniverse.IndexNames.Contains(candidate.Underlying, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsZoneBoardStrategy(candidate.Strategy)
                    && !ZoneStrategyRollout.AllowPaperAutoExecute(candidate.Strategy, candidate.Underlying))
                {
                    continue;
                }

                CreateProposalUnsafe(candidate, assessment, now, context);
                continue;
            }
            proposalCandidate ??= candidate;
            proposalAssessment ??= assessment;
        }

        if (!autoAdmitAll && proposalCandidate is not null && proposalAssessment is not null)
        {
            CreateProposalUnsafe(proposalCandidate, proposalAssessment, now, context);
        }
    }

    private static bool IsUniverseUnderlying(string underlying) =>
        IndexUniverse.IsIndex(underlying)
        || IndexUniverse.Equities.Contains(underlying, StringComparer.OrdinalIgnoreCase);

    private void MaybeNotifySignalUnsafe(StrategyCandidate candidate, RiskAssessment assessment, DateTimeOffset now)
    {
        var key = $"{candidate.Underlying}|{candidate.Strategy}";
        if (_signalNotifiedAt.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(15))
        {
            return;
        }
        _signalNotifiedAt[key] = now;
        var followUp = _config?.AutoAdmitTrades == true
            ? "Auto-admit will enter paper if risk checks pass."
            : "Watch for the confirmation prompt or review Recent decisions.";
        AddMessageUnsafe(
            "assistant",
            $"Signal — {candidate.Strategy} on {candidate.Underlying} ({candidate.Direction}): "
            + $"score {candidate.EvidenceScore:N0}, max risk ₹{assessment.MaxRisk:N2}, max reward ₹{assessment.MaxReward:N2}. "
            + followUp);
    }

    private void CreateProposalUnsafe(
        StrategyCandidate candidate,
        RiskAssessment assessment,
        DateTimeOffset now,
        IReadOnlyList<MarketContextSnapshot> context)
    {
        var id = Guid.NewGuid().ToString("N");
        var legs = candidate.Legs.Select((leg, i) => new PaperTradeLegProposal(
            $"{id}-{i + 1}", leg.Symbol, leg.Underlying, leg.InstrumentType, leg.Side, leg.Ratio,
            assessment.QuantityMultiplier * leg.Ratio, leg.Price, leg.Strike, leg.OptionRight, leg.Expiry, leg.Quote)).ToList();
        var net = legs.Sum(x => (x.Side == "BUY" ? 1m : -1m) * x.IndicativePrice * x.Ratio);
        _pending = new(
            id,
            legs.Count == 1 ? legs[0].InstrumentType : "MULTI_LEG",
            candidate.Underlying,
            candidate.Direction,
            assessment.QuantityMultiplier,
            net,
            candidate.StopLoss,
            candidate.Target,
            string.Join("; ", candidate.Evidence),
            now.AddMinutes(1),
            candidate.Strategy,
            candidate.Underlying,
            candidate.EvidenceScore,
            assessment.MaxRisk,
            assessment.MaxReward,
            assessment.CapitalRequired,
            assessment.MaxRisk > 0 ? assessment.MaxReward / assessment.MaxRisk : 0,
            candidate.Evidence,
            legs,
            candidate.DataTimestamp);
        _history.SaveMultiProposal(_pending);
        if (_config?.AutoAdmitTrades == true)
        {
            if (_zoneLive.Value.ShouldAutoExecuteZoneLive(candidate.Strategy, candidate.Underlying))
            {
                AddMessageUnsafe(
                    "assistant",
                    $"Zone LIVE AUTO: {candidate.Strategy} on {candidate.Underlying} — submitting {string.Join(", ", legs.Select(x => $"{x.Side} {x.Symbol}"))} to broker.");
                _deferZoneLiveProposalId = id;
                return;
            }

            ConfirmPendingUnsafe(context);
            return;
        }

        AddMessageUnsafe(
            "assistant",
            $"Paper proposal {candidate.Strategy} on {candidate.Underlying}: "
            + $"{string.Join(", ", legs.Select(x => $"{x.Side} {x.Symbol} × {x.Quantity}"))}. "
            + $"Max risk ₹{assessment.MaxRisk:N2}, max reward ₹{assessment.MaxReward:N2}, evidence score {candidate.EvidenceScore:N0} (not probability). "
            + "Reply CONFIRM or REJECT.");
    }

    private void ConfirmPendingUnsafe(IReadOnlyList<MarketContextSnapshot> fresh)
    {
        if (_pending is null)
        {
            AddMessageUnsafe("assistant", "There is no pending trade to confirm.");
            return;
        }
        var proposal = _pending;
        if (proposal.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            CancelPendingUnsafe("EXPIRED", "confirmation timed out");
            AddMessageUnsafe("assistant", "The proposal expired. No fill was made.");
            return;
        }
        var proposedLegs = proposal.Legs ?? [];
        var fills = new List<PaperPositionLegSnapshot>();
        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var isHedgedSpread = proposal.Strategy.Contains("SPREAD", StringComparison.OrdinalIgnoreCase);
        if (_config is null
            || _positions.Count >= _config.MaxOpenPositions
            || (isHedgedSpread
                && (_config.AllocatedCapital < 100_000m
                    || !IsIndexUnderlying(proposal.Underlying)
                    || !proposedLegs.Any(leg => leg.Side == "BUY")
                    || !proposedLegs.Any(leg => leg.Side == "SELL")
                    || _positions.Any(position =>
                        position.Strategy.Contains("SPREAD", StringComparison.OrdinalIgnoreCase))))
            || nowIst.TimeOfDay < new TimeSpan(9, 20, 0)
            || nowIst.TimeOfDay >= new TimeSpan(15, 10, 0)
            || (proposedLegs.Any(x => x.InstrumentType == "OPTION")
                && !IsZoneBoardStrategy(proposal.Strategy)
                && _positions.Any(x => IsIndexUnderlying(x.Underlying)
                    && x.Underlying.Equals(proposal.Underlying, StringComparison.OrdinalIgnoreCase)
                    && x.Direction == proposal.Direction)))
        {
            CancelPendingUnsafe("CANCELLED", "portfolio or entry-session constraints changed");
            AddMessageUnsafe("assistant", "Portfolio or entry-session constraints changed, so the proposal was cancelled.");
            return;
        }
        foreach (var leg in proposedLegs)
        {
            var quote = fresh.FirstOrDefault(x => x.Symbol.Equals(leg.Symbol, StringComparison.OrdinalIgnoreCase));
            if (quote is null || !quote.Quote.IsFresh || quote.Last <= 0)
            {
                CancelPendingUnsafe("CANCELLED", $"fresh quote unavailable for {leg.Symbol}");
                AddMessageUnsafe("assistant", $"Fresh data for {leg.Symbol} was unavailable. The entire proposal was cancelled.");
                return;
            }
            if (leg.InstrumentType == "OPTION"
                && (quote.Quote.Bid is null || quote.Quote.Ask is null
                    || quote.Quote.Ask <= 0
                    || (quote.Quote.Ask - quote.Quote.Bid) / quote.Quote.Ask > 0.08m
                    || quote.Quote.Volume is null or <= 0
                    || quote.Quote.OpenInterest is null or <= 0))
            {
                CancelPendingUnsafe("CANCELLED", $"liquidity changed for {leg.Symbol}");
                AddMessageUnsafe("assistant", $"Liquidity for {leg.Symbol} no longer passed risk checks. The proposal was cancelled.");
                return;
            }
            var executablePrice = leg.InstrumentType == "OPTION"
                ? leg.Side == "BUY"
                    ? quote.Quote.Ask!.Value
                    : quote.Quote.Bid!.Value
                : quote.Last;
            var drift = Math.Abs(executablePrice - leg.IndicativePrice) / leg.IndicativePrice;
            if (drift > 0.005m)
            {
                CancelPendingUnsafe("CANCELLED", $"{leg.Symbol} moved more than 0.5%");
                AddMessageUnsafe("assistant", $"{leg.Symbol} moved {drift:P2}; all legs were cancelled with no partial fill.");
                return;
            }
            fills.Add(new(
                leg.Id, leg.Symbol, leg.Side, leg.Quantity, executablePrice, executablePrice, 0,
                leg.Strike, leg.OptionRight, leg.Expiry));
        }
        var (validatedRisk, validatedReward, validatedNet) = RecalculatePayoff(proposal, fills);
        if (fills.Count == 0
            || validatedRisk <= 0
            || validatedReward <= 0
            || validatedRisk > proposal.MaxRisk * 1.02m
            || (_config.AutoAdmitTrades != true
                && _positions.Sum(x => x.MaxRisk) + validatedRisk > _config.MaxDailyLoss + _realizedPnl))
        {
            CancelPendingUnsafe("CANCELLED", "aggregate risk changed");
            AddMessageUnsafe("assistant", "Aggregate risk changed, so the proposal was cancelled.");
            return;
        }
        var validatedProposal = proposal with
        {
            IndicativePrice = validatedNet,
            MaxRisk = validatedRisk,
            MaxReward = validatedReward,
            RewardRisk = validatedReward / validatedRisk,
        };
        var openedAt = DateTimeOffset.UtcNow;
        var openingRegime = fresh.FirstOrDefault(x =>
            x.Symbol.Equals(proposal.Underlying, StringComparison.OrdinalIgnoreCase))?.Evidence.Regime ?? "UNKNOWN";
        var position = Position.FromProposal(validatedProposal, fills, openedAt, openingRegime);
        _history.OpenMultiLegAtomic(proposal.Id, fills, openedAt, validatedRisk, validatedReward);
        _history.MarkOpened(proposal.Id, proposal.Quantity, validatedNet, openedAt);
        _positions.Add(position);
        _pending = null;
        var admitLabel = _config?.AutoAdmitTrades == true ? "Auto-admitted" : "Confirmed";
        AddMessageUnsafe("assistant", $"{admitLabel} paper fill: {proposal.Strategy} {proposal.Underlying}, {fills.Count} leg(s).");
    }

    private void UpdatePositionsUnsafe(IReadOnlyList<MarketContextSnapshot> context, DateTimeOffset nowIst)
    {
        foreach (var position in _positions.ToList())
        {
            foreach (var leg in position.Legs)
            {
                var quote = context.FirstOrDefault(x => x.Symbol.Equals(leg.Symbol, StringComparison.OrdinalIgnoreCase));
                if (quote is not null && quote.Last > 0)
                {
                    var mark = leg.OptionRight is null
                        ? quote.Last
                        : leg.Side == "BUY"
                            ? quote.Quote.Bid ?? quote.Last
                            : quote.Quote.Ask ?? quote.Last;
                    if (leg.EntryPrice > 200m && mark <= 150m && leg.EntryPrice / mark > 3m)
                    {
                        continue;
                    }

                    leg.CurrentPrice = mark;
                }
                else if (leg.CurrentPrice <= 0 && leg.EntryPrice > 0)
                {
                    leg.CurrentPrice = leg.EntryPrice;
                }
            }
            _history.UpdateLegMarks(position.Id, position.LegSnapshots());
            var pnl = PositionPnl(position);
            position.PeakPnl = Math.Max(position.PeakPnl, pnl);
            string? reason = null;
            if (position.Strategy is ZoneBoardPattern.B1BreakLong or ZoneBoardPattern.S2BounceLong)
            {
                var last = ZoneBoardMarkPrice(position, context) ?? position.Legs[0].CurrentPrice;
                if (last <= position.StopLoss) reason = "hard stop";
                else if (last >= position.Target) reason = "target reached";
            }
            else if (position.Strategy is ZoneBoardPattern.S1BreakShort or ZoneRejectionPattern.B1RejectionToS2)
            {
                var last = ZoneBoardMarkPrice(position, context) ?? position.Legs[0].CurrentPrice;
                if (last >= position.StopLoss) reason = "hard stop";
                else if (last <= position.Target) reason = "target reached";
            }
            else if (position.Strategy is TenAmIntradayPattern.HighFadeStrategy or TenAmIntradayPattern.LowBreakStrategy)
            {
                var leg = position.Legs[0];
                var last = leg.CurrentPrice;
                var entry = leg.EntryPrice;
                var isShort = leg.Side == "SELL";
                var reference = ParseTenAmReference(position.TriggerReason);
                var stop = position.StopLoss;
                TenAmIntradayPattern.ApplyTrailingStop(
                    position.Strategy,
                    reference,
                    entry,
                    last,
                    ref stop,
                    isShort);
                position.StopLoss = stop;
                if (isShort)
                {
                    if (last >= position.StopLoss) reason = "hard stop";
                    else if (last <= position.Target) reason = "target reached";
                }
                else
                {
                    if (last <= position.StopLoss) reason = "hard stop";
                    else if (last >= position.Target) reason = "target reached";
                }
            }
            if (reason is null && pnl <= -position.MaxRisk * 0.55m) reason = "hard stop";
            if (reason is null && position.MaxReward > 0 && pnl >= position.MaxReward * 0.65m) reason = "target reached";
            if (reason is null && position.PeakPnl >= position.MaxReward * 0.45m
                && pnl <= position.PeakPnl - position.MaxReward * 0.25m) reason = "trailing stop";
            if (reason is null && DateTimeOffset.UtcNow >= position.ExitDeadline) reason = "time exit";
            var underlying = context.FirstOrDefault(x => x.Symbol.Equals(position.Underlying, StringComparison.OrdinalIgnoreCase));
            if (reason is null && underlying?.Evidence.Regime == "VOLATILE"
                && position.OpeningRegime != "VOLATILE") reason = "adverse regime";
            if (reason is null && nowIst.TimeOfDay >= new TimeSpan(15, 20, 0)) reason = "15:20 intraday square-off";
            if (reason is not null) CloseUnsafe(position, reason);
        }
    }

    private void CloseAllUnsafe(string reason)
    {
        foreach (var position in _positions.ToList()) CloseUnsafe(position, reason);
    }

    private static decimal ParseTenAmReference(string reason)
    {
        foreach (var token in reason.Split('·', '|', ';', '\n', ' '))
        {
            if (token.StartsWith("H10=", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("L10=", StringComparison.OrdinalIgnoreCase))
            {
                var value = token[(token.IndexOf('=') + 1)..];
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var level))
                {
                    return level;
                }
            }
        }

        return 0;
    }

    private void CloseUnsafe(Position position, string reason)
    {
        foreach (var leg in position.Legs)
        {
            if (leg.CurrentPrice <= 0 && leg.EntryPrice > 0)
            {
                leg.CurrentPrice = leg.EntryPrice;
            }
        }

        var pnl = PositionPnl(position);
        if (position.Legs.Count == 1)
        {
            var leg = position.Legs[0];
            var exit = leg.CurrentPrice;
            if (exit > 0 && leg.EntryPrice > 0)
            {
                pnl = PaperTradeEconomics.RealizedPnl(
                    position.Direction,
                    leg.EntryPrice,
                    exit,
                    leg.Quantity);
            }
        }

        _realizedPnl += pnl;
        _closedTrades++;
        if (pnl > 0) _winningTrades++;
        else if (pnl < 0) _losingTrades++;
        else _breakevenTrades++;
        if (PaperExitAnalytics.IsTargetHit(reason)) _targetHits++;
        if (PaperExitAnalytics.IsStopLossHit(reason)) _stopLossHits++;
        if (PaperExitAnalytics.IsTrailingStopHit(reason)) _trailingStopHits++;
        _positions.Remove(position);
        var legs = position.LegSnapshots();
        _history.CloseMultiLegAtomic(position.Id, legs, reason, pnl, DateTimeOffset.UtcNow);
        _history.MarkFinal(position.Id, "CLOSED", reason, position.NetMark, pnl, DateTimeOffset.UtcNow);
        AddMessageUnsafe("assistant", $"Paper {position.Strategy} {position.Underlying} closed ({reason}); P&L ₹{pnl:N2}.");
    }

    private void CancelPendingUnsafe(string status, string reason)
    {
        if (_pending is null) return;
        _history.MarkFinal(_pending.Id, status, reason);
        _history.MarkMultiFinal(_pending.Id, status, reason, DateTimeOffset.UtcNow);
        _pending = null;
    }

    private async Task RefreshFundsAsync(CancellationToken ct, bool force = false)
    {
        var broker = _brokers.Market;
        if (!broker.HasSession || (!force && DateTimeOffset.UtcNow - _lastFundsRefresh < TimeSpan.FromSeconds(15))) return;
        try
        {
            var funds = await broker.GetFundSummaryAsync(ct);
            lock (_gate)
            {
                _lastFundsRefresh = DateTimeOffset.UtcNow;
                if (funds is not null)
                {
                    _availableFunds = funds.AvailableBalance;
                    _utilizedFunds = funds.AmountUtilized;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Unable to refresh m.Stock funds.");
        }
    }

    private PaperAgentSnapshot SnapshotUnsafe()
    {
        SyncOpenPositionsFromStoreUnsafe();
        var positions = _positions.Select(x => x.Snapshot()).ToList();
        var unrealized = positions.Sum(x => x.Pnl);
        var indexes = _latestContext.Where(x => x.InstrumentType == "INDEX").ToList();
        var regime = indexes.Count == 0
            ? null
            : new MarketRegimeSnapshot(
                indexes.GroupBy(x => x.Evidence.Regime).OrderByDescending(x => x.Count()).First().Key,
                indexes.Min(x => x.Quote.DataTimestamp),
                indexes.All(x => x.Quote.IsFresh),
                indexes.Select(x => $"{x.Symbol}:{x.Evidence.Trend}/{x.Evidence.Regime}").ToList());
        return new(
            true,
            _running,
            _pending is not null ? "AWAITING CONFIRMATION" : _running ? "SCANNING STRATEGIES" : "STOPPED",
            _availableFunds,
            _utilizedFunds,
            _config,
            _realizedPnl,
            unrealized,
            _realizedPnl + unrealized,
            _pending,
            positions,
            _messages.TakeLast(50).ToList(),
            _updatedAt,
            regime,
            _recentDecisions.TakeLast(50).Reverse().ToList(),
            _history.RecentEvents(50),
            new(
                _closedTrades,
                _winningTrades,
                _losingTrades,
                _closedTrades == 0 ? 0 : decimal.Round((decimal)_winningTrades / _closedTrades * 100m, 2),
                _realizedPnl,
                unrealized,
                _positions.Sum(x => x.MaxRisk),
                _targetHits,
                _stopLossHits,
                _trailingStopHits,
                _breakevenTrades),
            null);
    }

    private void SyncOpenPositionsFromStoreUnsafe()
    {
        foreach (var trade in _history.RecoverOpenTrades())
        {
            if (_positions.Any(p => p.Id.Equals(trade.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _positions.Add(Position.FromRecovered(trade));
            _log.LogInformation(
                "Re-synced open paper position {TradeId} {Underlying} from durable store.",
                trade.Id,
                trade.Underlying);
        }
    }

    private decimal UnrealizedPnlUnsafe() => _positions.Sum(PositionPnl);

    private static bool IsZoneBoardStrategy(string strategy) =>
        strategy is ZoneBoardPattern.B1BreakLong
            or ZoneBoardPattern.S1BreakShort
            or ZoneBoardPattern.S2BounceLong;

    private static decimal? ZoneBoardMarkPrice(Position position, IReadOnlyList<MarketContextSnapshot> context)
    {
        if (position.Legs.All(leg => leg.OptionRight is null))
        {
            return null;
        }

        var underlying = context.FirstOrDefault(x =>
            x.Symbol.Equals(position.Underlying, StringComparison.OrdinalIgnoreCase));
        return underlying is { Last: > 0 } ? underlying.Last : null;
    }

    private static bool IsIndexUnderlying(string value) =>
        value is "NIFTY" or "BANKNIFTY" or "FINNIFTY" or "SENSEX";

    private static decimal PositionPnl(Position position) => position.Legs.Sum(leg =>
        (leg.Side == "BUY" ? leg.CurrentPrice - leg.EntryPrice : leg.EntryPrice - leg.CurrentPrice) * leg.Quantity);

    private static (decimal MaxRisk, decimal MaxReward, decimal NetPremium) RecalculatePayoff(
        PaperTradeProposal proposal,
        IReadOnlyList<PaperPositionLegSnapshot> fills)
    {
        if (fills.Count == 0 || proposal.Quantity <= 0)
        {
            return (0, 0, 0);
        }
        var net = fills.Sum(leg =>
            (leg.Side == "BUY" ? 1m : -1m) * leg.EntryPrice * leg.Quantity) / proposal.Quantity;
        if (fills.Count == 1)
        {
            var leg = fills[0];
            if (leg.OptionRight is not null)
            {
                if (leg.Side == "SELL")
                {
                    var shortRisk = Math.Abs(proposal.StopLoss - leg.EntryPrice) * leg.Quantity;
                    var shortReward = Math.Abs(leg.EntryPrice - proposal.Target) * leg.Quantity;
                    return (shortRisk, shortReward, net);
                }
                return (leg.EntryPrice * leg.Quantity, proposal.MaxReward, net);
            }
            var risk = Math.Abs(leg.EntryPrice - proposal.StopLoss) * leg.Quantity;
            var reward = Math.Abs(proposal.Target - leg.EntryPrice) * leg.Quantity;
            return (risk, reward, net);
        }
        var strikes = fills.Where(leg => leg.Strike is not null).Select(leg => leg.Strike!.Value).ToList();
        if (strikes.Count < 2)
        {
            return (0, 0, net);
        }
        var width = strikes.Max() - strikes.Min();
        var debit = net > 0;
        var premium = Math.Abs(net);
        var payoff = PortfolioRiskManager.VerticalPayoff(width, premium, debit);
        return (payoff.MaxRisk * proposal.Quantity, payoff.MaxReward * proposal.Quantity, net);
    }

    private void RememberDecisionUnsafe(RankedDecisionSnapshot decision)
    {
        _recentDecisions.Add(decision);
        if (_recentDecisions.Count > 100) _recentDecisions.RemoveRange(0, _recentDecisions.Count - 100);
    }

    private void AddMessage(string role, string text)
    {
        lock (_gate) AddMessageUnsafe(role, text);
    }

    private void AddMessageUnsafe(string role, string text)
    {
        _messages.Add(new(role, text, DateTimeOffset.UtcNow));
        if (_messages.Count > 100) _messages.RemoveRange(0, _messages.Count - 100);
        _updatedAt = DateTimeOffset.UtcNow;
    }

    private sealed class Position
    {
        public required string Id { get; init; }
        public required string Strategy { get; init; }
        public required string Underlying { get; init; }
        public required string Direction { get; init; }
        public required string TriggerReason { get; init; }
        public required List<Leg> Legs { get; init; }
        public decimal StopLoss { get; set; }
        public decimal Target { get; init; }
        public decimal MaxRisk { get; init; }
        public decimal MaxReward { get; init; }
        public decimal CapitalRequired { get; init; }
        public decimal PeakPnl { get; set; }
        public string OpeningRegime { get; init; } = "UNKNOWN";
        public DateTimeOffset OpenedAt { get; init; }
        public DateTimeOffset ExitDeadline { get; init; }
        public decimal NetEntry => Legs.Sum(x => (x.Side == "BUY" ? 1m : -1m) * x.EntryPrice);
        public decimal NetMark => Legs.Sum(x => (x.Side == "BUY" ? 1m : -1m) * x.CurrentPrice);

        public static Position FromProposal(
            PaperTradeProposal proposal,
            IReadOnlyList<PaperPositionLegSnapshot> legs,
            DateTimeOffset at,
            string openingRegime) =>
            new()
            {
                Id = proposal.Id,
                Strategy = proposal.Strategy,
                Underlying = proposal.Underlying,
                Direction = proposal.Direction,
                TriggerReason = proposal.Reason,
                Legs = legs.Select(Leg.FromSnapshot).ToList(),
                StopLoss = proposal.StopLoss,
                Target = proposal.Target,
                MaxRisk = proposal.MaxRisk,
                MaxReward = proposal.MaxReward,
                CapitalRequired = proposal.CapitalRequired,
                OpeningRegime = openingRegime,
                OpenedAt = at,
                ExitDeadline = at.AddMinutes(90),
            };

        public static Position FromRecovered(TradeHistoryStore.RecoveredTrade trade) =>
            new()
            {
                Id = trade.Id,
                Strategy = trade.Strategy,
                Underlying = trade.Underlying,
                Direction = trade.Direction,
                TriggerReason = "recovered after restart",
                Legs = trade.Legs.Select(x => new Leg
                {
                    Id = x.Id, Symbol = x.Symbol, Side = x.Side, Quantity = x.Quantity,
                    EntryPrice = x.EntryPrice, CurrentPrice = x.CurrentPrice, Strike = x.Strike,
                    OptionRight = x.OptionRight, Expiry = x.Expiry,
                }).ToList(),
                StopLoss = trade.StopLoss,
                Target = trade.Target,
                MaxRisk = trade.MaxRisk,
                MaxReward = trade.MaxReward,
                CapitalRequired = trade.CapitalRequired,
                OpenedAt = trade.OpenedAt,
                ExitDeadline = trade.OpenedAt.AddMinutes(90),
            };

        public IReadOnlyList<PaperPositionLegSnapshot> LegSnapshots() => Legs.Select(x => x.Snapshot()).ToList();

        public PaperPositionSnapshot Snapshot()
        {
            var first = Legs[0];
            return new(
                Id,
                Legs.Count == 1 ? (first.OptionRight is null ? "STOCK" : "OPTION") : "MULTI_LEG",
                Underlying,
                Direction,
                TriggerReason,
                first.Quantity,
                NetEntry,
                NetMark,
                StopLoss,
                Target,
                PositionPnl(this),
                OpenedAt,
                Strategy,
                Underlying,
                MaxRisk,
                CapitalRequired,
                LegSnapshots(),
                ExitDeadline);
        }
    }

    private sealed class Leg
    {
        public required string Id { get; init; }
        public required string Symbol { get; init; }
        public required string Side { get; init; }
        public int Quantity { get; init; }
        public decimal EntryPrice { get; init; }
        public decimal CurrentPrice { get; set; }
        public decimal? Strike { get; init; }
        public string? OptionRight { get; init; }
        public DateTimeOffset? Expiry { get; init; }

        public static Leg FromSnapshot(PaperPositionLegSnapshot value) => new()
        {
            Id = value.Id, Symbol = value.Symbol, Side = value.Side, Quantity = value.Quantity,
            EntryPrice = value.EntryPrice, CurrentPrice = value.CurrentPrice, Strike = value.Strike,
            OptionRight = value.OptionRight, Expiry = value.Expiry,
        };

        public PaperPositionLegSnapshot Snapshot()
        {
            var pnl = (Side == "BUY" ? CurrentPrice - EntryPrice : EntryPrice - CurrentPrice) * Quantity;
            return new(Id, Symbol, Side, Quantity, EntryPrice, CurrentPrice, pnl, Strike, OptionRight, Expiry);
        }
    }
}
