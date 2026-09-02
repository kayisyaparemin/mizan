using CoinFlow.Application.Abstractions;
using UglyToad.PdfPig;
using System.Text;

namespace CoinFlow.Infrastructure.Imports;

public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    private const int MaximumExtractedCharacters = 256_000;

    public Task<string> ExtractTextAsync(
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = PdfDocument.Open(pdf);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.NumberOfPages == 0)
        {
            return Task.FromResult(string.Empty);
        }

        // Statement header fields live on page one. Reading every transaction
        // page made malformed/encoded PDFs an unbounded CPU and allocation path.
        var pageText = document.GetPage(1).Text;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(pageText.Length <= MaximumExtractedCharacters
            ? pageText
            : pageText[..MaximumExtractedCharacters]);
    }
}
