namespace Crm_Api.Application.Dtos;

public class DashboardStatsDto
{
    public int TotalLeads { get; set; }
    public int NewLeads { get; set; }
    public int FollowUpsDue { get; set; }
    public int Converted { get; set; }
    public int Rejected { get; set; }

    /// <summary>Status -> count, for the doughnut chart.</summary>
    public Dictionary<string, int> ByStatus { get; set; } = new();

    /// <summary>Source -> count, for the bar chart.</summary>
    public Dictionary<string, int> BySource { get; set; } = new();

    /// <summary>Last ~14 days: date -> leads added, for the line chart.</summary>
    public Dictionary<string, int> LeadsPerDay { get; set; } = new();

    public List<RecentActivityDto> RecentActivity { get; set; } = new();
}

public class RecentActivityDto
{
    public string Timestamp { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string LeadId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class EmployeePerformanceDto
{
    public string Employee { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Converted { get; set; }
    public int Rejected { get; set; }
    public double ConversionRate { get; set; }
}
