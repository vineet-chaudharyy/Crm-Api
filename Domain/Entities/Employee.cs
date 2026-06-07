namespace Crm_Api.Domain.Entities;

/// <summary>
/// One employee = one row in the "Employees" tab. Doubles as the login user
/// store (PasswordHash + Role). Column order A..G.
/// </summary>
public class Employee
{
    public string EmployeeId { get; set; } = string.Empty; // A (e.g. EMP-0003)
    public string FullName { get; set; } = string.Empty;   // B
    public string Email { get; set; } = string.Empty;      // C (login username)
    public string PasswordHash { get; set; } = string.Empty; // D
    public string Role { get; set; } = "Employee";         // E
    public bool Active { get; set; } = true;               // F
    public string CreatedDate { get; set; } = string.Empty; // G

    public int RowNumber { get; set; }
}
