using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Central live-order safety policies. Cancellation and risk-reduction exits are not blocked by the emergency gate.</summary>
public sealed class LiveTradingExecutionSafetyService
{
    public const string EmergencyConfirmPhrase = "I AUTHORIZE EMERGENCY GATE CHANGE";

    private readonly LiveTradingEmergencyGate _emergency;
    private readonly BrokerCircuitBreaker _breaker;
    private readonly TradingExecutionSafetyOptions _options;
    private readonly TradingSafetyStore _store;
    private readonly ILogger<LiveTradingExecutionSafetyService> _log;

    public LiveTradingExecutionSafetyService(
        LiveTradingEmergencyGate emergency,
        BrokerCircuitBreaker breaker,
        TradingExecutionSafetyOptions options,
        TradingSafetyStore store,
        ILogger<LiveTradingExecutionSafetyService> log)
    {
        _emergency = emergency;
        _breaker = breaker;
        _options = options;
        _store = store;
        _log = log;
        RestorePersistedGate();
    }

    public LiveTradingEmergencyGateSnapshot GateSnapshot()
    {
        var persisted = _store.GetEmergencyGateState();
        return new LiveTradingEmergencyGateSnapshot(
            _emergency.IsHalted,
            _emergency.Reason,
            persisted?.ChangedUtc,
            persisted?.ChangedBy);
    }

    public (bool Allowed, string Message) AssertPolicy(LiveOrderSafetyPolicy policy)
    {
        return policy switch
        {
            LiveOrderSafetyPolicy.Cancellation or LiveOrderSafetyPolicy.StatusInquiry => (true, ""),
            LiveOrderSafetyPolicy.RiskReductionExit => (true, ""),
            LiveOrderSafetyPolicy.Modification => AssertBlockedForHaltAndBreaker("Order modification"),
            LiveOrderSafetyPolicy.NewPlacement => AssertBlockedForHaltAndBreaker("New live order placement"),
            _ => (false, "Unsupported safety policy."),
        };
    }

    public void Halt(string reason, string actor)
    {
        _emergency.Halt(reason);
        PersistGate(actor);
        _store.AppendGateAudit("HALT", reason, actor);
        _log.LogWarning("Live emergency gate HALTED by {Actor}: {Reason}", actor, reason);
    }

    public void Clear(string actor, string reason)
    {
        _emergency.Clear();
        PersistGate(actor);
        _store.AppendGateAudit("CLEAR", reason, actor);
        _log.LogInformation("Live emergency gate cleared by {Actor}: {Reason}", actor, reason);
    }

    private (bool Allowed, string Message) AssertBlockedForHaltAndBreaker(string action)
    {
        if (_emergency.IsHalted)
        {
            return (false, $"{action} blocked: emergency gate active — {_emergency.Reason}");
        }

        var snap = _breaker.Snapshot();
        if (snap.LiveOrdersBlocked || snap.State.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"LIVE ORDERS BLOCKED: {snap.TripReason ?? "circuit breaker open"}.");
        }

        var breaker = _breaker.AssertLiveOrderAllowed();
        if (!breaker.Allowed)
        {
            return (false, breaker.Message);
        }

        return (true, "");
    }

    private void RestorePersistedGate()
    {
        if (!_options.PersistEmergencyGateState)
        {
            return;
        }

        try
        {
            var state = _store.GetEmergencyGateState();
            if (state is null)
            {
                return;
            }

            if (state.IsHalted)
            {
                _emergency.Halt(state.Reason);
            }
            else
            {
                _emergency.Clear();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to restore emergency gate state.");
            if (_options.FailSafeWhenGateUnavailable)
            {
                _emergency.Halt("Fail-safe: could not load emergency gate state.");
            }
        }
    }

    private void PersistGate(string actor)
    {
        if (!_options.PersistEmergencyGateState)
        {
            return;
        }

        _store.SaveEmergencyGateState(_emergency.IsHalted, _emergency.Reason, actor);
    }
}