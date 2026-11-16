using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin/incidents")]
[AdminAuthorize]
public sealed class SystemIncidentController : ControllerBase
{
    private readonly SystemIncidentService _incidents;
    private readonly ICurrentUserAccessor _user;

    public SystemIncidentController(SystemIncidentService incidents, ICurrentUserAccessor user)
    {
        _incidents = incidents;
        _user = user;
    }

    [HttpGet]
    public IActionResult List(
        [FromQuery] SystemIncidentStatus? status,
        [FromQuery] SystemIncidentSeverity? minSeverity,
        [FromQuery] string? component,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) =>
        Ok(_incidents.List(new SystemIncidentListQuery(status, minSeverity, component, fromUtc, toUtc, limit, offset)));

    [HttpGet("active")]
    public IActionResult Active([FromQuery] int limit = 100) => Ok(_incidents.ListActive(limit));

    [HttpGet("statistics")]
    public IActionResult Statistics([FromQuery] DateTimeOffset? fromUtc, [FromQuery] DateTimeOffset? toUtc) =>
        Ok(_incidents.Statistics(fromUtc, toUtc));

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var incident = _incidents.GetById(id);
        return incident is null ? NotFound() : Ok(Sanitize(incident));
    }

    [HttpGet("{id}/events")]
    public IActionResult Events(string id, [FromQuery] int limit = 200)
    {
        if (_incidents.GetById(id) is null)
        {
            return NotFound();
        }

        return Ok(_incidents.ListEvents(id, limit));
    }

    [HttpGet("{id}/notes")]
    public IActionResult Notes(string id, [FromQuery] int limit = 200)
    {
        if (_incidents.GetById(id) is null)
        {
            return NotFound();
        }

        return Ok(_incidents.ListNotes(id, limit));
    }

    [HttpPost("{id}/acknowledge")]
    public IActionResult Acknowledge(string id) =>
        Execute(id, adminId => Ok(Sanitize(_incidents.Acknowledge(id, adminId))));

    [HttpPost("{id}/investigate")]
    public IActionResult Investigate(string id) =>
        Execute(id, adminId => Ok(Sanitize(_incidents.Investigate(id, adminId))));

    [HttpPost("{id}/resolve")]
    public IActionResult Resolve(string id, [FromBody] IncidentActionRequest? body) =>
        Execute(id, adminId => Ok(Sanitize(_incidents.Resolve(id, adminId, body?.Message))));

    [HttpPost("{id}/close")]
    public IActionResult Close(string id, [FromBody] IncidentActionRequest? body) =>
        Execute(id, adminId => Ok(Sanitize(_incidents.Close(id, adminId, body?.Message))));

    [HttpPost("{id}/notes")]
    public IActionResult AddNote(string id, [FromBody] IncidentNoteRequest body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Note))
        {
            return BadRequest(new { message = "Note is required." });
        }

        if (_incidents.GetById(id) is null)
        {
            return NotFound();
        }

        var adminId = RequireAdminId();
        return Ok(_incidents.AddNote(id, body.Note.Trim(), adminId));
    }

    [HttpGet("~/api/admin/alerts")]
    public IActionResult Alerts(
        [FromQuery] SystemAlertStatus? status,
        [FromQuery] SystemIncidentSeverity? minSeverity,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) =>
        Ok(_incidents.ListAlerts(new SystemAlertListQuery(status, minSeverity, fromUtc, toUtc, limit, offset)));

    [HttpGet("~/api/admin/alerts/{id}")]
    public IActionResult Alert(string id)
    {
        var alert = _incidents.GetAlert(id);
        return alert is null ? NotFound() : Ok(alert);
    }

    [HttpPost("~/api/admin/alerts/{id}/acknowledge")]
    public IActionResult AcknowledgeAlert(string id)
    {
        try
        {
            var adminId = RequireAdminId();
            return Ok(_incidents.AcknowledgeAlert(id, adminId));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private IActionResult Execute(string id, Func<string, IActionResult> action)
    {
        if (_incidents.GetById(id) is null)
        {
            return NotFound();
        }

        try
        {
            return action(RequireAdminId());
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private string RequireAdminId()
    {
        if (string.IsNullOrWhiteSpace(_user.UserId))
        {
            throw new UnauthorizedAccessException();
        }

        return _user.UserId;
    }

    private static SystemIncidentRecord Sanitize(SystemIncidentRecord record) => record;

    public sealed class IncidentActionRequest
    {
        public string? Message { get; set; }
    }

    public sealed class IncidentNoteRequest
    {
        public string Note { get; set; } = "";
    }
}