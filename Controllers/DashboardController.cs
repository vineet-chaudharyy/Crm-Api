using Crm_Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboard;
    public DashboardController(DashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct) =>
        Ok(await _dashboard.GetStatsAsync(ct));
}

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly DashboardService _dashboard;
    public ReportsController(DashboardService dashboard) => _dashboard = dashboard;

    /// <summary>Employee performance report (Admin + Manager view).</summary>
    [HttpGet("employee-performance")]
    public async Task<IActionResult> EmployeePerformance(CancellationToken ct) =>
        Ok(await _dashboard.GetEmployeePerformanceAsync(ct));
}
