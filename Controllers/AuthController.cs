using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Domain.Entities;
using Crm_Api.Domain.Enums;
using Crm_Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IEmployeeRepository _employees;
    private readonly IJwtTokenService _jwt;
    private readonly IActivityLogRepository _activity;
    private readonly IAttendanceRepository _attendance;

    public AuthController(
        IEmployeeRepository employees,
        IJwtTokenService jwt,
        IActivityLogRepository activity,
        IAttendanceRepository attendance)
    {
        _employees = employees;
        _jwt = jwt;
        _activity = activity;
        _attendance = attendance;
    }

    // ── Setup Status ─────────────────────────────────────────────────────────

    /// <summary>
    /// Public endpoint — returns whether an Admin account already exists.
    /// The React app calls this on first load to decide whether to show
    /// the First-Time Setup wizard or the normal Login page.
    /// </summary>
    [HttpGet("setup-status")]
    public async Task<IActionResult> SetupStatus(CancellationToken ct)
    {
        try
        {
            var all = await _employees.GetAllAsync(ct);
            var adminExists = all.Any(e =>
                e.Role.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) && e.Active);

            return Ok(new { adminExists });
        }
        catch
        {
            // Sheet not reachable yet — treat as "not set up"
            return Ok(new { adminExists = false });
        }
    }

    // ── First-Time Setup ──────────────────────────────────────────────────────

    /// <summary>
    /// Create the very first Admin account.
    /// This endpoint is ONLY usable when no Admin exists.
    /// Once an Admin is created it returns 409 for all subsequent calls,
    /// so it cannot be abused to create extra admins anonymously.
    /// On success it returns a JWT so the admin is automatically signed in.
    /// </summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest req, CancellationToken ct)
    {
        // Guard — refuse if an admin already exists
        var all = await _employees.GetAllAsync(ct);
        if (all.Any(e => e.Role.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase) && e.Active))
            return Conflict(new { message = "Setup already completed. An Admin account exists." });

        // Validate inputs
        if (string.IsNullOrWhiteSpace(req.FullName))
            return BadRequest(new { message = "Full name is required." });
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });
        if (req.Password != req.ConfirmPassword)
            return BadRequest(new { message = "Passwords do not match." });

        // Create admin
        var admin = await _employees.AddAsync(new Employee
        {
            FullName = req.FullName.Trim(),
            Email = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = UserRoles.Admin,
            Active = true,
        }, ct);

        // Log the setup event
        await _activity.LogAsync(new ActivityLog
        {
            User = admin.Email,
            Action = "SetupCompleted",
            Details = $"First Admin '{admin.FullName}' created via Setup wizard" +
                      (string.IsNullOrWhiteSpace(req.OrganisationName)
                          ? ""
                          : $" | Org: {req.OrganisationName}")
        }, ct);

        // Auto-login: issue a JWT immediately
        var (token, expires) = _jwt.CreateToken(admin);

        return Ok(new
        {
            message = "Setup complete. Admin account created.",
            token,
            expiresAt = expires,
            fullName = admin.FullName,
            email = admin.Email,
            role = admin.Role,
        });
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _employees.GetByEmailAsync(req.Email, ct);
        if (user is null || !user.Active || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        var (token, expires) = _jwt.CreateToken(user);

        await _activity.LogAsync(new ActivityLog
        {
            User = user.Email,
            Action = "Login",
            Details = $"{user.FullName} signed in"
        }, ct);

        // Record today's login time (only the FIRST login of the day is kept)
        var today = IndianTime.TodayString();
        var existingAttendance = await _attendance.GetTodayAsync(user.EmployeeId, today, ct);
        if (existingAttendance is null)
        {
            await _attendance.AddAsync(new Attendance
            {
                Date = today,
                EmployeeId = user.EmployeeId,
                EmployeeName = user.FullName,
                LoginTime = IndianTime.Now.ToString("HH:mm:ss"),
            }, ct);
        }

        return Ok(new AuthResponse
        {
            Token = token,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            ExpiresAt = expires
        });
    }

    // ── Whoami ────────────────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        name = User.Identity?.Name,
        email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
        role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
    });

    // ── Logout ────────────────────────────────────────────────────────────────

    /// <summary>Records the logout time and computes total hours worked today.</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var employeeId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

        var today = IndianTime.TodayString();
        var record = await _attendance.GetTodayAsync(employeeId, today, ct);
        if (record is not null && string.IsNullOrWhiteSpace(record.LogoutTime))
        {
            var now = IndianTime.Now;
            record.LogoutTime = now.ToString("HH:mm:ss");

            if (TimeSpan.TryParse(record.LoginTime, out var loginTs))
            {
                var worked = now.TimeOfDay - loginTs;
                if (worked < TimeSpan.Zero) worked = TimeSpan.Zero;
                record.TotalHours = $"{(int)worked.TotalHours}h {worked.Minutes}m";
            }

            await _attendance.UpdateAsync(record, ct);
        }

        await _activity.LogAsync(new ActivityLog
        {
            User = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
            Action = "Logout",
            Details = $"{User.Identity?.Name} signed out"
        }, ct);

        return Ok(new { message = "Logged out." });
    }
}
