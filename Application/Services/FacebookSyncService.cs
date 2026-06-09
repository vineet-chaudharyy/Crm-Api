using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Crm_Api.Infrastructure.GoogleSheets;

namespace Crm_Api.Application.Services;

/// <summary>
/// Syncs leads from the "Video Ad" Google Sheet tab into the CRM Leads tab.
///
/// Column layout (0-based index):
///  0=id  1=created_time  3=ad_name  12=which_service  13=business_name
///  14=full_name  15=phone  16=email  17=lead_status  18=follow_up  19=date  20=time
///
/// Rules:
///  • New rows   → imported as CRM leads (dedup by phone OR fb id in Notes)
///  • Rows gone  → matching CRM leads marked status="Deleted"
/// </summary>
public class FacebookSyncService
{
    private readonly GoogleSheetsClient _sheets;
    private readonly ILeadRepository _leads;
    private readonly IActivityLogRepository _activity;
    private readonly ILogger<FacebookSyncService> _logger;

    public const string SourceTab = "Video Ad";

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

        // ── Read source tab ───────────────────────────────────────────────────
        IList<IList<object>> rows;
        try
        {
            rows = await _sheets.ReadAsync(spreadsheetId, SourceTab, "A2:U", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FacebookSyncService: could not read '{Tab}' tab.", SourceTab);
            return (0, 0, 0);
        }

        // Build lookup: fbId → row data
        var sheetById = new Dictionary<string, IList<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var fbId = Col(row, 0);
            if (!string.IsNullOrWhiteSpace(fbId))
                sheetById[fbId] = row;
        }

        // ── Load existing CRM leads ───────────────────────────────────────────
        var existing      = await _leads.GetAllAsync(ct);
        var existingPhones = new HashSet<string>(
            existing.Select(l => NormaliseDigits(l.MobileNumber ?? "")),
            StringComparer.OrdinalIgnoreCase);

        // Map fbId → CRM lead (for delete detection)
        var crmByFbId = new Dictionary<string, Lead>(StringComparer.OrdinalIgnoreCase);
        foreach (var lead in existing)
        {
            var fbId = ExtractFbId(lead.Notes ?? "");
            if (!string.IsNullOrWhiteSpace(fbId))
                crmByFbId[fbId] = lead;
        }

        int added = 0, deleted = 0, skipped = 0;

        // ── Import NEW leads ──────────────────────────────────────────────────
        foreach (var row in rows)
        {
            var fbId     = Col(row, 0);
            var phone    = Col(row, 15);
            var normPhone = NormaliseDigits(phone);

            // Skip duplicates
            if ((!string.IsNullOrWhiteSpace(fbId)     && crmByFbId.ContainsKey(fbId)) ||
                (!string.IsNullOrWhiteSpace(normPhone) && existingPhones.Contains(normPhone)))
            {
                skipped++;
                continue;
            }

            var fullName = Col(row, 14);
            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
            {
                skipped++; continue;
            }

            var createdTime  = Col(row, 1);
            var adName       = Col(row, 3);
            var service      = Col(row, 12);
            var businessName = Col(row, 13);
            var email        = Col(row, 16);
            var fbStatus     = Col(row, 17);
            var followUp     = Col(row, 18);
            var followDate   = Col(row, 19);
            var followTime   = Col(row, 20);

            var dateAdded = DateTime.TryParse(createdTime, out var dt)
                ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            var status = MapStatus(fbStatus);

            var followUpDate = "";
            if (!string.IsNullOrWhiteSpace(followDate))
                followUpDate = string.IsNullOrWhiteSpace(followTime) ? followDate : $"{followDate} {followTime}";
            else if (!string.IsNullOrWhiteSpace(followUp))
                followUpDate = followUp;

            var notesParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(service)) notesParts.Add($"Service: {service}");
            if (!string.IsNullOrWhiteSpace(fbId))    notesParts.Add($"FB: {fbId}");

            var lead = new Lead
            {
                FullName         = string.IsNullOrWhiteSpace(fullName) ? "Unknown" : fullName,
                MobileNumber     = phone,
                EmailAddress     = email,
                CompanyName      = businessName,
                LeadSource       = string.IsNullOrWhiteSpace(adName) ? "Facebook" : $"Facebook - {adName}",
                Status           = status,
                FollowUpDate     = followUpDate,
                AssignedEmployee = "",
                Notes            = string.Join(" | ", notesParts),
                DateAdded        = dateAdded,
                City             = "",
                State            = "",
            };

            var created = await _leads.AddAsync(lead, ct);

            if (!string.IsNullOrWhiteSpace(normPhone)) existingPhones.Add(normPhone);
            if (!string.IsNullOrWhiteSpace(fbId))      crmByFbId[fbId] = created;
            added++;
        }

        // ── Mark DELETED leads ────────────────────────────────────────────────
        // Any CRM lead with a FB: id that is no longer in the sheet → "Deleted"
        foreach (var (fbId, lead) in crmByFbId)
        {
            if (sheetById.ContainsKey(fbId)) continue;                         // still in sheet ✓
            if (lead.Status.Equals("Deleted", StringComparison.OrdinalIgnoreCase)) continue; // already deleted

            lead.Status      = "Deleted";
            lead.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await _leads.UpdateAsync(lead, ct);

            await _activity.LogAsync(new ActivityLog
            {
                User    = "System",
                Action  = "Auto-Deleted",
                LeadId  = lead.LeadId,
                Details = $"Lead '{lead.FullName}' removed from Facebook sheet → marked Deleted"
            }, ct);

            deleted++;
        }

        _logger.LogInformation(
            "FacebookSync: +{Added} new, {Deleted} deleted, {Skipped} skipped.",
            added, deleted, skipped);

        if (added > 0 || deleted > 0)
        {
            await _activity.LogAsync(new ActivityLog
            {
                User    = "System",
                Action  = "Facebook Sync",
                Details = $"Auto-sync: {added} new leads imported, {deleted} marked Deleted, {skipped} duplicates skipped."
            }, ct);
        }

        return (added, deleted, skipped);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Col(IList<object> row, int i) =>
        i < row.Count ? (row[i]?.ToString() ?? "").Trim() : "";

    private static string NormaliseDigits(string s) =>
        new string(s.Where(char.IsDigit).ToArray());

    private static string ExtractFbId(string notes)
    {
        var idx = notes.IndexOf("FB:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var start = idx + 3;
        var end   = notes.IndexOf('|', start);
        var raw   = end < 0 ? notes[start..] : notes[start..end];
        return raw.Trim();
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
}
