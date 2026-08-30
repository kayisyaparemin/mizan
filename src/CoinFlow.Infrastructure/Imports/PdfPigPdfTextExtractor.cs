using CoinFlow.Application.Abstractions;
using UglyToad.PdfPig;

namespace CoinFlow.Infrastructure.Imports;

public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    public Task<string> ExtractTextAsync(
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocument.Open(pdf);
        var text = string.Join(
            Environment.NewLine,
            document.GetPages().Select(page => page.Text));
        return Task.FromResult(text);
    }
}
