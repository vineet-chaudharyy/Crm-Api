namespace Crm_Api.Application.Dtos;

/// <summary>Body for creating or updating a reminder.</summary>
public class ReminderUpsertDto
{
    public string LeadId { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    /// <summary>ISO datetime string: "yyyy-MM-dd HH:mm"</summary>
    public string ReminderTime { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Body for PATCH /api/reminders/{id}/complete</summary>
public class CompleteReminderDto
{
    public bool IsCompleted { get; set; } = true;
}
