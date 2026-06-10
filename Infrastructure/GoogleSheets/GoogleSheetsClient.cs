using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

// Suppress obsolete warnings for GoogleCredential.FromJson/FromStream.
// These methods still work correctly; replacement (CredentialFactory) has
// the same behaviour and we suppress only here to keep build output clean.
#pragma warning disable CS0618

namespace Crm_Api.Infrastructure.GoogleSheets;

/// <summary>
/// Thin wrapper over the Google Sheets API.
///
/// BUG FIX: Bootstrap cache key is now "spreadsheetId::tab1|tab2" instead of
/// just "spreadsheetId". Previously all tabs shared the same spreadsheetId so
/// once Leads bootstrapped, MetaEvents / Reminders etc. were silently skipped.
/// </summary>
public class GoogleSheetsClient
{
    // ── Header row definitions ───────────────────────────────────────────────
    public static readonly string[] LeadHeader =
    {
        "Lead ID", "Date Added", "Full Name", "Mobile Number", "Email Address",
        "City", "State", "Company Name", "Lead Source", "Status",
        "Follow Up Date", "Assigned Employee", "Notes", "Last Updated"
    };

    public static readonly string[] EmployeeHeader =
    {
        "Employee ID", "Full Name", "Email", "Password Hash", "Role", "Active", "Created Date"
    };

    public static readonly string[] ActivityHeader =
    {
        "Timestamp", "User", "Action", "Lead ID", "Details"
    };

    public static readonly string[] ReminderHeader =
    {
        "Reminder ID", "Lead ID", "Lead Name", "Employee ID", "Employee Name",
        "Reminder Time", "Notes", "Is Completed"
    };

    public static readonly string[] MetaEventHeader =
    {
        "Event ID", "Lead ID", "Lead Name", "Previous Status", "New Status",
        "Event Name", "Event Sent", "Meta Response", "Created Date", "Retry Count"
    };

    // ── Internal state ───────────────────────────────────────────────────────
    private readonly GoogleSheetsOptions _opt;
    private readonly ILogger<GoogleSheetsClient> _logger;
    private readonly Application.Services.SettingsService _settingsService;
    private readonly SemaphoreSlim _authGate = new(1, 1);
    private SheetsService? _service;

    /// <summary>
    /// Bootstrap cache keyed by "spreadsheetId::tab1|tab2|..."
    /// Each unique (spreadsheet + tab set) combination is bootstrapped once.
    /// This fixes the bug where all tabs shared a spreadsheetId and only the
    /// first caller's tabs were ever created.
    /// </summary>
    private readonly HashSet<string> _bootstrapped = new();
    private readonly SemaphoreSlim _bootstrapGate = new(1, 1);

    /// <summary>Tab GIDs keyed by "spreadsheetId::tabName".</summary>
    private readonly Dictionary<string, int> _tabGids = new();

    public GoogleSheetsClient(
        IOptions<GoogleSheetsOptions> opt,
        ILogger<GoogleSheetsClient> logger,
        Application.Services.SettingsService settingsService)
    {
        _opt = opt.Value;
        _logger = logger;
        _settingsService = settingsService;

        // When admin saves new credentials via Settings UI → reset auth + bootstrap
        _settingsService.SettingsChanged += () =>
        {
            _service = null;           // force re-auth with new credentials
            _bootstrapped.Clear();     // force re-bootstrap all tabs
            _logger.LogInformation("Settings changed — GoogleSheetsClient reset.");
        };
    }

    /// <summary>
    /// Effective SpreadsheetId — user-settings.json overrides appsettings.json.
    /// Repositories call this when they need the real sheet ID.
    /// </summary>
    public string EffectiveSpreadsheetId =>
        _settingsService.GetSpreadsheetId(_opt.SpreadsheetId);

    /// <summary>
    /// Resolves the spreadsheet ID for a specific entity.
    /// If the entity has its own override ID → use it.
    /// Otherwise → use EffectiveSpreadsheetId (which picks up UI-saved settings).
    /// Always call this as a property (not in constructor) so UI changes are reflected.
    /// </summary>
    public string ResolveSheetId(string? entitySpecificId) =>
        string.IsNullOrWhiteSpace(entitySpecificId)
            ? EffectiveSpreadsheetId
            : entitySpecificId;

    // ── Auth ─────────────────────────────────────────────────────────────────

