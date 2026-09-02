using CoinFlow.Application.Abstractions;
using UglyToad.PdfPig;
using System.Text;

namespace CoinFlow.Infrastructure.Imports;

public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    private const int MaximumPageCount = 100;
    private const int MaximumExtractedCharacters = 2_000_000;

    public Task<string> ExtractTextAsync(
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocument.Open(pdf);
        var text = new StringBuilder();
        var pageCount = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++pageCount > MaximumPageCount ||
                text.Length >= MaximumExtractedCharacters)
            {
                break;
            }

            var remaining = MaximumExtractedCharacters - text.Length;
            var pageText = page.Text;
            text.Append(pageText.AsSpan(0, Math.Min(pageText.Length, remaining)));
            text.AppendLine();
        }

        return Task.FromResult(text.ToString());
    }
}
