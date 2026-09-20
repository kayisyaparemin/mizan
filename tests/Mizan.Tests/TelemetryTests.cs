using Mizan.Application.Services;
using Mizan.Infrastructure.Services;
using Xunit;

namespace Mizan.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void NullTelemetryService_ExecutesWithoutThrowing()
    {
        ITelemetryService telemetry = NullTelemetryService.Instance;

        // Verify all contract methods execute safely
        telemetry.TrackEvent("test_event", new Dictionary<string, string> { ["key"] = "value" });
        telemetry.TrackInvariantViolation("I16", "Plan frozen check", new Dictionary<string, string> { ["planId"] = "1" });
        telemetry.CaptureException(new InvalidOperationException("Test exception"), new Dictionary<string, string> { ["context"] = "unit_test" });
        telemetry.AddBreadcrumb("Navigated to dashboard", "navigation");

        Assert.NotNull(telemetry);
    }
}
