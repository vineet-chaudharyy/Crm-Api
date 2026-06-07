using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RemindersController : ControllerBase
{
    private readonly IReminderRepository _reminders;
    private readonly ILeadRepository _leads;
    private readonly IActivityLogRepository _activity;

    public RemindersController(
        IReminderRepository reminders,
        ILeadRepository leads,
        IActivityLogRepository activity)
    {
        _reminders = reminders;
        _leads = leads;
        _activity = activity;
    }

    private string CurrentUser => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "unknown";
    private string CurrentRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Employee";

    /// <summary>
    /// Get reminders. Admins see all; employees see only their own.
    /// Optional query: ?leadId=LD-0001 or ?employeeId=EMP-0002
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? leadId,
        [FromQuery] string? employeeId,
        CancellationToken ct)
    {
        List<Reminder> list;

        if (!string.IsNullOrWhiteSpace(leadId))
            list = await _reminders.GetByLeadIdAsync(leadId, ct);
        else if (!string.IsNullOrWhiteSpace(employeeId))
            list = await _reminders.GetByEmployeeIdAsync(employeeId, ct);
        else
            list = await _reminders.GetAllAsync(ct);

        // Non-admins only see their own reminders
        if (!CurrentRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            list = list.Where(r => r.EmployeeId.Equals(CurrentUser, StringComparison.OrdinalIgnoreCase)
                                || r.EmployeeName.Equals(CurrentUser, StringComparison.OrdinalIgnoreCase)).ToList();

        return Ok(list.OrderBy(r => r.ReminderTime));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var reminder = await _reminders.GetByIdAsync(id, ct);
        return reminder is null ? NotFound() : Ok(reminder);
    }

    /// <summary>Create a new reminder for a lead.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReminderUpsertDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.LeadId))
            return BadRequest(new { message = "LeadId is required." });
        if (string.IsNullOrWhiteSpace(dto.ReminderTime))
            return BadRequest(new { message = "ReminderTime is required (yyyy-MM-dd HH:mm)." });

        // Fetch the lead name for denormalisation
        var lead = await _leads.GetByIdAsync(dto.LeadId, ct);
        var leadName = lead?.FullName ?? dto.LeadId;

        var reminder = new Reminder
        {
            LeadId = dto.LeadId,
            LeadName = leadName,
            EmployeeId = dto.EmployeeId,
            EmployeeName = dto.EmployeeName,
            ReminderTime = dto.ReminderTime,
            Notes = dto.Notes,
            IsCompleted = "FALSE"
        };

        var created = await _reminders.AddAsync(reminder, ct);

        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser,
            Action = "ReminderSet",
            LeadId = dto.LeadId,
            Details = $"Reminder set for '{leadName}' at {dto.ReminderTime} by {dto.EmployeeName}"
        }, ct);

        return CreatedAtAction(nameof(Get), new { id = created.ReminderId }, created);
    }

    /// <summary>Update a reminder (reschedule / edit notes).</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ReminderUpsertDto dto, CancellationToken ct)
    {
        var existing = await _reminders.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.ReminderTime = dto.ReminderTime;
        existing.Notes = dto.Notes;
        existing.EmployeeId = dto.EmployeeId;
        existing.EmployeeName = dto.EmployeeName;

        var ok = await _reminders.UpdateAsync(existing, ct);
        return ok ? Ok(existing) : StatusCode(500, new { message = "Update failed." });
    }

    /// <summary>Mark a reminder as completed (or undo).</summary>
    [HttpPatch("{id}/complete")]
    public async Task<IActionResult> Complete(string id, [FromBody] CompleteReminderDto dto, CancellationToken ct)
    {
        var existing = await _reminders.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.IsCompleted = dto.IsCompleted ? "TRUE" : "FALSE";
        var ok = await _reminders.UpdateAsync(existing, ct);

        if (ok)
        {
            await _activity.LogAsync(new ActivityLog
            {
                User = CurrentUser,
                Action = dto.IsCompleted ? "ReminderDone" : "ReminderReopened",
                LeadId = existing.LeadId,
                Details = $"Reminder {id} marked {(dto.IsCompleted ? "completed" : "pending")}"
            }, ct);
        }

        return ok ? Ok(existing) : StatusCode(500, new { message = "Update failed." });
    }

    /// <summary>Delete a reminder.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await _reminders.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
