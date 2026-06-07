namespace Crm_Api.Domain.Entities;

/// <summary>One row in the "Activity" tab — used for activity feed + audit trail.</summary>
public class ActivityLog
{
    public string Timestamp { get; set; } = string.Empty; // A
    public string User { get; set; } = string.Empty;      // B
    public string Action { get; set; } = string.Empty;    // C  (Created / Updated / Deleted / Login ...)
    public string LeadId { get; set; } = string.Empty;    // D
    public string Details { get; set; } = string.Empty;   // E
}
