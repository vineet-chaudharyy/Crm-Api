using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class AttendanceRepository : IAttendanceRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId;
    private string SheetId => _client.ResolveEntitySheetId("Attendance", _entitySheetId);
    private readonly string _tab;

    public AttendanceRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.AttendanceSpreadsheetId;
        _tab = opt.Value.AttendanceTab;
    }

    public async Task<List<Attendance>> GetAllAsync(CancellationToken ct = default)
    {
        await _client.EnsureAttendanceSchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:F", ct);
        var list = new List<Attendance>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(Cell(r, 0))) continue;
            list.Add(MapRow(r, i + 2));
        }
        return list;
    }

    public async Task<Attendance?> GetTodayAsync(string employeeId, string date, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(a =>
            a.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase) &&
            a.Date == date);
    }

    public async Task<Attendance> AddAsync(Attendance attendance, CancellationToken ct = default)
    {
        await _client.EnsureAttendanceSchemaAsync(ct);
        attendance.RowNumber = await _client.AppendAsync(SheetId, _tab, ToRow(attendance), ct);
        return attendance;
    }

    public async Task<bool> UpdateAsync(Attendance attendance, CancellationToken ct = default)
    {
        if (attendance.RowNumber <= 0) return false;
        await _client.OverwriteAsync(SheetId, _tab, $"A{attendance.RowNumber}",
            new List<IList<object>> { ToRow(attendance) }, ct);
        return true;
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";

    private static Attendance MapRow(IList<object> r, int rowNumber) => new()
    {
        Date = Cell(r, 0),
        EmployeeId = Cell(r, 1),
        EmployeeName = Cell(r, 2),
        LoginTime = Cell(r, 3),
        LogoutTime = Cell(r, 4),
        TotalHours = Cell(r, 5),
        RowNumber = rowNumber
    };

    private static IList<object> ToRow(Attendance a) => new List<object>
    {
        a.Date, a.EmployeeId, a.EmployeeName, a.LoginTime, a.LogoutTime, a.TotalHours
    };
}
