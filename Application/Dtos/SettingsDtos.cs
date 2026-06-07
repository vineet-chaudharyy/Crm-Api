namespace Crm_Api.Application.Dtos;

// ── Persisted settings model (written to user-settings.json) ─────────────────

public class UserSettings
{
    public GoogleSheetsSettings GoogleSheets { get; set; } = new();
    public MetaSettings         Meta         { get; set; } = new();
}

public class GoogleSheetsSettings
{
    /// <summary>Google Spreadsheet ID from the sheet URL.</summary>
    public string SpreadsheetId { get; set; } = string.Empty;

    /// <summary>
    /// Full contents of the google-credentials.json service-account key file.
    /// Admin pastes the JSON here; the API uses it directly without a file on disk.
    /// </summary>
    public string CredentialsJson { get; set; } = string.Empty;
}

public class MetaSettings
{
    public bool   Enabled         { get; set; } = false;
    public string DatasetId       { get; set; } = string.Empty;
    public string AccessToken     { get; set; } = string.Empty;
    public string ApiVersion      { get; set; } = "v25.0";
    public string LeadEventSource { get; set; } = "DIGIHOOK CRM";
}

// ── API request / response DTOs ───────────────────────────────────────────────

public class SaveGoogleSheetsSettingsRequest
{
    public string SpreadsheetId   { get; set; } = string.Empty;
    public string CredentialsJson { get; set; } = string.Empty;
}

public class SaveMetaSettingsRequest
{
    public bool   Enabled         { get; set; } = true;
    public string DatasetId       { get; set; } = string.Empty;
    public string AccessToken     { get; set; } = string.Empty;
    public string ApiVersion      { get; set; } = "v25.0";
    public string LeadEventSource { get; set; } = "DIGIHOOK CRM";
}

/// <summary>
/// What GET /api/settings returns.
/// AccessToken is masked — never returned in full.
/// CredentialsJson is returned as a boolean (configured yes/no) not the raw JSON.
/// </summary>
public class SettingsResponse
{
    public GoogleSheetsSettingsResponse GoogleSheets { get; set; } = new();
    public MetaSettingsResponse         Meta         { get; set; } = new();
}

public class GoogleSheetsSettingsResponse
{
    public string SpreadsheetId     { get; set; } = string.Empty;
    public bool   CredentialsLinked { get; set; }   // true = credentials JSON is saved
    public string Status            { get; set; } = string.Empty; // "Configured" / "Not configured"
}

public class MetaSettingsResponse
{
    public bool   Enabled         { get; set; }
    public string DatasetId       { get; set; } = string.Empty;
    public string AccessTokenHint { get; set; } = string.Empty; // first 6 chars + "***"
    public string ApiVersion      { get; set; } = "v25.0";
    public string LeadEventSource { get; set; } = "DIGIHOOK CRM";
    public bool   IsConfigured    { get; set; }
}
