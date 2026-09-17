using System.Text.RegularExpressions;

namespace Mizan.Tests;

// Ekrana giden tarih ve tutar metinleri cihaz kültürüne bırakılmamalı:
// emülatör İngilizce çalıştığı için "07 October 2026" ya da "1,234.56 TL"
// görünüyor. Bu hata sınıfı parça parça defalarca düzeltildi; bu test yeni
// bir kültürsüz biçim eklendiğinde derlemeyi kırmızıya çevirir.
//
// Uygulama başlangıcında DefaultThreadCurrentCulture'ı tr-TR yapmak yerine
// her çağrıda kültürü açıkça vermeyi seçtik: global ayar, kültüre duyarlı
// string karşılaştırmalarını (Türkçe I/İ, sıralama) da sessizce değiştirir,
// Domain/Application katmanındaki metinleri test ortamında doğrulanamaz
// bırakır ve Android'in yerel tarih seçicisini zaten etkilemez.
public sealed class CultureFormattingSourceTests
{
    // $"{tarih:dd MMMM yyyy}" gibi interpolasyon delikleri sağlayıcı
    // alamaz; biçim kültüre duyarlıysa her zaman ihlaldir.
    private static readonly Regex InterpolationHole = new(
        @"\{[^{}""\r\n]+?:(?<format>[^{}""\r\n]+)\}",
        RegexOptions.Compiled);

    // Tek argümanlı ToString("..."); ikinci argüman (kültür) verilmişse
    // kapanış parantezi hemen gelmez.
    private static readonly Regex ToStringWithoutProvider = new(
        @"\.ToString\(\s*""(?<format>[^""]*)""\s*\)",
        RegexOptions.Compiled);

    // XAML'de Binding StringFormat='{0:...}' ve DatePicker Format="..."
    // cihaz kültürüyle biçimlenir.
    private static readonly Regex XamlFormat = new(
        @"\{0:(?<format>[^{}]+)\}|\bFormat=""(?<format>[^""]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void UserFacingFormats_AlwaysPassTurkishCulture()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var violations = new List<string>();

        foreach (var file in SourceFiles(src, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Collect(file, text, InterpolationHole, violations);
            Collect(file, text, ToStringWithoutProvider, violations);
        }

        foreach (var file in SourceFiles(src, "*.xaml"))
        {
            Collect(file, File.ReadAllText(file), XamlFormat, violations);
        }

        Assert.True(
            violations.Count == 0,
            "Kültürsüz biçimlendirme bulundu; ToString(format, TurkishCulture) kullan:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("dd MMMM yyyy", true)]
    [InlineData("dd MMM", true)]
    [InlineData("dddd", true)]
    [InlineData("N2", true)]
    [InlineData("n0", true)]
    [InlineData("C2", true)]
    [InlineData("P1", true)]
    [InlineData("#,##0.00", true)]
    [InlineData("dd.MM.yyyy", false)]
    [InlineData("dd.MM", false)]
    [InlineData("N", false)]
    [InlineData("D", false)]
    [InlineData("O", false)]
    [InlineData("yyyy-MM-dd", false)]
    public void CultureSensitiveFormat_RecognizesNamesAndSeparators(
        string format,
        bool expected) =>
        Assert.Equal(expected, IsCultureSensitive(format));

    [Fact]
    public void Scanner_FlagsCultureLessCallsButNotProviderCalls()
    {
        Assert.Matches(InterpolationHole, "$\"{plan.PeriodStart:dd MMMM yyyy}\"");
        Assert.Matches(ToStringWithoutProvider, "date.ToString(\"dd MMMM\")");
        Assert.DoesNotMatch(
            ToStringWithoutProvider,
            "date.ToString(\"dd MMMM\", TurkishCulture)");
        Assert.DoesNotMatch(
            ToStringWithoutProvider,
            "date.ToString(\n    \"dd MMMM\",\n    TurkishCulture)");
    }

    private static void Collect(
        string file,
        string text,
        Regex pattern,
        List<string> violations)
    {
        foreach (Match match in pattern.Matches(text))
        {
            if (!IsCultureSensitive(match.Groups["format"].Value))
            {
                continue;
            }

            var line = text.AsSpan(0, match.Index).Count('\n') + 1;
            violations.Add($"{file}:{line}: {match.Value}");
        }
    }

    // Ay/gün adları (MMM, ddd) ile binlik/ondalık ayırıcı üreten sayı
    // biçimleri kültüre bağlıdır. "dd.MM.yyyy" içindeki nokta sabit
    // karakterdir, kültürden etkilenmez. Sayı biçimlerinde basamak şartı,
    // Guid'in "N"/"P" biçimleriyle karışmamak için.
    private static bool IsCultureSensitive(string format)
    {
        var trimmed = format.Trim();
        return trimmed.Contains("MMM", StringComparison.Ordinal) ||
               trimmed.Contains("ddd", StringComparison.Ordinal) ||
               Regex.IsMatch(trimmed, @"^[NnCcPpFf]\d+$") ||
               Regex.IsMatch(trimmed, @"^[#0,.]*[#0][#0,.]*[.,][#0,.]*$");
    }

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Mizan.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "Mizan repository root was not found.");
    }
}
