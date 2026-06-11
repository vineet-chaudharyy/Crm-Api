namespace Crm_Api.Domain.Entities;

/// <summary>
/// One row = one employee's attendance for one day, in the "Attendance" tab.
/// Column order (A..F) must match the header written by GoogleSheetsClient.
/// </summary>
public class Attendance
{
    public string Date { get; set; } = string.Empty;         // A  (yyyy-MM-dd, IST)
    public string EmployeeId { get; set; } = string.Empty;    // B
    public string EmployeeName { get; set; } = string.Empty;  // C
    public string LoginTime { get; set; } = string.Empty;     // D  (HH:mm:ss, IST)
    public string LogoutTime { get; set; } = string.Empty;    // E  (HH:mm:ss, IST)
    public string TotalHours { get; set; } = string.Empty;    // F  (e.g. "7h 32m")

    /// <summary>1-based row index in the sheet (not stored).</summary>
    public int RowNumber { get; set; }
}
