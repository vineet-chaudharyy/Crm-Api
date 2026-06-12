using Crm_Api.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceRepository _attendance;
    private readonly IEmployeeRepository _employees;

    public AttendanceController(IAttendanceRepository attendance, IEmployeeRepository employees)
    {
        _attendance = attendance;
        _employees = employees;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var all = await _attendance.GetAllAsync(ct);
        var employees = await _employees.GetAllAsync(ct);
        var adminIds = employees
            .Where(e => e.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.EmployeeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = all.Where(a => !adminIds.Contains(a.EmployeeId));
        return Ok(filtered.OrderByDescending(a => a.Date).ThenBy(a => a.EmployeeName));
    }
}
