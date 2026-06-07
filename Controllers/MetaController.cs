using Crm_Api.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

/// <summary>
/// Admin-only endpoints for Meta Conversions API management.
/// GET  /api/meta/stats    — dashboard stats card
/// GET  /api/meta/events   — full event log
/// POST /api/meta/retry/{id} — retry a failed event
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
public class MetaController : ControllerBase
{
    private readonly IMetaConversionService _meta;

    public MetaController(IMetaConversionService meta) => _meta = meta;

    /// <summary>Returns total/successful/failed event counts + last sync time.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct) =>
        Ok(await _meta.GetStatsAsync(ct));

    /// <summary>Returns the full event log (newest first).</summary>
    [HttpGet("events")]
    public async Task<IActionResult> Events(CancellationToken ct) =>
        Ok(await _meta.GetEventsAsync(ct));

    /// <summary>Retry a specific failed event by EventId.</summary>
    [HttpPost("retry/{eventId}")]
    public async Task<IActionResult> Retry(string eventId, CancellationToken ct)
    {
        var ok = await _meta.RetryEventAsync(eventId, ct);
        return ok
            ? Ok(new { message = $"Event {eventId} retried successfully." })
            : BadRequest(new { message = $"Retry failed for event {eventId}." });
    }
}
