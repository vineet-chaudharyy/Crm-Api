namespace Crm_Api.Infrastructure.Auth;

/// <summary>
/// DataSeeder is intentionally empty.
/// The first Admin account is now created through the
/// First-Time Setup wizard at POST /api/auth/setup.
/// No credentials are ever hardcoded or auto-seeded.
/// </summary>
public class DataSeeder
{
    // Kept as a no-op class so Program.cs compiles without changes to its DI registration.
    public Task SeedAsync(CancellationToken ct = default) => Task.CompletedTask;
}
