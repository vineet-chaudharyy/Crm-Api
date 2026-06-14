using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Infrastructure.GoogleSheets;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly GoogleSheetsClient _client;
    private readonly string? _entitySheetId;
    private string SheetId => _client.ResolveEntitySheetId("Employees", _entitySheetId);
    private readonly string _tab;

    public EmployeeRepository(GoogleSheetsClient client, IOptions<GoogleSheetsOptions> opt)
    {
        _client = client;
        _entitySheetId = opt.Value.EmployeesSpreadsheetId;
        _tab = opt.Value.EmployeesTab;
    }

    public async Task<List<Employee>> GetAllAsync(CancellationToken ct = default)
    {
        await _client.EnsureEmployeesSchemaAsync(ct);
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:G", ct);
        var list = new List<Employee>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0 || string.IsNullOrWhiteSpace(Cell(r, 0))) continue;
            list.Add(MapRow(r, i + 2));
        }
        return list;
    }

    public async Task<Employee?> GetByIdAsync(string employeeId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(e => e.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(e => e.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> UpdateAsync(Employee emp, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(emp.EmployeeId, ct);
        if (existing is null) return false;

        emp.RowNumber = existing.RowNumber;
        if (string.IsNullOrWhiteSpace(emp.Email)) emp.Email = existing.Email;
        if (string.IsNullOrWhiteSpace(emp.CreatedDate)) emp.CreatedDate = existing.CreatedDate;
        if (string.IsNullOrWhiteSpace(emp.PasswordHash)) emp.PasswordHash = existing.PasswordHash;

        await _client.OverwriteAsync(SheetId, _tab, $"A{existing.RowNumber}",
            new List<IList<object>> { ToRow(emp) }, ct);
        return true;
    }

    public async Task<Employee> AddAsync(Employee e, CancellationToken ct = default)
    {
        await _client.EnsureEmployeesSchemaAsync(ct);
        e.EmployeeId = await NextEmployeeIdAsync(ct);
        if (string.IsNullOrWhiteSpace(e.CreatedDate))
            e.CreatedDate = IndianTime.TodayString();
        e.RowNumber = await _client.AppendAsync(SheetId, _tab, ToRow(e), ct);
        return e;
    }

    private async Task<string> NextEmployeeIdAsync(CancellationToken ct)
    {
        var rows = await _client.ReadAsync(SheetId, _tab, "A2:A", ct);
        var max = 0;
        foreach (var r in rows)
        {
            if (r.Count == 0) continue;
            var digits = new string((r[0]?.ToString() ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n) && n > max) max = n;
        }
        return $"EMP-{(max + 1):D4}";
    }

    private static string Cell(IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";

    private static Employee MapRow(IList<object> r, int rowNumber) => new()
    {
        EmployeeId = Cell(r, 0),
        FullName = Cell(r, 1),
        Email = Cell(r, 2),
        PasswordHash = Cell(r, 3),
        Role = Cell(r, 4),
        Active = !Cell(r, 5).Equals("FALSE", StringComparison.OrdinalIgnoreCase)
                 && !Cell(r, 5).Equals("false", StringComparison.OrdinalIgnoreCase),
        CreatedDate = Cell(r, 6),
        RowNumber = rowNumber
    };

    private static IList<object> ToRow(Employee e) => new List<object>
    {
        e.EmployeeId, e.FullName, e.Email, e.PasswordHash, e.Role,
        e.Active ? "TRUE" : "FALSE", e.CreatedDate
    };
}

