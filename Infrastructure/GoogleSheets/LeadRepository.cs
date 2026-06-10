using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class LeadRepository : ILeadRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId; // per-entity override (null = use effective default)
    private readonly string _tab;
    private const int ColCount = 14; // A..N

    // Always evaluated fresh — picks up SpreadsheetId saved via Settings UI
    private string SheetId => _client.ResolveSheetId(_entitySheetId);

    public LeadRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.LeadsSpreadsheetId; // null/empty = use default
        _tab = opt.Value.LeadsTab;
    }

    public async Task<List<Lead>> GetAllAsync(CancellationToken ct = default)
    {
        await _client.EnsureLeadsSchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:N", ct);
        var leads = new List<Lead>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(Cell(r, 0))) continue;
            leads.Add(MapRow(r, rowNumber: i + 2));
        }
        return leads;
    }

    public async Task<Lead?> GetByIdAsync(string leadId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(l => l.LeadId.Equals(leadId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Lead> AddAsync(Lead lead, CancellationToken ct = default)
    {
        await _client.EnsureLeadsSchemaAsync(ct);
        lead.LeadId = await NextLeadIdAsync(ct);
        var now = IndianTime.NowString();
        if (string.IsNullOrWhiteSpace(lead.DateAdded))
            lead.DateAdded = IndianTime.TodayString();
        lead.LastUpdated = now;
        lead.RowNumber = await _client.AppendAsync(SheetId, _tab, ToRow(lead), ct);
        return lead;
    }

    public async Task<bool> UpdateAsync(Lead lead, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(lead.LeadId, ct);
        if (existing is null) return false;

        lead.RowNumber = existing.RowNumber;
        lead.DateAdded = string.IsNullOrWhiteSpace(lead.DateAdded) ? existing.DateAdded : lead.DateAdded;
        lead.LastUpdated = IndianTime.NowString();

        await _client.OverwriteAsync(SheetId, _tab, $"A{existing.RowNumber}",
            new List<IList<object>> { ToRow(lead) }, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string leadId, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(leadId, ct);
        if (existing is null) return false;
        await _client.DeleteRowAsync(SheetId, _tab, existing.RowNumber, ct);
        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<string> NextLeadIdAsync(CancellationToken ct)
    {
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:A", ct);
        var max = 0;
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            var digits = new string((r[0]?.ToString() ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > max) max = n;
        }
        return $"LD-{(max + 1):D4}";
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";

    private static Lead MapRow(IList<object> r, int rowNumber) => new()
    {
        LeadId = Cell(r, 0),
        DateAdded = Cell(r, 1),
        FullName = Cell(r, 2),
        MobileNumber = Cell(r, 3),
        EmailAddress = Cell(r, 4),
        City = Cell(r, 5),
        State = Cell(r, 6),
        CompanyName = Cell(r, 7),
        LeadSource = Cell(r, 8),
        Status = Cell(r, 9),
        FollowUpDate = Cell(r, 10),
        AssignedEmployee = Cell(r, 11),
        Notes = Cell(r, 12),
        LastUpdated = Cell(r, 13),
        RowNumber = rowNumber
    };

    private static IList<object> ToRow(Lead l) => new List<object>
    {
        l.LeadId, l.DateAdded, l.FullName, l.MobileNumber, l.EmailAddress,
        l.City, l.State, l.CompanyName, l.LeadSource, l.Status,
        l.FollowUpDate, l.AssignedEmployee, l.Notes, l.LastUpdated
    };
}
