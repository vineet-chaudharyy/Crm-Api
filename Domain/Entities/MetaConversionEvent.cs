namespace Crm_Api.Domain.Entities;

/// <summary>
/// One row in the "MetaEvents" Google Sheet tab.
/// Stores every event we attempt to send to Meta Conversions API.
/// Column order A..J must match GoogleSheetsClient.MetaEventHeader.
/// </summary>
public class MetaConversionEvent
{
    public string EventId { get; set; } = string.Empty;        // A  (META-0001)
    public string LeadId { get; set; } = string.Empty;         // B
    public string LeadName { get; set; } = string.Empty;       // C
    public string PreviousStatus { get; set; } = string.Empty; // D
    public string NewStatus { get; set; } = string.Empty;      // E
    public string EventName { get; set; } = string.Empty;      // F  (Lead / Contact / Purchase …)
    public string EventSent { get; set; } = "FALSE";           // G  (TRUE / FALSE)
    public string MetaResponse { get; set; } = string.Empty;   // H  (JSON or error message)
    public string CreatedDate { get; set; } = string.Empty;    // I  (yyyy-MM-dd HH:mm:ss)
    public string RetryCount { get; set; } = "0";              // J

    /// <summary>1-based sheet row — not stored.</summary>
    public int RowNumber { get; set; }
}
