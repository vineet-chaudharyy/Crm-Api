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

    public AttendanceController(IAttendanceRepository attendance)
    {
        _attendance = attendance;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var all = await _attendance.GetAllAsync(ct);
        return Ok(all.OrderByDescending(a => a.Date).ThenBy(a => a.EmployeeName));
    }
}
