using System.Text.Json;
using Crm_Api.Application.Dtos;
using Crm_Api.Application.Services;
using Crm_Api.Infrastructure.GoogleSheets;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Suppress obsolete warnings for GoogleCredential.FromJson/FromStream.
// The replacement (CredentialFactory) has the same behaviour; these methods
// still work correctly and will not be removed in the near term.
#pragma warning disable CS0618

namespace Crm_Api.Controllers;

/// <summary>
/// Admin-only endpoints for managing Google Sheets and Meta credentials
/// directly from the CRM Settings UI — no server file editing required.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settings;
    private readonly GoogleSheetsOptions _gsOpts;
    private readonly MetaConversionOptions _metaOpts;
    private readonly GoogleSheetsClient _sheetsClient;
    private readonly IHttpClientFactory _httpFactory;

    public SettingsController(
        SettingsService settings,
        IOptions<GoogleSheetsOptions> gsOpts,
        IOptions<MetaConversionOptions> metaOpts,
        GoogleSheetsClient sheetsClient,
        IHttpClientFactory httpFactory)
    {
        _settings     = settings;
        _gsOpts       = gsOpts.Value;
        _metaOpts     = metaOpts.Value;
        _sheetsClient = sheetsClient;
        _httpFactory  = httpFactory;
    }

    // ── GET /api/settings ─────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Get()
    {
        var current            = _settings.Current;
        var effectiveSheetId   = _settings.GetSpreadsheetId(_gsOpts.SpreadsheetId);
        var effectiveDatasetId = _settings.GetMetaDatasetId(_metaOpts.DatasetId);
        var effectiveToken     = _settings.GetMetaAccessToken(_metaOpts.AccessToken);

        return Ok(new SettingsResponse
        {
            GoogleSheets = new GoogleSheetsSettingsResponse
            {
                SpreadsheetId     = effectiveSheetId,
                CredentialsLinked = !string.IsNullOrWhiteSpace(current.GoogleSheets.CredentialsJson),
                Status = !string.IsNullOrWhiteSpace(effectiveSheetId) &&
                         (!string.IsNullOrWhiteSpace(_settings.GetCredentialsJson()) ||
                          System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, _gsOpts.CredentialsPath)))
                    ? "Configured" : "Not configured",
            },
            Meta = new MetaSettingsResponse
            {
                Enabled         = _settings.GetMetaEnabled(_metaOpts.Enabled),
                DatasetId       = effectiveDatasetId,
                AccessTokenHint = MaskToken(effectiveToken),
                ApiVersion      = _settings.GetMetaApiVersion(_metaOpts.ApiVersion),
                LeadEventSource = _settings.GetMetaLeadEventSource(_metaOpts.LeadEventSource),
                IsConfigured    = !string.IsNullOrWhiteSpace(effectiveDatasetId)
                                  && !string.IsNullOrWhiteSpace(effectiveToken)
                                  && IsRealToken(effectiveToken),
            }
        });
    }

    // ── PUT /api/settings/google-sheets ───────────────────────────────────────

    [HttpPut("google-sheets")]
    public async Task<IActionResult> SaveGoogleSheets(
        [FromBody] SaveGoogleSheetsSettingsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SpreadsheetId))
            return BadRequest(new { message = "Spreadsheet ID is required." });

        if (!string.IsNullOrWhiteSpace(req.CredentialsJson))
        {
            var err = ValidateCredentialsJson(req.CredentialsJson);
            if (err is not null) return BadRequest(new { message = err });
        }

        await _settings.SaveGoogleSheetsAsync(req.SpreadsheetId, req.CredentialsJson);
        return Ok(new { message = "Google Sheets settings saved. Reconnecting…" });
    }

    // ── PUT /api/settings/meta ────────────────────────────────────────────────

    [HttpPut("meta")]
    public async Task<IActionResult> SaveMeta(
        [FromBody] SaveMetaSettingsRequest req, CancellationToken ct)
    {
        await _settings.SaveMetaAsync(
            req.Enabled, req.DatasetId, req.AccessToken,
            req.ApiVersion, req.LeadEventSource);

        return Ok(new { message = "Meta settings saved successfully." });
    }

    // ── POST /api/settings/test-google-sheets ─────────────────────────────────

    [HttpPost("test-google-sheets")]
    public async Task<IActionResult> TestGoogleSheets(
        [FromBody] SaveGoogleSheetsSettingsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SpreadsheetId))
            return BadRequest(new { success = false, message = "Spreadsheet ID is required." });

        try
        {
            GoogleCredential credential;

            // 1. Use JSON from the request body (just pasted by admin)
            var credJson = !string.IsNullOrWhiteSpace(req.CredentialsJson)
                ? req.CredentialsJson
                : _settings.GetCredentialsJson();   // 2. already saved in user-settings

            if (!string.IsNullOrWhiteSpace(credJson))
            {
                // Use credentials from UI / user-settings
                credential = GoogleCredential.FromJson(credJson)
                    .CreateScoped(SheetsService.Scope.Spreadsheets);
            }
            else
            {
                // 3. Fall back to file on disk
                var filePath = System.IO.Path.Combine(AppContext.BaseDirectory, _gsOpts.CredentialsPath);
                if (!System.IO.File.Exists(filePath))
                    return Ok(new
                    {
                        success = false,
                        message = "No credentials found. Paste your google-credentials.json content in the field above."
                    });

                await using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                credential = (await GoogleCredential.FromStreamAsync(fs, ct))
                    .CreateScoped(SheetsService.Scope.Spreadsheets);
            }

            var service = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "DIGIHOOK CRM"
            });

            var spreadsheet = await service.Spreadsheets
                .Get(req.SpreadsheetId.Trim())
                .ExecuteAsync(ct);

            return Ok(new
            {
                success    = true,
                message    = $"✓ Connected! Spreadsheet: \"{spreadsheet.Properties.Title}\"",
                sheetTitle = spreadsheet.Properties.Title,
                tabCount   = spreadsheet.Sheets.Count
            });
        }
        catch (Google.GoogleApiException gex)
        {
            var msg = gex.Error?.Code == 403
                ? "Access denied — share the spreadsheet with your service account email (found in credentials JSON as 'client_email')."
                : gex.Error?.Code == 404
                    ? "Spreadsheet not found — double-check the Spreadsheet ID."
                    : $"Google API error: {gex.Message}";
            return Ok(new { success = false, message = msg });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // ── POST /api/settings/test-meta ──────────────────────────────────────────

    [HttpPost("test-meta")]
    public async Task<IActionResult> TestMeta(
        [FromBody] SaveMetaSettingsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DatasetId))
            return BadRequest(new { success = false, message = "Dataset ID is required." });
        if (string.IsNullOrWhiteSpace(req.AccessToken) || !IsRealToken(req.AccessToken))
            return BadRequest(new { success = false, message = "Access Token is required." });

        try
        {
            var version = string.IsNullOrWhiteSpace(req.ApiVersion) ? "v25.0" : req.ApiVersion;
            var url = $"https://graph.facebook.com/{version}/{req.DatasetId}" +
                      $"?access_token={req.AccessToken}&fields=id,name";

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            var response = await http.GetAsync(url, ct);
            var body     = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var name = string.Empty;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                }
                catch { /* ignore */ }

                return Ok(new
                {
                    success = true,
                    message = $"✓ Connected to Meta! Dataset: \"{(string.IsNullOrWhiteSpace(name) ? req.DatasetId : name)}\""
                });
            }

            var errMsg = "Invalid credentials.";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                    errMsg = err.TryGetProperty("message", out var m) ? m.GetString() ?? errMsg : errMsg;
            }
            catch { /* ignore */ }

            return Ok(new { success = false, message = $"Meta API error: {errMsg}" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = $"Connection failed: {ex.Message}" });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Contains("YOUR_ACCESS_TOKEN")) return "";
        if (token.Length <= 8) return "***";
        return token[..6] + "***" + token[^3..];
    }

    private static bool IsRealToken(string token) =>
        !string.IsNullOrWhiteSpace(token) &&
        !token.Contains("YOUR_ACCESS_TOKEN") &&
        token.Length > 10;

    private static string? ValidateCredentialsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out _))
                return "Invalid credentials JSON — missing 'type' field.";
            if (!root.TryGetProperty("client_email", out _))
                return "Invalid credentials JSON — missing 'client_email' field.";
            if (!root.TryGetProperty("private_key", out _))
                return "Invalid credentials JSON — missing 'private_key' field.";
            return null;
        }
        catch
        {
            return "Invalid JSON — paste the exact contents of your google-credentials.json file.";
        }
    }
}

#pragma warning restore CS0618
