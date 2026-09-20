using Mizan.Application.Services;

namespace Mizan.Infrastructure.Services;

/// <summary>
/// Test ortamı ve telemetri devre dışı olduğunda kullanılan güvenli no-op telemetri servisi.
/// </summary>
public sealed class NullTelemetryService : ITelemetryService
{
    public static readonly NullTelemetryService Instance = new();

    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
    }

    public void TrackInvariantViolation(string invariantCode, string description, IDictionary<string, string>? details = null)
    {
    }

    public void CaptureException(Exception exception, IDictionary<string, string>? context = null)
    {
    }

    public void AddBreadcrumb(string message, string category = "navigation")
    {
    }
}
