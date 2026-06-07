namespace Crm_Api.Domain.Enums;

/// <summary>Role names used for JWT claims and [Authorize(Roles=...)].</summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";
}
