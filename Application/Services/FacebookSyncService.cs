using System.Text.Json;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Crm_Api.Infrastructure.GoogleSheets;

namespace Crm_Api.Application.Services;

/// <summary>
/// Syncs leads from ALL non-CRM tabs in Google Sheets.
/// Auto-detects any tab that has lead-like columns (phone/email/name).
/// Supports any Facebook Lead Ads export format regardless of tab name.
/// </summary>
public class FacebookSyncService
{
    // These tabs belong to the CRM itself — never sync FROM them
    private static readonly HashSet<string> SystemTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Leads", "Employees", "Activity", "Reminders", "MetaEvents"
    };

    private readonly GoogleSheetsClient _sheets;
    private readonly ILeadRepository _leads;
    private readonly IActivityLogRepository _activity;
    private readonly ILogger<FacebookSyncService> _logger;

    public FacebookSyncService(
        GoogleSheetsClient sheets,
        ILeadRepository leads,
        IActivityLogRepository activity,
        ILogger<FacebookSyncService> logger)
    {
        _sheets   = sheets;
        _leads    = leads;
        _activity = activity;
        _logger   = logger;
    }

    public async Task<(int added, int deleted, int skipped)> RunAsync(CancellationToken ct = default)
    {
        var spreadsheetId = _sheets.EffectiveSpreadsheetId;

        // ── Discover all non-CRM tabs ─────────────────────────────────────────
        List<string> adTabs;
        try
        {
            adTabs = await GetAdTabsAsync(spreadsheetId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FacebookSyncService: could not list sheet tabs.");
            return (0, 0, 0);
        }

        if (adTabs.Count == 0)
        {
            _logger.LogInformation("FacebookSyncService: no ad tabs found to sync.");
            return (0, 0, 0);
        }

        _logger.LogInformation("FacebookSyncService: found {N} ad tab(s): {Tabs}",
            adTabs.Count, string.Join(", ", adTabs));

        // ── Load existing CRM leads once ──────────────────────────────────────
        var existing      = await _leads.GetAllAsync(ct);
        var existingPhones = new HashSet<string>(
            existing.Select(l => NormaliseDigits(l.MobileNumber ?? "")),
            StringComparer.OrdinalIgnoreCase);

        var crmByFbId = new Dictionary<string, Lead>(StringComparer.OrdinalIgnoreCase);
        foreach (var lead in existing)
        {
            var fbId = ExtractFbId(lead.Notes ?? "");
            if (!string.IsNullOrWhiteSpace(fbId))
                crmByFbId[fbId] = lead;
        }

        int totalAdded = 0, totalDeleted = 0, totalSkipped = 0;
        var allSheetFbIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Process each ad tab ───────────────────────────────────────────────
        foreach (var tabName in adTabs)
        {
            IList<IList<object>> rows;
            try
            {
                rows = await _sheets.ReadAsync(spreadsheetId, tabName, "A1:ZZ", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read tab '{Tab}' — skipping.", tabName);
                continue;
            }

            if (rows.Count < 2) continue; // header + at least 1 data row needed

            // Detect column positions from header row
            var colMap = DetectColumns(rows[0]);
            if (!colMap.HasLeadData)
            {
                _logger.LogInformation("Tab '{Tab}' has no recognisable lead columns — skipping.", tabName);
                continue;
            }

            _logger.LogInformation("Syncing tab '{Tab}' — columns: name={N} phone={P} email={E}",
                tabName, colMap.FullName, colMap.Phone, colMap.Email);

            // Process data rows (skip header)
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                string Col(int idx) => idx >= 0 && idx < row.Count ? (row[idx]?.ToString() ?? "").Trim() : "";

                var fbId      = Col(colMap.Id);
                var phone     = Col(colMap.Phone);
                var normPhone = NormaliseDigits(phone);
                var fullName  = Col(colMap.FullName);

                if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                    continue; // empty row

                if (!string.IsNullOrWhiteSpace(fbId)) allSheetFbIds.Add(fbId);

                // Build raw ad data JSON (all original columns)
                var rawData = new Dictionary<string, string>();
                for (var c = 0; c < rows[0].Count; c++)
                {
                    var colName = (rows[0][c]?.ToString() ?? "").Trim();
                    var colVal  = c < row.Count ? (row[c]?.ToString() ?? "").Trim() : "";
                    if (!string.IsNullOrWhiteSpace(colName) && !string.IsNullOrWhiteSpace(colVal))
                        rawData[colName] = colVal;
                }

                var createdTime  = Col(colMap.CreatedTime);
                var adName       = Col(colMap.AdName);
                var businessName = Col(colMap.BusinessName);
                var email        = Col(colMap.Email);
                var fbStatus     = Col(colMap.Status);
                var followUp     = Col(colMap.FollowUp);
                var followDate   = Col(colMap.FollowDate);
                var followTime   = Col(colMap.FollowTime);

                var dateAdded = DateTime.TryParse(createdTime, out var dt)
                    ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                    : IndianTime.NowString();

                var newStatus = MapStatus(fbStatus);

                var followUpDate = "";
                if (!string.IsNullOrWhiteSpace(followDate))
                    followUpDate = string.IsNullOrWhiteSpace(followTime) ? followDate : $"{followDate} {followTime}";
                else if (!string.IsNullOrWhiteSpace(followUp))
                    followUpDate = followUp;

                var notesParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(fbId)) notesParts.Add($"FB: {fbId}");
                notesParts.Add($"Ad Tab: {tabName}");
                notesParts.Add($"AD_DATA:{JsonSerializer.Serialize(rawData)}");
                var notes = string.Join(" | ", notesParts);

                var source = string.IsNullOrWhiteSpace(adName) ? $"Facebook - {tabName}" : $"Facebook - {adName}";

                // ── UPDATE existing lead (matched by FB id) ───────────────────
                if (!string.IsNullOrWhiteSpace(fbId) && crmByFbId.TryGetValue(fbId, out var existingLead))
                {
                    // Only update fields that come from the sheet; preserve CRM-managed fields
                    existingLead.FullName     = string.IsNullOrWhiteSpace(fullName) ? existingLead.FullName : fullName;
                    existingLead.MobileNumber = string.IsNullOrWhiteSpace(phone)    ? existingLead.MobileNumber : phone;
                    existingLead.EmailAddress = string.IsNullOrWhiteSpace(email)    ? existingLead.EmailAddress : email;
                    existingLead.CompanyName  = string.IsNullOrWhiteSpace(businessName) ? existingLead.CompanyName : businessName;
                    existingLead.LeadSource   = source;
                    existingLead.FollowUpDate = string.IsNullOrWhiteSpace(followUpDate) ? existingLead.FollowUpDate : followUpDate;
                    existingLead.Notes        = notes; // always refresh AD_DATA
                    // Only update status if CRM status is still "New" (not manually changed)
                    if (existingLead.Status.Equals("New", StringComparison.OrdinalIgnoreCase))
                        existingLead.Status = newStatus;

                    await _leads.UpdateAsync(existingLead, ct);
                    totalAdded++; // count as "synced"
                    continue;
                }

                // ── Also check by phone (no FB id available) ──────────────────
                if (!string.IsNullOrWhiteSpace(normPhone) && existingPhones.Contains(normPhone))
                {
                    totalSkipped++;
                    continue;
                }

                // ── ADD new lead ──────────────────────────────────────────────
                var lead = new Lead
                {
                    FullName         = string.IsNullOrWhiteSpace(fullName) ? "Unknown" : fullName,
                    MobileNumber     = phone,
                    EmailAddress     = email,
                    CompanyName      = businessName,
                    LeadSource       = source,
                    Status           = newStatus,
                    FollowUpDate     = followUpDate,
                    AssignedEmployee = "",
                    Notes            = notes,
                    DateAdded        = dateAdded,
                    City             = "",
                    State            = "",
                };

                var created = await _leads.AddAsync(lead, ct);
                if (!string.IsNullOrWhiteSpace(normPhone)) existingPhones.Add(normPhone);
                if (!string.IsNullOrWhiteSpace(fbId))      crmByFbId[fbId] = created;
                totalAdded++;
            }
        }

        // ── REMOVE leads — FB ids no longer in ANY ad tab ────────────────────
        foreach (var (fbId, lead) in crmByFbId)
        {
            if (allSheetFbIds.Contains(fbId)) continue;

            await _leads.DeleteAsync(lead.LeadId, ct);

            await _activity.LogAsync(new ActivityLog
            {
                User    = "System",
                Action  = "Auto-Deleted",
                LeadId  = lead.LeadId,
                Details = $"Lead '{lead.FullName}' removed from Facebook sheet → deleted from CRM"
            }, ct);

            totalDeleted++;
        }

        _logger.LogInformation(
            "FacebookSync: +{A} new, {D} deleted, {S} skipped across {T} tab(s).",
            totalAdded, totalDeleted, totalSkipped, adTabs.Count);

        if (totalAdded > 0 || totalDeleted > 0)
        {
            await _activity.LogAsync(new ActivityLog
            {
                User    = "System",
                Action  = "Facebook Sync",
                Details = $"Auto-sync ({adTabs.Count} tabs): {totalAdded} new, {totalDeleted} deleted, {totalSkipped} skipped."
            }, ct);
        }

        return (totalAdded, totalDeleted, totalSkipped);
    }

    // ── Get all non-CRM tabs from the spreadsheet ─────────────────────────────

    private async Task<List<string>> GetAdTabsAsync(string spreadsheetId, CancellationToken ct)
    {
        // Use the public ReadAsync with a dummy range just to trigger auth,
        // then use the sheets metadata endpoint via the SheetsService.
        // We expose a helper on GoogleSheetsClient for this.
        return await _sheets.GetNonSystemTabsAsync(spreadsheetId, SystemTabs, ct);
    }

    // ── Column auto-detection from header row ─────────────────────────────────

    private static ColumnMap DetectColumns(IList<object> header)
    {
        var map = new ColumnMap();
        for (var i = 0; i < header.Count; i++)
        {
            var col = (header[i]?.ToString() ?? "").Trim().ToLowerInvariant()
                       .Replace(" ", "_").Replace("?", "").Replace(":", "");

            if (col == "id" || col == "lead_id")                            map.Id           = i;
            if (IsMatch(col, "created_time", "created_at", "date", "timestamp")) map.CreatedTime  = i;
            if (IsMatch(col, "ad_name", "adname"))                           map.AdName       = i;
            if (IsMatch(col, "full_name", "fullname", "name", "customer_name")) map.FullName     = i;
            if (IsMatch(col, "phone", "phone_number", "mobile", "mobile_number", "contact")) map.Phone = i;
            if (IsMatch(col, "email", "email_address"))                      map.Email        = i;
            if (IsMatch(col, "what_is_your_business_name", "business_name", "company")) map.BusinessName = i;
            if (IsMatch(col, "which_service", "service", "service_interested", "interest")) map.Service = i;
            if (IsMatch(col, "lead_status", "status"))                       map.Status       = i;
            if (IsMatch(col, "follow_up", "followup"))                       map.FollowUp     = i;
            if (IsMatch(col, "date") && col != "created_time")               map.FollowDate   = i;
            if (IsMatch(col, "time"))                                        map.FollowTime   = i;
        }
        return map;
    }

    private static bool IsMatch(string col, params string[] targets) =>
        targets.Any(t => col == t || col.Contains(t));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormaliseDigits(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits; // ignore country code
    }

    private static string ExtractFbId(string notes)
    {
        var idx = notes.IndexOf("FB:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var start = idx + 3;
        var end   = notes.IndexOf('|', start);
        return (end < 0 ? notes[start..] : notes[start..end]).Trim();
    }

    private static string MapStatus(string fbStatus) =>
        (fbStatus ?? "").Trim().ToLowerInvariant() switch
        {
            "converted"  => "Converted",
            "interested" => "Interested",
            "contacted"  => "Contacted",
            "rejected"   => "Rejected",
            "follow up"  => "Follow Up",
            _            => "New"
        };

    private class ColumnMap
    {
        public int Id           = -1;
        public int CreatedTime  = -1;
        public int AdName       = -1;
        public int FullName     = -1;
        public int Phone        = -1;
        public int Email        = -1;
        public int BusinessName = -1;
        public int Service      = -1;
        public int Status       = -1;
        public int FollowUp     = -1;
        public int FollowDate   = -1;
        public int FollowTime   = -1;

        // A tab is considered a lead source if it has at least phone OR (name + email)
        public bool HasLeadData => Phone >= 0 || (FullName >= 0 && Email >= 0);
    }
}
