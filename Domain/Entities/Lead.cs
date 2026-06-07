namespace Crm_Api.Domain.Entities;

/// <summary>
/// One lead = one row in the "Leads" tab of the Google Sheet.
/// Column order (A..N) MUST match the header row written by GoogleSheetsClient.
/// </summary>
public class Lead
{
    public string LeadId { get; set; } = string.Empty;   // A  (auto-generated, e.g. LD-0007)
    public string DateAdded { get; set; } = string.Empty; // B
    public string FullName { get; set; } = string.Empty;  // C
    public string MobileNumber { get; set; } = string.Empty; // D
    public string EmailAddress { get; set; } = string.Empty; // E
    public string City { get; set; } = string.Empty;      // F
    public string State { get; set; } = string.Empty;     // G
    public string CompanyName { get; set; } = string.Empty; // H
    public string LeadSource { get; set; } = string.Empty; // I
    public string Status { get; set; } = "New";           // J
    public string FollowUpDate { get; set; } = string.Empty; // K
    public string AssignedEmployee { get; set; } = string.Empty; // L
    public string Notes { get; set; } = string.Empty;     // M
    public string LastUpdated { get; set; } = string.Empty; // N

    /// <summary>1-based row index in the sheet (not stored, used for update/delete).</summary>
    public int RowNumber { get; set; }
}
