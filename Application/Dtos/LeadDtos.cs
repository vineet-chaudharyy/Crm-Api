namespace Crm_Api.Application.Dtos;

/// <summary>Fields a client can set when creating/updating a lead. LeadId,
/// DateAdded and LastUpdated are managed by the server.</summary>
public class LeadUpsertDto
{
    public string FullName { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string LeadSource { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public string FollowUpDate { get; set; } = string.Empty;
    public string AssignedEmployee { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Temperature { get; set; } = string.Empty; // Hot / Warm / Cold
    public string Budget { get; set; } = string.Empty;      // e.g. "80L", "1.2 Cr"
    public string ProjectName { get; set; } = string.Empty; // property / project of interest
}

/// <summary>Body for POST /api/leads/{id}/interaction — log a call/message/note on the lead timeline.</summary>
public class InteractionDto
{
    public string Type { get; set; } = "Note";     // Call / WhatsApp / Note
    public string Outcome { get; set; } = string.Empty; // optional free-text result
}

/// <summary>Body for PUT /api/leads/{id}/transfer — move lead to another employee.</summary>
public class TransferLeadDto
{
    public string ToEmployeeId { get; set; } = string.Empty;
    public string ToEmployeeName { get; set; } = string.Empty;
    public string ToEmployeeEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Query parameters for searching/filtering leads.</summary>
public class LeadFilterDto
{
    public string? Search { get; set; }          // matches name/email/mobile/company
    public string? Status { get; set; }
    public string? AssignedEmployee { get; set; }
    public string? FromDate { get; set; }        // yyyy-MM-dd (DateAdded >=)
    public string? ToDate { get; set; }          // yyyy-MM-dd (DateAdded <=)
    public bool? Unassigned { get; set; }        // true → only leads with no AssignedEmployee
}
