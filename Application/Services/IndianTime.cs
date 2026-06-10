namespace Crm_Api.Application.Services;

/// <summary>
/// Helper for getting current date/time in India Standard Time (UTC+5:30),
/// regardless of server timezone (Azure servers run in UTC).
/// </summary>
public static class IndianTime
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    /// <summary>Current date/time in IST.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);

    /// <summary>Current date in IST (time = 00:00).</summary>
    public static DateTime Today => Now.Date;

    /// <summary>"yyyy-MM-dd HH:mm:ss" in IST.</summary>
    public static string NowString() => Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>"yyyy-MM-dd" in IST.</summary>
    public static string TodayString() => Now.ToString("yyyy-MM-dd");
}
