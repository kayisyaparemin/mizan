using Mizan.Application.Services;
using Sentry;

namespace Mizan.App.Services;

/// <summary>
/// Sentry.Maui tabanlı telemetri ve çökme raporlama servisi.
/// </summary>
public sealed class SentryTelemetryService : ITelemetryService
{
    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        SentrySdk.CaptureEvent(new SentryEvent
        {
            Message = eventName,
            Level = SentryLevel.Info
        }, scope =>
        {
            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    scope.SetExtra(kvp.Key, kvp.Value);
                }
            }
        });
    }

    public void TrackInvariantViolation(string invariantCode, string description, IDictionary<string, string>? details = null)
    {
        SentrySdk.CaptureEvent(new SentryEvent
        {
            Message = $"[INVARIANT {invariantCode}] {description}",
            Level = SentryLevel.Error
        }, scope =>
        {
            scope.SetTag("invariant_code", invariantCode);
            if (details != null)
            {
                foreach (var kvp in details)
                {
                    scope.SetExtra(kvp.Key, kvp.Value);
                }
            }
        });
    }

    public void CaptureException(Exception exception, IDictionary<string, string>? context = null)
    {
        SentrySdk.CaptureException(exception, scope =>
        {
            if (context != null)
            {
                foreach (var kvp in context)
                {
                    scope.SetExtra(kvp.Key, kvp.Value);
                }
            }
        });
    }

    public void AddBreadcrumb(string message, string category = "navigation")
    {
        SentrySdk.AddBreadcrumb(message, category: category);
    }
}
