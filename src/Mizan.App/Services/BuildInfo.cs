using System.Reflection;

namespace Mizan.App.Services;

public static class BuildInfo
{
#if MIZAN_DEV_BUILD
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    public static string Version => AppInfo.Current.VersionString;
    public static string Commit => Metadata("MizanCommit", "local");
    public static string BuildNumber => Metadata("MizanBuildNumber", AppInfo.Current.BuildString);
    public static string Channel => IsDevelopment ? "Geliştirme Sürümü" : "Kararlı Sürüm";

    private static string Metadata(string key, string fallback) =>
        typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == key)?.Value ?? fallback;
}
