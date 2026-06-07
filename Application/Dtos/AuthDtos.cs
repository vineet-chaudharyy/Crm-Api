namespace Crm_Api.Application.Dtos;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class CreateEmployeeRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
}

/// <summary>
/// Body for POST /api/auth/setup — creates the very first Admin account.
/// Only accepted when NO admin exists in the system.
/// </summary>
public class SetupRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
}

/// <summary>Body for PUT /api/employees/{id} — admin can update name, role, active status, and optionally change password.</summary>
public class UpdateEmployeeRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
    public bool Active { get; set; } = true;
    /// <summary>Leave empty to keep the existing password unchanged.</summary>
    public string? NewPassword { get; set; }
}
