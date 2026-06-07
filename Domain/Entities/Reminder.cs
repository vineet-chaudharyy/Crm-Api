namespace Crm_Api.Domain.Entities;

/// <summary>
/// One reminder = one row in the "Reminders" tab of the Google Sheet.
/// Column order (A..H) must match the header written by GoogleSheetsClient.
/// </summary>
public class Reminder
{
    public string ReminderId { get; set; } = string.Empty;     // A  (REM-0001)
    public string LeadId { get; set; } = string.Empty;         // B
    public string LeadName { get; set; } = string.Empty;       // C  (denormalised for quick display)
    public string EmployeeId { get; set; } = string.Empty;     // D
    public string EmployeeName { get; set; } = string.Empty;   // E
    public string ReminderTime { get; set; } = string.Empty;   // F  (yyyy-MM-dd HH:mm)
    public string Notes { get; set; } = string.Empty;          // G
    public string IsCompleted { get; set; } = "FALSE";         // H

    /// <summary>1-based row index in the sheet (not stored).</summary>
    public int RowNumber { get; set; }
}
