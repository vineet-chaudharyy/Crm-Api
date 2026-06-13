using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class ActivityLogRepository : IActivityLogRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId;
    private string SheetId => _client.ResolveSheetId(_entitySheetId);
    private readonly string _tab;

    public ActivityLogRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.ActivitySpreadsheetId;
        _tab = opt.Value.ActivityTab;
    }

    public async Task LogAsync(ActivityLog entry, CancellationToken ct = default)
    {
        await _client.EnsureActivitySchemaAsync(ct);
        if (string.IsNullOrWhiteSpace(entry.Timestamp))
            entry.Timestamp = IndianTime.NowString();
        await _client.AppendAsync(SheetId, _tab, new List<object>
        {
            entry.Timestamp, entry.User, entry.Action, entry.LeadId, entry.Details
        }, ct);
    }

    public async Task<List<ActivityLog>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await _client.EnsureActivitySchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:E", ct);
        var list = new List<ActivityLog>();
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            list.Add(new ActivityLog
            {
                Timestamp = Cell(r, 0),
                User = Cell(r, 1),
                Action = Cell(r, 2),
                LeadId = Cell(r, 3),
                Details = Cell(r, 4)
            });
        }
        list.Reverse(); // newest first
        return list.Take(count).ToList();
    }

    public async Task<List<ActivityLog>> GetByLeadIdAsync(string leadId, CancellationToken ct = default)
    {
        await _client.EnsureActivitySchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:E", ct);
        var list = new List<ActivityLog>();
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            if (!Cell(r, 3).Equals(leadId, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new ActivityLog
            {
                Timestamp = Cell(r, 0),
                User = Cell(r, 1),
                Action = Cell(r, 2),
                LeadId = Cell(r, 3),
                Details = Cell(r, 4)
            });
        }
        list.Reverse(); // newest first
        return list;
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";
}

