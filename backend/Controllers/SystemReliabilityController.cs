using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin/system")]
[AdminAuthorize]
public sealed class SystemReliabilityController : ControllerBase
{
    private readonly SystemInstanceRegistryService _instances;
    private readonly IDistributedLockService _locks;
    private readonly SystemLeaderElectionService _leaders;
    private readonly SystemCoordinationStore _store;
    private readonly SystemRecoveryService _recovery;
    private readonly SystemRecoveryPolicyService _policies;

    public SystemReliabilityController(
        SystemInstanceRegistryService instances,
        IDistributedLockService locks,
        SystemLeaderElectionService leaders,
        SystemCoordinationStore store,
        SystemRecoveryService recovery,
        SystemRecoveryPolicyService policies)
    {
        _instances = instances;
        _locks = locks;
        _leaders = leaders;
        _store = store;
        _recovery = recovery;
        _policies = policies;
    }

    [HttpGet("instances")]
    public IActionResult ListInstances() => Ok(_instances.List());

    [HttpGet("instances/{id}")]
    public IActionResult GetInstance(string id)
    {
        var instance = _instances.Get(id);
        return instance is null ? NotFound() : Ok(instance);
    }

    [HttpPost("instances/{id}/drain")]
    public IActionResult DrainInstance(string id)
    {
        if (_instances.Get(id) is null)
        {
            return NotFound();
        }

        if (id != _instances.InstanceId)
        {
            _store.SetInstanceDraining(id, true);
            return Ok(new { status = "draining", instanceId = id });
        }

        _instances.Drain();
        return Ok(new { status = "draining", instanceId = id });
    }

    [HttpGet("workers")]
    public IActionResult ListWorkers([FromQuery] string? instanceId) => Ok(_store.ListWorkers(instanceId));

    [HttpGet("workers/{id}")]
    public IActionResult GetWorker(string id)
    {
        var worker = _store.GetWorker(id);
        return worker is null ? NotFound() : Ok(worker);
    }

    [HttpGet("locks")]
    public IActionResult ListLocks() => Ok(_locks.ListActive());

    [HttpGet("leader")]
    public IActionResult Leader([FromQuery] string role = SystemLeaderRoles.NotificationCoordinator) =>
        Ok(_leaders.Current(role));

    [HttpGet("recovery/history")]
    public IActionResult RecoveryHistory([FromQuery] int limit = 100) => Ok(_recovery.History(limit));

    [HttpGet("recovery/policies")]
    public IActionResult RecoveryPolicies() => Ok(_policies.List());

    [HttpPut("recovery/policies")]
    public IActionResult PutRecoveryPolicies([FromBody] List<SystemRecoveryPolicyRecord>? body)
    {
        if (body is null || body.Count == 0)
        {
            return BadRequest(new { message = "At least one policy is required." });
        }

        _policies.Replace(body);
        return Ok(_policies.List());
    }

    [HttpPost("workers/{id}/recovery")]
    public IActionResult RecoverWorker(string id, [FromBody] ManualRecoveryRequest? body)
    {
        if (_store.GetWorker(id) is null)
        {
            return NotFound();
        }

        try
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var history = _recovery.TryManualRecovery(id, body?.Reason, correlationId);
            return Ok(history);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("workers/{id}/disable-recovery")]
    public IActionResult DisableRecovery(string id)
    {
        try
        {
            _recovery.SetRecoveryEnabled(id, false);
            return Ok();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("workers/{id}/enable-recovery")]
    public IActionResult EnableRecovery(string id)
    {
        try
        {
            _recovery.SetRecoveryEnabled(id, true);
            return Ok();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}