using System.Reflection;
using System.Runtime.CompilerServices;
using Mizan.Application.Services;
using Mizan.Domain.Models;
using Mizan.Infrastructure.Persistence;

namespace Mizan.Tests.Architecture;

/// <summary>
/// Projenin mimari kurallarını (Clean Architecture, MVVM Invariants) otomatik denetleyen
/// ve ihlal eden ajan / geliştiricileri test aşamasında derhal durduran test kalkanı.
/// </summary>
public sealed class ArchitecturalInvariantTests
{
    private static readonly string SolutionRoot = ResolveSolutionRoot();

    [Fact]
    public void ViewModels_MustNotExceed_350_Lines()
    {
        var viewModelsDir = Path.Combine(SolutionRoot, "src", "Mizan.App", "ViewModels");
        if (!Directory.Exists(viewModelsDir))
        {
            return; // CI ortamında path çözülemezse atla
        }

        var files = Directory.GetFiles(viewModelsDir, "*.cs");
        var violations = new List<string>();

        foreach (var file in files)
        {
            var lineCount = File.ReadAllLines(file).Length;
            if (lineCount > 350)
            {
                violations.Add($"{Path.GetFileName(file)}: {lineCount} satır (Max: 350)");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Mimari Kural İhlali: ViewModels 350 satırı aşamaz!\nİhlal eden dosyalar:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void ViewModels_MustNotReference_Forbidden_UI_Types()
    {
        var viewModelsDir = Path.Combine(SolutionRoot, "src", "Mizan.App", "ViewModels");
        if (!Directory.Exists(viewModelsDir))
        {
            return;
        }

        var forbiddenTokens = new[]
        {
            "Shell.Current",
            "NavigationPage",
            "DisplayAlert",
            "Application.Current"
        };

        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(viewModelsDir, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var token in forbiddenTokens)
                {
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetFileName(file)} (Satır {i + 1}): '{token}' kullanımı yasaktır. INavigationService veya IDialogService kullanın.");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Mimari Kural İhlali: ViewModels UI bileşenlerine doğrudan bağımlı olamaz!\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void No_Async_Void_Methods_In_Domain_Application_Or_Infrastructure()
    {
        var assemblies = new[]
        {
            typeof(FinancialPlan).Assembly,
            typeof(MizanService).Assembly,
            typeof(SqliteMizanStore).Assembly
        };

        var asyncVoidMethods = new List<string>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.ReturnType == typeof(void) &&
                        method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
                    {
                        asyncVoidMethods.Add($"{type.FullName}.{method.Name}");
                    }
                }
            }
        }

        Assert.True(
            asyncVoidMethods.Count == 0,
            $"Mimari Kural İhlali: 'async void' metotlar tespit edildi (Task dönülmelidir):\n{string.Join("\n", asyncVoidMethods)}");
    }

    [Fact]
    public void Domain_Assembly_Must_Not_Reference_Other_Projects()
    {
        var domainAssembly = typeof(FinancialPlan).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        var forbiddenReferences = new[]
        {
            "Mizan.Application",
            "Mizan.Infrastructure",
            "Mizan.App",
            "Microsoft.Maui",
            "Microsoft.Data.Sqlite"
        };

        var violations = referencedAssemblies
            .Where(r => forbiddenReferences.Any(f => r.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Clean Architecture İhlali: Mizan.Domain dış bağımlılık içeremez! Tespit edilenler: {string.Join(", ", violations)}");
    }

    [Fact]
    public void No_New_Source_String_Tests_Allowed()
    {
        var testDir = Path.Combine(SolutionRoot, "tests", "Mizan.Tests");
        if (!Directory.Exists(testDir))
        {
            return;
        }

        var sourceTestFiles = Directory.GetFiles(testDir, "*SourceTests.cs")
            .Select(Path.GetFileName)
            .ToList();

        // Projede daha önceden kalan 12 eski dosya haricinde yeni *SourceTests eklenemez
        const int LegacyBaseline = 12;
        Assert.True(
            sourceTestFiles.Count <= LegacyBaseline,
            $"Mimari Kural İhlali (Kural 4A): Yeni sahte string arayan test dosyası (*SourceTests.cs) eklenemez! Gerçek xUnit davranış testleri yazılmalıdır. Mevcut sayı: {sourceTestFiles.Count}, İzin verilen max: {LegacyBaseline}");
    }

    private static string ResolveSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Mizan.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
