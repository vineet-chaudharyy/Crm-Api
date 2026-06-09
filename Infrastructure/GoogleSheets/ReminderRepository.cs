using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class ReminderRepository : IReminderRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId;
    private string SheetId => _client.ResolveSheetId(_entitySheetId);
    private readonly string _tab;

    public ReminderRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.RemindersSpreadsheetId;
        _tab = opt.Value.RemindersTab;
    }

    public async Task<List<Reminder>> GetAllAsync(CancellationToken ct = default)
    {
        await _client.EnsureRemindersSchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:H", ct);
        var list = new List<Reminder>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(Cell(r, 0))) continue;
            list.Add(MapRow(r, i + 2));
        }
        return list;
    }

    public async Task<List<Reminder>> GetByLeadIdAsync(string leadId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(r => r.LeadId.Equals(leadId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<Reminder>> GetByEmployeeIdAsync(string employeeId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(r => r.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<Reminder?> GetByIdAsync(string reminderId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(r => r.ReminderId.Equals(reminderId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Reminder> AddAsync(Reminder reminder, CancellationToken ct = default)
    {
        await _client.EnsureRemindersSchemaAsync(ct);
        reminder.ReminderId = await NextReminderIdAsync(ct);
        reminder.IsCompleted = "FALSE";
        reminder.RowNumber = await _client.AppendAsync(SheetId, _tab, ToRow(reminder), ct);
        return reminder;
    }

    public async Task<bool> UpdateAsync(Reminder reminder, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(reminder.ReminderId, ct);
        if (existing is null) return false;

        reminder.RowNumber = existing.RowNumber;
        await _client.OverwriteAsync(SheetId, _tab, $"A{existing.RowNumber}",
            new List<IList<object>> { ToRow(reminder) }, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string reminderId, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(reminderId, ct);
        if (existing is null) return false;
        await _client.DeleteRowAsync(SheetId, _tab, existing.RowNumber, ct);
        return true;
    }

    // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<string> NextReminderIdAsync(CancellationToken ct)
    {
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:A", ct);
        var max = 0;
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            var digits = new string((r[0]?.ToString() ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > max) max = n;
        }
        return $"REM-{(max + 1):D4}";
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";

    private static Reminder MapRow(IList<object> r, int rowNumber) => new()
    {
        ReminderId = Cell(r, 0),
        LeadId = Cell(r, 1),
        LeadName = Cell(r, 2),
        EmployeeId = Cell(r, 3),
        EmployeeName = Cell(r, 4),
        ReminderTime = Cell(r, 5),
        Notes = Cell(r, 6),
        IsCompleted = Cell(r, 7),
        RowNumber = rowNumber
    };

    private static IList<object> ToRow(Reminder r) => new List<object>
    {
        r.ReminderId, r.LeadId, r.LeadName, r.EmployeeId, r.EmployeeName,
        r.ReminderTime, r.Notes, r.IsCompleted
    };
}

