namespace Crm_Api.Application.Dtos;

// ── Dashboard stats ──────────────────────────────────────────────────────────

public class MetaStatsDto
{
    public int TotalEventsSent { get; set; }
    public int SuccessfulEvents { get; set; }
    public int FailedEvents { get; set; }
    public string LastSyncTime { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }         // true when DatasetId + AccessToken are set
}

// ── Event log item returned by GET /api/meta/events ─────────────────────────

public class MetaEventDto
{
    public string EventId { get; set; } = string.Empty;
    public string LeadId { get; set; } = string.Empty;
    public string LeadName { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public bool EventSent { get; set; }
    public string MetaResponse { get; set; } = string.Empty;
    public string CreatedDate { get; set; } = string.Empty;
    public int RetryCount { get; set; }
}

// ── Internal payload sent to Meta Conversions API ───────────────────────────
// (not exposed through the public API — used internally by MetaConversionService)

public class MetaConversionOptions
{
    public const string SectionName = "MetaConversions";
    public string DatasetId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v25.0";
    public string LeadEventSource { get; set; } = "DIGIHOOK CRM";
    public bool Enabled { get; set; } = true;
}
