using System.Text.Json;
using Crm_Api.Application.Dtos;

namespace Crm_Api.Application.Services;

/// <summary>
/// Singleton service that persists admin-configured settings to
/// "user-settings.json" in the application base directory.
///
/// Priority (highest first):
///   1. user-settings.json  — written by admin via Settings UI
///   2. appsettings.json    — deployment defaults / fallback
///
/// GoogleSheetsClient and MetaConversionService call this service
/// to get the effective credentials at runtime.
/// </summary>
public class SettingsService
{
    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private UserSettings _current = new();

    // Event fired when settings change — GoogleSheetsClient listens to reset its cache
    public event Action? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "user-settings.json");
        Load();
    }

    /// <summary>Returns the current effective settings (in-memory).</summary>
    public UserSettings Current => _current;

    // ── Load ──────────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _current = JsonSerializer.Deserialize<UserSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new UserSettings();
            _logger.LogInformation("Loaded user-settings.json from {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user-settings.json — using defaults.");
            _current = new UserSettings();
        }
    }

    // ── Save Google Sheets ────────────────────────────────────────────────────

    public async Task SaveGoogleSheetsAsync(string spreadsheetId, string credentialsJson)
    {
        await _lock.WaitAsync();
        try
        {
            _current.GoogleSheets.SpreadsheetId = spreadsheetId.Trim();

            // Only overwrite credentials if a new JSON was provided
            if (!string.IsNullOrWhiteSpace(credentialsJson))
                _current.GoogleSheets.CredentialsJson = credentialsJson.Trim();

            await PersistAsync();
            SettingsChanged?.Invoke();
            _logger.LogInformation("Google Sheets settings saved. SpreadsheetId={Id}", spreadsheetId);
        }
        finally { _lock.Release(); }
    }

    // ── Save Meta ─────────────────────────────────────────────────────────────

    public async Task SaveMetaAsync(
        bool enabled, string datasetId, string accessToken,
        string apiVersion, string leadEventSource)
    {
        await _lock.WaitAsync();
        try
        {
            _current.Meta.Enabled         = enabled;
            _current.Meta.DatasetId       = datasetId.Trim();
            _current.Meta.ApiVersion      = apiVersion.Trim();
            _current.Meta.LeadEventSource = leadEventSource.Trim();

            // Only overwrite token if a new one was provided (not masked placeholder)
            if (!string.IsNullOrWhiteSpace(accessToken) && !accessToken.Contains("***"))
                _current.Meta.AccessToken = accessToken.Trim();

            await PersistAsync();
            SettingsChanged?.Invoke();
            _logger.LogInformation("Meta settings saved. DatasetId={Id}", datasetId);
        }
        finally { _lock.Release(); }
    }

    // ── Effective getters (used by GoogleSheetsClient / MetaConversionService) ─

    /// <summary>Effective SpreadsheetId — user-settings overrides appsettings.</summary>
    public string GetSpreadsheetId(string appsettingsFallback) =>
        string.IsNullOrWhiteSpace(_current.GoogleSheets.SpreadsheetId)
            ? appsettingsFallback
            : _current.GoogleSheets.SpreadsheetId;

    /// <summary>Effective credentials JSON — null means "read from file path".</summary>
    public string? GetCredentialsJson() =>
        string.IsNullOrWhiteSpace(_current.GoogleSheets.CredentialsJson)
            ? null
            : _current.GoogleSheets.CredentialsJson;

    /// <summary>Effective Meta DatasetId.</summary>
    public string GetMetaDatasetId(string fallback) =>
        string.IsNullOrWhiteSpace(_current.Meta.DatasetId) ? fallback : _current.Meta.DatasetId;

    /// <summary>Effective Meta AccessToken.</summary>
    public string GetMetaAccessToken(string fallback) =>
        string.IsNullOrWhiteSpace(_current.Meta.AccessToken) ? fallback : _current.Meta.AccessToken;

    /// <summary>Effective Meta Enabled flag.</summary>
    public bool GetMetaEnabled(bool fallback) =>
        string.IsNullOrWhiteSpace(_current.Meta.DatasetId) ? fallback : _current.Meta.Enabled;

    public string GetMetaApiVersion(string fallback) =>
        string.IsNullOrWhiteSpace(_current.Meta.ApiVersion) ? fallback : _current.Meta.ApiVersion;

    public string GetMetaLeadEventSource(string fallback) =>
        string.IsNullOrWhiteSpace(_current.Meta.LeadEventSource) ? fallback : _current.Meta.LeadEventSource;

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(_filePath, json);
    }
}
