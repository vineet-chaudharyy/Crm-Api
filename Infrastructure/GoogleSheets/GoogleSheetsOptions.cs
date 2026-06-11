namespace Crm_Api.Infrastructure.GoogleSheets;

public class GoogleSheetsOptions
{
    public const string SectionName = "GoogleSheets";

    /// <summary>
    /// Default / fallback spreadsheet ID used when no per-entity ID is set.
    /// All tabs live here unless overridden below.
    /// </summary>
    public string SpreadsheetId { get; set; } = string.Empty;

    /// <summary>Path to the service-account JSON key (shared across all sheets).</summary>
    public string CredentialsPath { get; set; } = "google-credentials.json";

    /// <summary>
    /// Full JSON content of the service-account key.
    /// Set this via Azure App Settings as GoogleSheets__CredentialsJson
    /// (double underscore). Takes priority over CredentialsPath.
    /// </summary>
    public string? CredentialsJson { get; set; }

    // ── Tab names ────────────────────────────────────────────────────────────
    public string LeadsTab { get; set; } = "Leads";
    public string EmployeesTab { get; set; } = "Employees";
    public string ActivityTab { get; set; } = "Activity";
    public string RemindersTab { get; set; } = "Reminders";
    public string MetaEventsTab { get; set; } = "MetaEvents";
    public string AttendanceTab { get; set; } = "Attendance";

    // ── Per-entity optional spreadsheet IDs ──────────────────────────────────
    // Leave empty (or omit from appsettings) to use the default SpreadsheetId above.
    // Set to a different Spreadsheet ID to store that data in a separate Google Sheet.

    /// <summary>Spreadsheet that holds the Leads tab. Falls back to SpreadsheetId.</summary>
    public string? LeadsSpreadsheetId { get; set; }

    /// <summary>Spreadsheet that holds the Employees tab. Falls back to SpreadsheetId.</summary>
    public string? EmployeesSpreadsheetId { get; set; }

    /// <summary>Spreadsheet that holds the Activity tab. Falls back to SpreadsheetId.</summary>
    public string? ActivitySpreadsheetId { get; set; }

    /// <summary>Spreadsheet that holds the Reminders tab. Falls back to SpreadsheetId.</summary>
    public string? RemindersSpreadsheetId { get; set; }

    /// <summary>Spreadsheet that holds the Attendance tab. Falls back to SpreadsheetId.</summary>
    public string? AttendanceSpreadsheetId { get; set; }

    // ── Helper — resolves the effective spreadsheet ID for each entity ────────
    public string? MetaEventsSpreadsheetId { get; set; }

    public string Leads => Resolve(LeadsSpreadsheetId);
    public string Employees => Resolve(EmployeesSpreadsheetId);
    public string Activity => Resolve(ActivitySpreadsheetId);
    public string Reminders => Resolve(RemindersSpreadsheetId);
    public string MetaEvents => Resolve(MetaEventsSpreadsheetId);

    private string Resolve(string? specific) =>
        string.IsNullOrWhiteSpace(specific) ? SpreadsheetId : specific;
}
