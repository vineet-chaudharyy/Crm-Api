using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;

namespace Crm_Api.Application.Services;

public class DashboardService
{
    private readonly ILeadRepository _leads;
    private readonly IActivityLogRepository _activity;

    public DashboardService(ILeadRepository leads, IActivityLogRepository activity)
    {
        _leads = leads;
        _activity = activity;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var leads = await _leads.GetAllAsync(ct);
        var today = IndianTime.Today;

        var dto = new DashboardStatsDto
        {
            TotalLeads = leads.Count,
            NewLeads = leads.Count(l => l.Status.Equals("New", StringComparison.OrdinalIgnoreCase)),
            Converted = leads.Count(l => l.Status.Equals("Converted", StringComparison.OrdinalIgnoreCase)),
            Rejected = leads.Count(l => l.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)),
            FollowUpsDue = leads.Count(l =>
                DateTime.TryParse(l.FollowUpDate, out var d) && d.Date <= today &&
                !l.Status.Equals("Converted", StringComparison.OrdinalIgnoreCase) &&
                !l.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)),
            UnassignedLeads = leads.Count(l => string.IsNullOrWhiteSpace(l.AssignedEmployee)),
            LeadsToday = leads.Count(l => DateTime.TryParse(l.DateAdded, out var da) && da.Date == today),
            SiteVisits = leads.Count(l => l.Status.Equals("Site Visit", StringComparison.OrdinalIgnoreCase)),
            SiteVisitsToday = leads.Count(l => l.Status.Equals("Site Visit", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParse(l.LastUpdated, out var lu) && lu.Date == today),
            DuplicateLeads = leads
                .Where(l => !string.IsNullOrWhiteSpace(l.MobileNumber))
                .GroupBy(l => new string(l.MobileNumber.Where(char.IsDigit).ToArray()))
                .Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Sum(g => g.Count())
        };

        dto.ByStatus = leads.GroupBy(l => string.IsNullOrWhiteSpace(l.Status) ? "Unknown" : l.Status)
                            .ToDictionary(g => g.Key, g => g.Count());

        dto.BySource = leads.GroupBy(l => string.IsNullOrWhiteSpace(l.LeadSource) ? "Unknown" : l.LeadSource)
                            .ToDictionary(g => g.Key, g => g.Count());

        // last 14 days
        var perDay = new Dictionary<string, int>();
        for (var i = 13; i >= 0; i--)
        {
            var day = today.AddDays(-i).ToString("yyyy-MM-dd");
            perDay[day] = 0;
        }
        foreach (var l in leads)
        {
            if (DateTime.TryParse(l.DateAdded, out var d))
            {
                var key = d.ToString("yyyy-MM-dd");
                if (perDay.ContainsKey(key)) perDay[key]++;
            }
        }
        dto.LeadsPerDay = perDay;

        var recent = await _activity.GetRecentAsync(8, ct);
        dto.RecentActivity = recent.Select(a => new RecentActivityDto
        {
            Timestamp = a.Timestamp,
            User = a.User,
            Action = a.Action,
            LeadId = a.LeadId,
            Details = a.Details
        }).ToList();

        return dto;
    }

    public async Task<List<EmployeePerformanceDto>> GetEmployeePerformanceAsync(CancellationToken ct = default)
    {
        var leads = await _leads.GetAllAsync(ct);
        var today = IndianTime.Today;
        return leads
            .Where(l => !string.IsNullOrWhiteSpace(l.AssignedEmployee))
            .GroupBy(l => l.AssignedEmployee)
            .Select(g =>
            {
                var total = g.Count();
                var converted = g.Count(l => l.Status.Equals("Converted", StringComparison.OrdinalIgnoreCase));
                var notEngaged = new[] { "New", "Assigned", "" };
                var called = g.Count(l => !notEngaged.Contains(l.Status, StringComparer.OrdinalIgnoreCase));
                var pending = g.Count(l =>
                    DateTime.TryParse(l.FollowUpDate, out var d) && d.Date <= today &&
                    !l.Status.Equals("Converted", StringComparison.OrdinalIgnoreCase) &&
                    !l.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase));
                return new EmployeePerformanceDto
                {
                    Employee = g.Key,
                    Total = total,
                    Called = called,
                    PendingFollowUps = pending,
                    SiteVisits = g.Count(l => l.Status.Equals("Site Visit", StringComparison.OrdinalIgnoreCase)),
                    Converted = converted,
                    Rejected = g.Count(l => l.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)),
                    ConversionRate = total == 0 ? 0 : Math.Round(converted * 100.0 / total, 1)
                };
            })
            .OrderByDescending(x => x.Converted)
            .ToList();
    }
}
