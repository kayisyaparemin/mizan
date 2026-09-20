namespace Mizan.Application.Services;

/// <summary>
/// Clean Architecture uyumlu telemetri ve hata raporlama arayüzü.
/// Domain ve Application katmanları harici telemetri sağlayıcılarına (Sentry, AppCenter vb.) doğrudan bağımlı olmaz.
/// </summary>
public interface ITelemetryService
{
    void TrackEvent(string eventName, IDictionary<string, string>? properties = null);
    void TrackInvariantViolation(string invariantCode, string description, IDictionary<string, string>? details = null);
    void CaptureException(Exception exception, IDictionary<string, string>? context = null);
    void AddBreadcrumb(string message, string category = "navigation");
}
