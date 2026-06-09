using System.Globalization;
using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Crm_Api.Infrastructure.GoogleSheets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm_Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class LeadsController : ControllerBase
{
    private readonly ILeadRepository _leads;
    private readonly IActivityLogRepository _activity;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GoogleSheetsClient _sheets;

    public LeadsController(
        ILeadRepository leads,
        IActivityLogRepository activity,
        IServiceScopeFactory scopeFactory,
        GoogleSheetsClient sheets)
    {
        _leads = leads;
        _activity = activity;
        _scopeFactory = scopeFactory;
        _sheets = sheets;
    }

    private string CurrentUser => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "unknown";
    private string CurrentRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Employee";
    private string CurrentName => User.Identity?.Name ?? "";

    // ── GET /api/leads ────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] LeadFilterDto filter, CancellationToken ct)
    {
        var leads = await _leads.GetAllAsync(ct);

        if (!CurrentRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            leads = leads.Where(l =>
                l.AssignedEmployee.Equals(CurrentName, StringComparison.OrdinalIgnoreCase) ||
                l.AssignedEmployee.Equals(CurrentUser, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            leads = leads.Where(l =>
                Contains(l.FullName, s) || Contains(l.EmailAddress, s) ||
                Contains(l.MobileNumber, s) || Contains(l.CompanyName, s) ||
                Contains(l.City, s) || Contains(l.LeadId, s)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(filter.Status))
            leads = leads.Where(l => l.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(filter.AssignedEmployee))
            leads = leads.Where(l => l.AssignedEmployee.Equals(filter.AssignedEmployee, StringComparison.OrdinalIgnoreCase)).ToList();
        if (TryDate(filter.FromDate, out var from))
            leads = leads.Where(l => TryDate(l.DateAdded, out var d) && d >= from).ToList();
        if (TryDate(filter.ToDate, out var to))
            leads = leads.Where(l => TryDate(l.DateAdded, out var d) && d <= to).ToList();

        return Ok(leads.OrderByDescending(l => l.LeadId));
    }

    // ── GET /api/leads/{id} ───────────────────────────────────────────────────
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var lead = await _leads.GetByIdAsync(id, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    // ── POST /api/leads ───────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LeadUpsertDto dto, CancellationToken ct)
    {
        var created = await _leads.AddAsync(Map(dto), ct);

        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser, Action = "Created", LeadId = created.LeadId,
            Details = $"Lead '{created.FullName}' created"
        }, ct);

        // Fire Meta event in its own scope (prevents scoped-service-after-dispose bug)
        FireMetaEvent(created, "", "New");

        return CreatedAtAction(nameof(Get), new { id = created.LeadId }, created);
    }

    // ── PUT /api/leads/{id} ───────────────────────────────────────────────────
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] LeadUpsertDto dto, CancellationToken ct)
    {
        // Capture existing status BEFORE update for change detection
        var existing = await _leads.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        var previousStatus = existing.Status;
        var newStatus = string.IsNullOrWhiteSpace(dto.Status) ? "New" : dto.Status;

        var lead = Map(dto);
        lead.LeadId = id;
        var ok = await _leads.UpdateAsync(lead, ct);
        if (!ok) return NotFound();

        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser, Action = "Updated", LeadId = id,
            Details = $"Lead updated → status {newStatus}"
        }, ct);

        // Only fire Meta event when status actually changed
        if (!previousStatus.Equals(newStatus, StringComparison.OrdinalIgnoreCase))
        {
            // Fetch the fully saved lead (has correct email/phone for hashing)
            var saved = await _leads.GetByIdAsync(id, ct) ?? lead;
            FireMetaEvent(saved, previousStatus, newStatus);
        }

        return Ok(await _leads.GetByIdAsync(id, ct));
    }

    // ── DELETE /api/leads/{id} ────────────────────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await _leads.DeleteAsync(id, ct);
        if (!ok) return NotFound();
        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser, Action = "Deleted", LeadId = id, Details = "Lead deleted"
        }, ct);
        return NoContent();
    }

    // ── PUT /api/leads/{id}/transfer ──────────────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/transfer")]
    public async Task<IActionResult> Transfer(string id, [FromBody] TransferLeadDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ToEmployeeName))
            return BadRequest(new { message = "ToEmployeeName is required." });

        var lead = await _leads.GetByIdAsync(id, ct);
        if (lead is null) return NotFound();

        var previousEmployee = string.IsNullOrWhiteSpace(lead.AssignedEmployee)
            ? "Unassigned" : lead.AssignedEmployee;
        var previousStatus = lead.Status;

        lead.AssignedEmployee = dto.ToEmployeeName;
        lead.Status = "Assigned";
        var ok = await _leads.UpdateAsync(lead, ct);
        if (!ok) return StatusCode(500, new { message = "Transfer failed." });

        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser, Action = "Transferred", LeadId = id,
            Details = $"Lead transferred from '{previousEmployee}' → '{dto.ToEmployeeName}'" +
                      (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $" | Reason: {dto.Reason}")
        }, ct);

        FireMetaEvent(lead, previousStatus, "Assigned");

        return Ok(await _leads.GetByIdAsync(id, ct));
    }

    // ── POST /api/leads/import ────────────────────────────────────────────────
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);
        var lines = content.Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return BadRequest(new { message = "CSV has no data rows." });

        var headers = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Idx(params string[] names) => headers.FindIndex(h => names.Contains(h));

        int iName    = Idx("full name", "name", "fullname");
        int iMobile  = Idx("mobile number", "mobile", "phone");
        int iEmail   = Idx("email address", "email");
        int iCity    = Idx("city");
        int iState   = Idx("state");
        int iCompany = Idx("company name", "company");
        int iSource  = Idx("lead source", "source");
        int iStatus  = Idx("status");
        int iAssigned = Idx("assigned employee", "assigned");

        var imported = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            var cols = SplitCsv(lines[i]);
            string At(int idx) => idx >= 0 && idx < cols.Count ? cols[idx].Trim() : "";
            var name = At(iName);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var status = string.IsNullOrWhiteSpace(At(iStatus)) ? "New" : At(iStatus);
            var created = await _leads.AddAsync(new Lead
            {
                FullName = name, MobileNumber = At(iMobile), EmailAddress = At(iEmail),
                City = At(iCity), State = At(iState), CompanyName = At(iCompany),
                LeadSource = string.IsNullOrWhiteSpace(At(iSource)) ? "Import" : At(iSource),
                Status = status, AssignedEmployee = At(iAssigned)
            }, ct);

            FireMetaEvent(created, "", status);
            imported++;
        }

        await _activity.LogAsync(new ActivityLog
        {
            User = CurrentUser, Action = "Imported",
            Details = $"{imported} leads imported from CSV"
        }, ct);

        return Ok(new { imported });
    }

    // ── POST /api/leads/sync-facebook ─────────────────────────────────────────
    /// <summary>
    /// Reads the "Video Ad" tab (Facebook Lead Ads export) from Google Sheets
    /// and imports any new leads into the CRM Leads tab.
    /// Deduplication is done by phone number — existing phones are skipped.
    ///
    /// Column mapping (0-based):
    ///  0=id, 1=created_time, 3=ad_name, 12=which_service, 13=business_name,
    ///  14=full_name, 15=phone, 16=email, 17=lead_status, 18=follow_up, 19=date, 20=time
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("sync-facebook")]
    public async Task<IActionResult> SyncFacebook(CancellationToken ct)
    {
        var spreadsheetId = _sheets.EffectiveSpreadsheetId;

        // Read all rows from "Video Ad" tab (skip header row 1)
        IList<IList<object>> rows;
        try
        {
            rows = await _sheets.ReadAsync(spreadsheetId, "Video Ad", "A2:U", ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Could not read 'Video Ad' tab: {ex.Message}" });
        }

        if (rows.Count == 0)
            return Ok(new { message = "No data found in 'Video Ad' tab.", added = 0, skipped = 0 });

        // Build set of existing phone numbers for deduplication
        var existing = await _leads.GetAllAsync(ct);
        var existingPhones = new HashSet<string>(
            existing.Select(l => NormaliseDigits(l.MobileNumber ?? "")),
            StringComparer.OrdinalIgnoreCase);

        // Also track FB lead IDs stored in Notes to avoid re-importing same record
        var existingFbIds = new HashSet<string>(
            existing
                .Where(l => (l.Notes ?? "").Contains("FB:"))
                .Select(l =>
                {
                    var start = (l.Notes ?? "").IndexOf("FB:") + 3;
                    var end   = (l.Notes ?? "").IndexOf(' ', start);
                    return end < 0
                        ? (l.Notes ?? "")[start..]
                        : (l.Notes ?? "")[start..end];
                }),
            StringComparer.OrdinalIgnoreCase);

        int added = 0, skipped = 0;

        foreach (var row in rows)
        {
            string Col(int i) => i < row.Count ? (row[i]?.ToString() ?? "").Trim() : "";

            var fbId     = Col(0);
            var phone    = Col(15);
            var normPhone = NormaliseDigits(phone);

            // Skip if already imported (by FB id or phone)
            if ((!string.IsNullOrWhiteSpace(fbId)    && existingFbIds.Contains(fbId)) ||
                (!string.IsNullOrWhiteSpace(normPhone) && existingPhones.Contains(normPhone)))
            {
                skipped++;
                continue;
            }

            var fullName     = Col(14);
            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
            {
                skipped++; continue; // empty row
            }

            var createdTime  = Col(1);
            var adName       = Col(3);
            var service      = Col(12);
            var businessName = Col(13);
            var email        = Col(16);
            var fbStatus     = Col(17);
            var followUp     = Col(18);
            var followDate   = Col(19);
            var followTime   = Col(20);

            // Parse date
            var dateAdded = DateTime.TryParse(createdTime, out var dt)
                ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // Map Facebook status → CRM status
            var status = fbStatus?.Trim().ToLowerInvariant() switch
            {
                "converted"  => "Converted",
                "interested" => "Interested",
                "contacted"  => "Contacted",
                "rejected"   => "Rejected",
                "follow up"  => "Follow Up",
                _            => "New"
            };

            // Combine follow-up date + time
            var followUpDate = "";
            if (!string.IsNullOrWhiteSpace(followDate))
                followUpDate = string.IsNullOrWhiteSpace(followTime)
                    ? followDate
                    : $"{followDate} {followTime}";
            else if (!string.IsNullOrWhiteSpace(followUp))
                followUpDate = followUp;

            // Build notes
            var notesParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(service))      notesParts.Add($"Service: {service}");
            if (!string.IsNullOrWhiteSpace(fbId))         notesParts.Add($"FB: {fbId}");

            var lead = new Lead
            {
                FullName        = string.IsNullOrWhiteSpace(fullName) ? "Unknown" : fullName,
                MobileNumber    = phone,
                EmailAddress    = email,
                CompanyName     = businessName,
                LeadSource      = string.IsNullOrWhiteSpace(adName) ? "Facebook" : $"Facebook - {adName}",
                Status          = status,
                FollowUpDate    = followUpDate,
                AssignedEmployee = "",
                Notes           = string.Join(" | ", notesParts),
                DateAdded       = dateAdded,
                City            = "",
                State           = "",
            };

            var created = await _leads.AddAsync(lead, ct);
            FireMetaEvent(created, "", status);

            if (!string.IsNullOrWhiteSpace(normPhone))  existingPhones.Add(normPhone);
            if (!string.IsNullOrWhiteSpace(fbId))       existingFbIds.Add(fbId);
            added++;
        }

        await _activity.LogAsync(new ActivityLog
        {
            User    = CurrentUser,
            Action  = "Facebook Sync",
            Details = $"Facebook sheet sync: {added} new leads imported, {skipped} duplicates skipped."
        }, ct);

        return Ok(new
        {
            message = $"✓ Sync complete! {added} new leads imported, {skipped} duplicates skipped.",
            added,
            skipped
        });
    }

    // ── Meta helper — creates its own DI scope so scoped services stay valid ──
    /// <summary>
    /// Fires a Meta Conversions API event in a fresh DI scope running on the
    /// thread-pool. Creating a new scope ensures IMetaEventRepository and
    /// IMetaConversionService are not tied to the (soon-to-be-disposed)
    /// HTTP request scope, which was the root cause of silent failures.
    /// </summary>
    private void FireMetaEvent(Lead lead, string previousStatus, string newStatus)
    {
        // Capture only value-type / immutable data — not DI-managed objects
        var capturedLead = lead;
        var capturedPrev = previousStatus;
        var capturedNew  = newStatus;

        _ = Task.Run(async () =>
        {
            // New scope → fresh, non-disposed scoped services
            await using var scope = _scopeFactory.CreateAsyncScope();
            var meta = scope.ServiceProvider.GetRequiredService<IMetaConversionService>();
            await meta.SendLeadEventAsync(capturedLead, capturedPrev, capturedNew);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool Contains(string hay, string needle) =>
        (hay ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool TryDate(string? s, out DateTime d) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private static Lead Map(LeadUpsertDto d) => new()
    {
        FullName = d.FullName, MobileNumber = d.MobileNumber, EmailAddress = d.EmailAddress,
        City = d.City, State = d.State, CompanyName = d.CompanyName, LeadSource = d.LeadSource,
        Status = string.IsNullOrWhiteSpace(d.Status) ? "New" : d.Status,
        FollowUpDate = d.FollowUpDate, AssignedEmployee = d.AssignedEmployee, Notes = d.Notes
    };

    private static string NormaliseDigits(string s) =>
        new string(s.Where(char.IsDigit).ToArray());

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { result.Add(cur.ToString()); cur.Clear(); }
            else if (c != '\r') cur.Append(c);
        }
        result.Add(cur.ToString());
        return result;
    }
}
