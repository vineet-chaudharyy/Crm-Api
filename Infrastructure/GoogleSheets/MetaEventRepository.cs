using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class MetaEventRepository : IMetaEventRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId;
    private string SheetId => _client.ResolveEntitySheetId("MetaEvents", _entitySheetId);
    private readonly string _tab;

    public MetaEventRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.MetaEventsSpreadsheetId;
        _tab = opt.Value.MetaEventsTab;
    }

    public async Task<List<MetaConversionEvent>> GetAllAsync(CancellationToken ct = default)
    {
        await _client.EnsureMetaEventsSchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:J", ct);
        var list = new List<MetaConversionEvent>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(Cell(r, 0))) continue;
            list.Add(MapRow(r, i + 2));
        }
        return list;
    }

    public async Task<MetaConversionEvent?> GetByIdAsync(string eventId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(e => e.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MetaConversionEvent> AddAsync(MetaConversionEvent evt, CancellationToken ct = default)
    {
        await _client.EnsureMetaEventsSchemaAsync(ct);
        evt.EventId = await NextEventIdAsync(ct);
        if (string.IsNullOrWhiteSpace(evt.CreatedDate))
            evt.CreatedDate = IndianTime.NowString();
        evt.RowNumber = await _client.AppendAsync(SheetId, _tab, ToRow(evt), ct);
        return evt;
    }

    public async Task<bool> UpdateAsync(MetaConversionEvent evt, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(evt.EventId, ct);
        if (existing is null) return false;
        evt.RowNumber = existing.RowNumber;
        await _client.OverwriteAsync(SheetId, _tab, $"A{existing.RowNumber}",
            new List<IList<object>> { ToRow(evt) }, ct);
        return true;
    }

    // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<string> NextEventIdAsync(CancellationToken ct)
    {
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:A", ct);
        var max = 0;
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            var digits = new string((r[0]?.ToString() ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > max) max = n;
        }
        return $"META-{(max + 1):D4}";
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";

    private static MetaConversionEvent MapRow(IList<object> r, int rowNumber) => new()
    {
        EventId = Cell(r, 0),
        LeadId = Cell(r, 1),
        LeadName = Cell(r, 2),
        PreviousStatus = Cell(r, 3),
        NewStatus = Cell(r, 4),
        EventName = Cell(r, 5),
        EventSent = Cell(r, 6),
        MetaResponse = Cell(r, 7),
        CreatedDate = Cell(r, 8),
        RetryCount = Cell(r, 9),
        RowNumber = rowNumber
    };

    private static IList<object> ToRow(MetaConversionEvent e) => new List<object>
    {
        e.EventId, e.LeadId, e.LeadName, e.PreviousStatus, e.NewStatus,
        e.EventName, e.EventSent, e.MetaResponse, e.CreatedDate, e.RetryCount
    };
}

