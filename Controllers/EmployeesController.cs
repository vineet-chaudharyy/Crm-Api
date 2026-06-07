using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Crm_Api.Domain.Enums;
using Crm_Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeRepository _employees;
    private readonly IActivityLogRepository _activity;

    public EmployeesController(IEmployeeRepository employees, IActivityLogRepository activity)
    {
        _employees = employees;
        _activity = activity;
    }

    /// <summary>List employees (passwords never returned).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _employees.GetAllAsync(ct);
        return Ok(list.Select(e => new
        {
            e.EmployeeId, e.FullName, e.Email, e.Role, e.Active, e.CreatedDate
        }));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Email and password are required." });

        var existing = await _employees.GetByEmailAsync(req.Email, ct);
        if (existing is not null) return Conflict(new { message = "Email already exists." });

        var role = req.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            ? UserRoles.Admin : UserRoles.Employee;

        var created = await _employees.AddAsync(new Employee
        {
            FullName = req.FullName,
            Email = req.Email,
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = role,
            Active = true
        }, ct);

        await _activity.LogAsync(new ActivityLog
        {
            User = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "admin",
            Action = "EmployeeAdded",
            Details = $"{created.FullName} ({role})"
        }, ct);

        return Ok(new { created.EmployeeId, created.FullName, created.Email, created.Role });
    }

    /// <summary>
    /// Update an existing employee's details. Admin only.
    /// Pass NewPassword only if you want to change the password — leave empty/null to keep existing.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateEmployeeRequest req, CancellationToken ct)
    {
        var existing = await _employees.GetByIdAsync(id, ct);
        if (existing is null) return NotFound(new { message = $"Employee '{id}' not found." });

        var role = req.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            ? UserRoles.Admin : UserRoles.Employee;

        // Only re-hash if a new password was actually provided
        var passwordHash = string.IsNullOrWhiteSpace(req.NewPassword)
            ? existing.PasswordHash
            : PasswordHasher.Hash(req.NewPassword);

        existing.FullName = req.FullName;
        existing.Role = role;
        existing.Active = req.Active;
        existing.PasswordHash = passwordHash;

        var ok = await _employees.UpdateAsync(existing, ct);
        if (!ok) return StatusCode(500, new { message = "Update failed." });

        var adminEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "admin";
        var pwNote = string.IsNullOrWhiteSpace(req.NewPassword) ? "" : " (password changed)";
        await _activity.LogAsync(new ActivityLog
        {
            User = adminEmail,
            Action = "EmployeeUpdated",
            Details = $"{existing.FullName} ({role}) updated{pwNote}"
        }, ct);

        return Ok(new { existing.EmployeeId, existing.FullName, existing.Email, existing.Role, existing.Active });
    }
}
