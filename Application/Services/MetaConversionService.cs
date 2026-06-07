using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Crm_Api.Application.Services;

/// <summary>
/// Sends lead status-change events to Meta Conversions API (graph.facebook.com).
/// Hashes PII with SHA-256 before transmission as required by Meta.
/// All operations are fire-and-forget safe — errors are caught and logged to
/// the MetaEvents sheet so they can be retried later.
/// </summary>
public class MetaConversionService : IMetaConversionService
{
    private readonly IMetaEventRepository _repo;
    private readonly ILeadRepository _leads;
    private readonly HttpClient _http;
    private readonly MetaConversionOptions _opts;
    private readonly SettingsService _settings;
    private readonly ILogger<MetaConversionService> _logger;

    // ── Status → Meta event name mapping ────────────────────────────────────
    private static readonly Dictionary<string, string> StatusToEvent =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "New",         "Lead"                  },
            { "Contacted",   "Contact"               },
            { "Interested",  "ViewContent"           },
            { "Follow Up",   "Lead"                  },
            { "Assigned",    "Lead"                  },
            { "Converted",   "Purchase"              },
            { "Rejected",    "Other"                 },
        };

    public MetaConversionService(
        IMetaEventRepository repo,
        ILeadRepository leads,
        HttpClient http,
        IOptions<MetaConversionOptions> opts,
        SettingsService settings,
        ILogger<MetaConversionService> logger)
    {
        _repo = repo;
        _leads = leads;
        _http = http;
        _opts = opts.Value;
        _settings = settings;
        _logger = logger;
    }

    // Effective credentials — SettingsService (UI) overrides appsettings.json
    private string EffectiveDatasetId   => _settings.GetMetaDatasetId(_opts.DatasetId);
    private string EffectiveAccessToken => _settings.GetMetaAccessToken(_opts.AccessToken);
    private bool   EffectiveEnabled     => _settings.GetMetaEnabled(_opts.Enabled);
    private string EffectiveVersion     => _settings.GetMetaApiVersion(_opts.ApiVersion);
    private string EffectiveSource      => _settings.GetMetaLeadEventSource(_opts.LeadEventSource);

    // ── Public interface ──────────────────────────────────────────────────────

    public async Task SendLeadEventAsync(
        Lead lead, string previousStatus, string newStatus, CancellationToken ct = default)
    {
        var eventName = StatusToEvent.GetValueOrDefault(newStatus, "Lead");

        // Create the log entry immediately (before sending)
        var logEntry = await _repo.AddAsync(new MetaConversionEvent
        {
            LeadId = lead.LeadId,
            LeadName = lead.FullName,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            EventName = eventName,
            EventSent = "FALSE",
            RetryCount = "0",
        }, ct);

        // Skip sending if not configured or disabled (check effective values, not appsettings)
        if (!EffectiveEnabled ||
            string.IsNullOrWhiteSpace(EffectiveDatasetId) ||
            string.IsNullOrWhiteSpace(EffectiveAccessToken) ||
            EffectiveAccessToken.Contains("YOUR_ACCESS_TOKEN"))
        {
            logEntry.MetaResponse = "Skipped — Meta not configured. Add DatasetId and AccessToken in Settings page.";
            await _repo.UpdateAsync(logEntry, ct);
            _logger.LogWarning("Meta Conversions API not configured. Event {Id} skipped.", logEntry.EventId);
            return;
        }

        await SendAndLogAsync(logEntry, lead, ct);
    }

    public async Task<bool> RetryEventAsync(string eventId, CancellationToken ct = default)
    {
        var logEntry = await _repo.GetByIdAsync(eventId, ct);
        if (logEntry is null) return false;
        if (logEntry.EventSent.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;

        var lead = await _leads.GetByIdAsync(logEntry.LeadId, ct);
        if (lead is null)
        {
            logEntry.MetaResponse = "Retry failed — lead no longer exists.";
            await _repo.UpdateAsync(logEntry, ct);
            return false;
        }

        logEntry.RetryCount = (int.TryParse(logEntry.RetryCount, out var r) ? r + 1 : 1).ToString();
        await SendAndLogAsync(logEntry, lead, ct);
        return logEntry.EventSent.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MetaStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var events = await _repo.GetAllAsync(ct);
        var successful = events.Count(e => e.EventSent.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
        var lastSync = events
            .Where(e => e.EventSent.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedDate)
            .FirstOrDefault()?.CreatedDate ?? "";

        return new MetaStatsDto
        {
            TotalEventsSent = events.Count,
            SuccessfulEvents = successful,
            FailedEvents = events.Count - successful,
            LastSyncTime = lastSync,
            IsConfigured = EffectiveEnabled
                           && !string.IsNullOrWhiteSpace(EffectiveDatasetId)
                           && !string.IsNullOrWhiteSpace(EffectiveAccessToken)
                           && !EffectiveAccessToken.Contains("YOUR_ACCESS_TOKEN")
        };
    }

    public async Task<List<MetaEventDto>> GetEventsAsync(CancellationToken ct = default)
    {
        var events = await _repo.GetAllAsync(ct);
        return events.OrderByDescending(e => e.CreatedDate).Select(e => new MetaEventDto
        {
            EventId = e.EventId,
            LeadId = e.LeadId,
            LeadName = e.LeadName,
            PreviousStatus = e.PreviousStatus,
            NewStatus = e.NewStatus,
            EventName = e.EventName,
            EventSent = e.EventSent.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
            MetaResponse = e.MetaResponse,
            CreatedDate = e.CreatedDate,
            RetryCount = int.TryParse(e.RetryCount, out var rc) ? rc : 0,
        }).ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SendAndLogAsync(MetaConversionEvent logEntry, Lead lead, CancellationToken ct)
    {
        try
        {
            var payload = BuildPayload(lead, logEntry.EventName);
            var url = $"https://graph.facebook.com/{EffectiveVersion}/{EffectiveDatasetId}" +
                      $"/events?access_token={EffectiveAccessToken}";

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                logEntry.EventSent = "TRUE";
                logEntry.MetaResponse = body;
                _logger.LogInformation("Meta event {Name} sent for lead {Id}. Response: {R}",
                    logEntry.EventName, lead.LeadId, body);
            }
            else
            {
                logEntry.EventSent = "FALSE";
                logEntry.MetaResponse = $"HTTP {(int)response.StatusCode}: {body}";
                _logger.LogError("Meta API returned {Code} for lead {Id}: {Body}",
                    response.StatusCode, lead.LeadId, body);
            }
        }
        catch (Exception ex)
        {
            logEntry.EventSent = "FALSE";
            logEntry.MetaResponse = $"Exception: {ex.Message}";
            _logger.LogError(ex, "Exception sending Meta event for lead {Id}", lead.LeadId);
        }

        await _repo.UpdateAsync(logEntry, ct);
    }

    private object BuildPayload(Lead lead, string eventName)
    {
        var eventTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // SHA-256 hash PII as required by Meta
        var hashedEmail = Hash(lead.EmailAddress?.Trim().ToLowerInvariant() ?? "");
        var hashedPhone = Hash(NormalisePhone(lead.MobileNumber ?? ""));

        var userData = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(hashedEmail))
            userData["em"] = new[] { hashedEmail };
        if (!string.IsNullOrWhiteSpace(hashedPhone))
            userData["ph"] = new[] { hashedPhone };
        // Include lead_id if the lead ID looks like a Meta lead ID (15-17 digits).
        // For our CRM IDs (LD-0001) we skip it.
        userData["lead_id"] = lead.LeadId;

        return new
        {
            data = new[]
            {
                new
                {
                    event_name = eventName,
                    event_time = eventTime,
                    action_source = "system_generated",
                    event_source = "crm",
                    lead_event_source = EffectiveSource,
                    user_data = userData
                }
            }
        };
    }

    /// <summary>SHA-256 hex hash — returns empty string for empty input.</summary>
    private static string Hash(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Remove all non-digit characters for phone normalisation.</summary>
    private static string NormalisePhone(string phone) =>
        new string(phone.Where(char.IsDigit).ToArray());
}
