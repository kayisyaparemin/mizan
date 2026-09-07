using System.Globalization;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Drawables;

/// <summary>
/// Dönem sonu nakit eğrisi. MAUI'de hazır grafik kontrolü yok; yeni bir
/// bağımlılık eklemek yerine <see cref="Microsoft.Maui.Graphics"/> ile
/// çiziliyor.
///
/// Ölçek her zaman sıfırı içerir ve sıfır çizgisi ayrıca vurgulanır: eğrinin
/// sıfırı kestiği ay "açığın kapandığı ay"dır ve grafiğin taşıdığı asıl bilgi
/// odur.
/// </summary>
public sealed class CashProjectionChartDrawable : IDrawable
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    private const float LeftPadding = 54f;
    private const float RightPadding = 12f;
    private const float TopPadding = 14f;
    private const float BottomPadding = 26f;

    private static readonly Color GridColor = Color.FromArgb("#BFCFFF");
    private static readonly Color ZeroLineColor = Color.FromArgb("#585762");
    private static readonly Color LabelColor = Color.FromArgb("#7E7A83");
    private static readonly Color BaselineColor = Color.FromArgb("#7E7A83");
    private static readonly Color ScenarioColor = Color.FromArgb("#4D567E");
    private static readonly Color NegativeFill = Color.FromArgb("#F3D9DD");
    private static readonly Color PositiveFill = Color.FromArgb("#DDEDE4");

    public SimulatorChartSeries Series { get; set; } =
        SimulatorChartSeries.Empty;

    /// <summary>Seçili dönemin indeksi; -1 ise seçim yok.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>
    /// Dokunulan x koordinatını en yakın döneme kancalar. Telefonda 12 noktadan
    /// birine tam basmak zor; kullanıcı grafiğin herhangi bir yerine basabilsin.
    /// </summary>
    public int IndexAt(float x, float width)
    {
        var count = Series.Points.Count;
        if (count == 0)
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        var plotWidth = Math.Max(1f, width - LeftPadding - RightPadding);
        var ratio = (x - LeftPadding) / plotWidth;
        var index = (int)Math.Round(ratio * (count - 1));
        return Math.Clamp(index, 0, count - 1);
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (!Series.HasData)
        {
            return;
        }

        var plot = new RectF(
            rect.X + LeftPadding,
            rect.Y + TopPadding,
            Math.Max(1f, rect.Width - LeftPadding - RightPadding),
            Math.Max(1f, rect.Height - TopPadding - BottomPadding));

        var (minimum, maximum) = PaddedRange();
        // Bantlar yalnızca eğri sıfırı kesiyorsa bilgi taşır; tek yönde kalan
        // seride tüm alanı boyamak gürültüden ibaret.
        if (Series.CrossesZero)
        {
            DrawBands(canvas, plot, minimum, maximum);
        }

        DrawAxis(canvas, plot, minimum, maximum);
        if (Series.HasScenario)
        {
            DrawLine(canvas, plot, minimum, maximum, BaselineColor, 2f, true);
            DrawLine(canvas, plot, minimum, maximum, ScenarioColor, 3f, false);
        }
        else
        {
            DrawLine(canvas, plot, minimum, maximum, ScenarioColor, 3f, false);
        }

        DrawSelection(canvas, plot, minimum, maximum);
        DrawMonthLabels(canvas, plot, rect);
    }

    private void DrawSelection(
        ICanvas canvas,
        RectF plot,
        float minimum,
        float maximum)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Series.Points.Count)
        {
            return;
        }

        var point = Series.Points[SelectedIndex];
        var x = IndexToX(SelectedIndex, plot, Series.Points.Count);
        canvas.StrokeColor = ZeroLineColor.WithAlpha(0.45f);
        canvas.StrokeSize = 1f;
        canvas.StrokeDashPattern = [4f, 3f];
        canvas.DrawLine(x, plot.Y, x, plot.Bottom);
        canvas.StrokeDashPattern = null;

        var value = Series.HasScenario ? point.Scenario : point.Baseline;
        var y = ValueToY(value, plot, minimum, maximum);
        canvas.FillColor = Colors.White;
        canvas.FillCircle(x, y, 6f);
        canvas.FillColor = ScenarioColor;
        canvas.FillCircle(x, y, 4f);
    }

    /// <summary>
    /// Uçlar kenara yapışmasın diye aralık biraz genişletilir; sıfır her
    /// koşulda içeride kalır.
    /// </summary>
    private (float Minimum, float Maximum) PaddedRange()
    {
        var minimum = (float)Series.Minimum;
        var maximum = (float)Series.Maximum;
        if (Math.Abs(maximum - minimum) < 1f)
        {
            // Düz çizgi: ölçek çökmesin diye yapay bir aralık verilir.
            maximum = minimum + 1f;
        }

        var padding = (maximum - minimum) * 0.08f;
        return (minimum - padding, maximum + padding);
    }

    private static void DrawBands(
        ICanvas canvas,
        RectF plot,
        float minimum,
        float maximum)
    {
        var zeroY = ValueToY(0m, plot, minimum, maximum);
        if (maximum > 0f)
        {
            canvas.FillColor = PositiveFill.WithAlpha(0.35f);
            canvas.FillRectangle(
                plot.X,
                plot.Y,
                plot.Width,
                Math.Max(0f, zeroY - plot.Y));
        }

        if (minimum < 0f)
        {
            canvas.FillColor = NegativeFill.WithAlpha(0.35f);
            canvas.FillRectangle(
                plot.X,
                zeroY,
                plot.Width,
                Math.Max(0f, plot.Bottom - zeroY));
        }
    }

    private void DrawAxis(
        ICanvas canvas,
        RectF plot,
        float minimum,
        float maximum)
    {
        canvas.FontSize = 10f;
        canvas.FontColor = LabelColor;

        var drawnLabelYs = new List<float>();
        foreach (var value in TickValues())
        {
            var y = ValueToY(value, plot, minimum, maximum);
            var isZero = value == 0m;
            canvas.StrokeColor = isZero ? ZeroLineColor : GridColor;
            canvas.StrokeSize = isZero ? 1.5f : 1f;
            canvas.StrokeDashPattern = isZero ? null : [3f, 3f];
            canvas.DrawLine(plot.X, y, plot.Right, y);
            canvas.StrokeDashPattern = null;

            // Sıkışık ölçekte etiketler üst üste biniyordu; sıfır her zaman
            // yazılır, diğerleri yalnızca yer varsa.
            if (!isZero && drawnLabelYs.Any(other => Math.Abs(other - y) < 15f))
            {
                continue;
            }

            drawnLabelYs.Add(y);
            canvas.DrawString(
                ShortMoney(value),
                plot.X - LeftPadding + 2f,
                y - 7f,
                LeftPadding - 6f,
                14f,
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
        }
    }

    /// <summary>
    /// Tick'ler kenar boşluğu eklenmiş aralıktan değil, gerçek veriden
    /// üretilir. Aksi halde veride hiç bulunmayan değerler eksende görünür —
    /// tamamı artıda olan bir seride negatif etiketler çıkıyordu.
    /// </summary>
    private IReadOnlyList<decimal> TickValues()
    {
        var ticks = new List<decimal> { 0m };
        const int stepCount = 2;
        if (Series.Maximum > 0m)
        {
            for (var index = 1; index <= stepCount; index++)
            {
                ticks.Add(Series.Maximum * index / stepCount);
            }
        }

        if (Series.Minimum < 0m)
        {
            for (var index = 1; index <= stepCount; index++)
            {
                ticks.Add(Series.Minimum * index / stepCount);
            }
        }

        return ticks;
    }

    private void DrawLine(
        ICanvas canvas,
        RectF plot,
        float minimum,
        float maximum,
        Color color,
        float thickness,
        bool useBaselineValue)
    {
        if (Series.Points.Count == 1)
        {
            var only = Series.Points[0];
            var value = useBaselineValue ? only.Baseline : only.Scenario;
            canvas.FillColor = color;
            canvas.FillCircle(
                plot.Center.X,
                ValueToY(value, plot, minimum, maximum),
                thickness + 1f);
            return;
        }

        var path = new PathF();
        for (var index = 0; index < Series.Points.Count; index++)
        {
            var point = Series.Points[index];
            var value = useBaselineValue ? point.Baseline : point.Scenario;
            var x = IndexToX(index, plot, Series.Points.Count);
            var y = ValueToY(value, plot, minimum, maximum);
            if (index == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        canvas.StrokeColor = color;
        canvas.StrokeSize = thickness;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);
    }

    private void DrawMonthLabels(ICanvas canvas, RectF plot, RectF rect)
    {
        canvas.FontSize = 10f;
        canvas.FontColor = LabelColor;

        // Dar ekranda her ay sığmaz; etiketler seyreltilir ama ilk ve son
        // dönem her zaman yazılır.
        var count = Series.Points.Count;
        var maximumLabels = Math.Max(2, (int)(plot.Width / 34f));
        var step = Math.Max(1, (int)Math.Ceiling(count / (double)maximumLabels));
        for (var index = 0; index < count; index++)
        {
            if (index % step != 0 && index != count - 1)
            {
                continue;
            }

            var x = IndexToX(index, plot, count);
            canvas.DrawString(
                Series.Points[index].Label,
                x - 17f,
                rect.Bottom - BottomPadding + 6f,
                34f,
                14f,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
        }
    }

    private static float IndexToX(int index, RectF plot, int count) =>
        count <= 1
            ? plot.Center.X
            : plot.X + (plot.Width * index / (count - 1));

    private static float ValueToY(
        decimal value,
        RectF plot,
        float minimum,
        float maximum)
    {
        var range = maximum - minimum;
        if (Math.Abs(range) < float.Epsilon)
        {
            return plot.Center.Y;
        }

        var ratio = ((float)value - minimum) / range;
        return plot.Bottom - (plot.Height * ratio);
    }

    private static string ShortMoney(decimal value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 1_000_000m)
        {
            return (value / 1_000_000m).ToString("0.#", TurkishCulture) + "M";
        }

        if (absolute >= 1_000m)
        {
            return (value / 1_000m).ToString("0.#", TurkishCulture) + "B";
        }

        return value.ToString("0", TurkishCulture);
    }
}