    private async Task<SheetsService> ServiceAsync(CancellationToken ct)
    {
        if (_service is not null) return _service;
        await _authGate.WaitAsync(ct);
        try
        {
            if (_service is not null) return _service;

            GoogleCredential credential;

            // Priority 1 — credentials JSON pasted via Settings UI (user-settings.json)
            // Priority 2 — credentials JSON set via Azure App Settings (GoogleSheets__CredentialsJson)
            var credJson = _settingsService.GetCredentialsJson();
            if (string.IsNullOrWhiteSpace(credJson) && !string.IsNullOrWhiteSpace(_opt.CredentialsJson))
            {
                credJson = _opt.CredentialsJson;
                _logger.LogInformation("Using credentials from Azure App Settings (GoogleSheets__CredentialsJson).");
            }

            if (!string.IsNullOrWhiteSpace(credJson))
            {
                credential = GoogleCredential.FromJson(credJson)
                    .CreateScoped(SheetsService.Scope.Spreadsheets);
                _logger.LogInformation("Using credentials JSON (UI or Azure App Settings).");
            }
            else
            {
                // Priority 2 — fall back to credentials file on disk (appsettings.json → CredentialsPath)
                var path = Path.IsPathRooted(_opt.CredentialsPath)
                    ? _opt.CredentialsPath
                    : Path.Combine(AppContext.BaseDirectory, _opt.CredentialsPath);
                if (!File.Exists(path) && File.Exists(_opt.CredentialsPath)) path = _opt.CredentialsPath;

                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        $"Google credentials not found. Either paste your credentials JSON in " +
                        $"Settings → Google Sheets, or place the file at '{path}'.");

                await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    credential = GoogleCredential.FromStream(fs).CreateScoped(SheetsService.Scope.Spreadsheets);
                _logger.LogInformation("Using credentials from file: {Path}", path);
            }

            _service = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "DIGIHOOK CRM"
            });
            _logger.LogInformation("Google Sheets service authenticated.");
            return _service;
        }
        finally { _authGate.Release(); }
    }

    // ── Schema bootstrap (per spreadsheet + tab set) ─────────────────────────

    /// <summary>
    /// Creates missing tabs and writes header rows for the given tab set.
    /// Cache key = "spreadsheetId::tab1|tab2|..." so each tab set is handled
    /// independently even when they share the same spreadsheetId.
    /// </summary>
    public async Task EnsureSchemaAsync(
        string spreadsheetId,
        IEnumerable<(string tab, string[] header)> wanted,
        CancellationToken ct = default)
    {
        var wantedList = wanted.ToList();

        // Build a deterministic cache key that includes the tab names
        var cacheKey = spreadsheetId + "::" +
                       string.Join("|", wantedList.Select(w => w.tab).OrderBy(t => t));

        if (_bootstrapped.Contains(cacheKey)) return;

        await _bootstrapGate.WaitAsync(ct);
        try
        {
            if (_bootstrapped.Contains(cacheKey)) return;

            var service = await ServiceAsync(ct);
            var meta = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
            var existing = meta.Sheets.ToDictionary(
                s => s.Properties.Title, s => s.Properties.SheetId ?? 0);

            // Create any missing tabs in a single batch
            var addRequests = new List<Request>();
            foreach (var (tab, _) in wantedList)
                if (!existing.ContainsKey(tab))
                    addRequests.Add(new Request
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties { Title = tab }
                        }
                    });

            if (addRequests.Count > 0)
            {
                await service.Spreadsheets
                    .BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = addRequests }, spreadsheetId)
                    .ExecuteAsync(ct);
                // Re-fetch to get updated GIDs
                meta = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
                existing = meta.Sheets.ToDictionary(
                    s => s.Properties.Title, s => s.Properties.SheetId ?? 0);
                _logger.LogInformation("Created {N} new tab(s) in spreadsheet {Id}",
                    addRequests.Count, spreadsheetId);
            }

            // Write header row on blank tabs; cache GIDs
            foreach (var (tab, header) in wantedList)
            {
                _tabGids[$"{spreadsheetId}::{tab}"] = existing.GetValueOrDefault(tab, 0);
                var first = await ReadAsync(spreadsheetId, tab, "1:1", ct);
                if (first.Count == 0 || first[0].Count == 0)
                {
                    await OverwriteAsync(spreadsheetId, tab, "A1",
                        new List<IList<object>> { header.Cast<object>().ToList() }, ct);
                    _logger.LogInformation("Wrote header row for tab '{Tab}' in {Id}", tab, spreadsheetId);
                }
            }

            _bootstrapped.Add(cacheKey);
            _logger.LogInformation("Schema ready: {Key}", cacheKey);
        }
        finally { _bootstrapGate.Release(); }
    }

    // ── Tab discovery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all tab names in a spreadsheet that are NOT in the systemTabs set.
    /// Used by FacebookSyncService to auto-detect ad/lead tabs.
    /// </summary>
    public async Task<List<string>> GetNonSystemTabsAsync(
        string spreadsheetId,
        HashSet<string> systemTabs,
        CancellationToken ct = default)
    {
        return await WithRetryAsync(async () =>
        {
            var service = await ServiceAsync(ct);
            var meta = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
            return meta.Sheets
                .Select(s => s.Properties.Title)
                .Where(t => !systemTabs.Contains(t))
                .ToList();
        }, ct);
    }

    // ── Per-entity convenience methods ────────────────────────────────────────

    public Task EnsureLeadsSchemaAsync(CancellationToken ct) =>
        EnsureSchemaAsync(ResolveSheetId(_opt.LeadsSpreadsheetId), new[] { (_opt.LeadsTab, LeadHeader) }, ct);

    public Task EnsureEmployeesSchemaAsync(CancellationToken ct) =>
        EnsureSchemaAsync(ResolveSheetId(_opt.EmployeesSpreadsheetId), new[] { (_opt.EmployeesTab, EmployeeHeader) }, ct);

    public Task EnsureActivitySchemaAsync(CancellationToken ct) =>
        EnsureSchemaAsync(ResolveSheetId(_opt.ActivitySpreadsheetId), new[] { (_opt.ActivityTab, ActivityHeader) }, ct);

    public Task EnsureRemindersSchemaAsync(CancellationToken ct) =>
        EnsureSchemaAsync(ResolveSheetId(_opt.RemindersSpreadsheetId), new[] { (_opt.RemindersTab, ReminderHeader) }, ct);

    public Task EnsureMetaEventsSchemaAsync(CancellationToken ct) =>
        EnsureSchemaAsync(ResolveSheetId(_opt.MetaEventsSpreadsheetId), new[] { (_opt.MetaEventsTab, MetaEventHeader) }, ct);

    // ── Tab GID helper ────────────────────────────────────────────────────────

    public async Task<int> TabGidAsync(string spreadsheetId, string tab, CancellationToken ct)
    {
        var key = $"{spreadsheetId}::{tab}";
        if (_tabGids.TryGetValue(key, out var gid)) return gid;

        var service = await ServiceAsync(ct);
        var meta = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
        foreach (var s in meta.Sheets)
            _tabGids[$"{spreadsheetId}::{s.Properties.Title}"] = s.Properties.SheetId ?? 0;
        return _tabGids.GetValueOrDefault(key, 0);
    }

    // ── Retry helper for 429 rate-limit errors ─────────────────────────────────
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var delays = new[] { 1000, 2000, 4000, 8000, 15000 };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Google.GoogleApiException ex) when (
                (int)ex.HttpStatusCode == 429 && attempt < delays.Length)
            {
                _logger.LogWarning("Google Sheets rate limit hit — retrying in {Delay}ms (attempt {N})",
                    delays[attempt], attempt + 1);
                await Task.Delay(delays[attempt], ct);
            }
        }
    }

    // ── Data methods ──────────────────────────────────────────────────────────

    public async Task<IList<IList<object>>> ReadAsync(
        string spreadsheetId, string tab, string a1Range, CancellationToken ct)
    {
        return await WithRetryAsync(async () =>
        {
            var service = await ServiceAsync(ct);
            var resp = await service.Spreadsheets.Values
                .Get(spreadsheetId, $"{tab}!{a1Range}")
                .ExecuteAsync(ct);
            return resp.Values ?? new List<IList<object>>();
        }, ct);
    }

    /// <summary>Appends one row; returns the 1-based row index written.</summary>
    public async Task<int> AppendAsync(
        string spreadsheetId, string tab, IList<object> row, CancellationToken ct)
    {
        var service = await ServiceAsync(ct);
        var body = new ValueRange { Values = new List<IList<object>> { row } };
        var req = service.Spreadsheets.Values.Append(body, spreadsheetId, $"{tab}!A:A");
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
        req.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        var resp = await req.ExecuteAsync(ct);
        var updated = resp.Updates?.UpdatedRange ?? "";
        var startCell = updated.Split('!').Last().Split(':').First();
        var digits = new string(startCell.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var idx) ? idx : -1;
    }

    public async Task OverwriteAsync(
        string spreadsheetId, string tab, string a1Start,
        IList<IList<object>> values, CancellationToken ct)
    {
        var service = await ServiceAsync(ct);
        var body = new ValueRange { Values = values };
        var req = service.Spreadsheets.Values.Update(body, spreadsheetId, $"{tab}!{a1Start}");
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await req.ExecuteAsync(ct);
    }

    /// <summary>Physically deletes a 1-based sheet row.</summary>
    public async Task DeleteRowAsync(string spreadsheetId, string tab, int rowNumber, CancellationToken ct)
    {
        var service = await ServiceAsync(ct);
        var gid = await TabGidAsync(spreadsheetId, tab, ct);
        var req = new Request
        {
            DeleteDimension = new DeleteDimensionRequest
            {
                Range = new DimensionRange
                {
                    SheetId = gid,
                    Dimension = "ROWS",
                    StartIndex = rowNumber - 1,
                    EndIndex = rowNumber
                }
            }
        };
        await service.Spreadsheets
            .BatchUpdate(new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request> { req }
            }, spreadsheetId)
            .ExecuteAsync(ct);
    }
}

#pragma warning restore CS0618
