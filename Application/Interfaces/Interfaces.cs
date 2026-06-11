using Crm_Api.Application.Dtos;
using Crm_Api.Domain.Entities;
using MetaDtos = Crm_Api.Application.Dtos;

namespace Crm_Api.Application.Interfaces;

public interface ILeadRepository
{
    Task<List<Lead>> GetAllAsync(CancellationToken ct = default);
    Task<Lead?> GetByIdAsync(string leadId, CancellationToken ct = default);
    Task<Lead> AddAsync(Lead lead, CancellationToken ct = default);
    Task<bool> UpdateAsync(Lead lead, CancellationToken ct = default);
    Task<bool> DeleteAsync(string leadId, CancellationToken ct = default);
}

public interface IEmployeeRepository
{
    Task<List<Employee>> GetAllAsync(CancellationToken ct = default);
    Task<Employee?> GetByIdAsync(string employeeId, CancellationToken ct = default);
    Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Employee> AddAsync(Employee employee, CancellationToken ct = default);
    Task<bool> UpdateAsync(Employee employee, CancellationToken ct = default);
}

public interface IActivityLogRepository
{
    Task LogAsync(ActivityLog entry, CancellationToken ct = default);
    Task<List<ActivityLog>> GetRecentAsync(int count, CancellationToken ct = default);
}

public interface IJwtTokenService
{
    (string token, DateTime expiresAt) CreateToken(Employee employee);
}

public interface IReminderRepository
{
    Task<List<Reminder>> GetAllAsync(CancellationToken ct = default);
    Task<List<Reminder>> GetByLeadIdAsync(string leadId, CancellationToken ct = default);
    Task<List<Reminder>> GetByEmployeeIdAsync(string employeeId, CancellationToken ct = default);
    Task<Reminder?> GetByIdAsync(string reminderId, CancellationToken ct = default);
    Task<Reminder> AddAsync(Reminder reminder, CancellationToken ct = default);
    Task<bool> UpdateAsync(Reminder reminder, CancellationToken ct = default);
    Task<bool> DeleteAsync(string reminderId, CancellationToken ct = default);
}

/// <summary>Persists Meta Conversions API event logs to Google Sheets.</summary>
public interface IMetaEventRepository
{
    Task<List<MetaConversionEvent>> GetAllAsync(CancellationToken ct = default);
    Task<MetaConversionEvent?> GetByIdAsync(string eventId, CancellationToken ct = default);
    Task<MetaConversionEvent> AddAsync(MetaConversionEvent evt, CancellationToken ct = default);
    Task<bool> UpdateAsync(MetaConversionEvent evt, CancellationToken ct = default);
}

public interface IAttendanceRepository
{
    Task<List<Attendance>> GetAllAsync(CancellationToken ct = default);
    Task<Attendance?> GetTodayAsync(string employeeId, string date, CancellationToken ct = default);
    Task<Attendance> AddAsync(Attendance attendance, CancellationToken ct = default);
    Task<bool> UpdateAsync(Attendance attendance, CancellationToken ct = default);
}

/// <summary>Sends events to Meta Conversions API and logs results.</summary>
public interface IMetaConversionService
{
    /// <summary>
    /// Fire a Conversions API event for a lead status change.
    /// Never throws — all errors are caught and logged.
    /// </summary>
    Task SendLeadEventAsync(Lead lead, string previousStatus, string newStatus, CancellationToken ct = default);

    /// <summary>Retry a previously failed event by its EventId.</summary>
    Task<bool> RetryEventAsync(string eventId, CancellationToken ct = default);

    Task<MetaDtos.MetaStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<List<MetaDtos.MetaEventDto>> GetEventsAsync(CancellationToken ct = default);
}
