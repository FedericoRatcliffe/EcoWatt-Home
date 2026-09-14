using System.Text;
using EcoWattCasa.Application.Billing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EcoWattCasa.Infrastructure.Billing;

/// <summary>
/// Saca el texto de la factura con PdfPig.
///
/// Se usa ContentOrderTextExtractor en vez de page.Text porque la factura tiene dos columnas:
/// el extractor crudo entrelaza los renglones de ambas y los conceptos quedan separados de
/// sus importes.
/// </summary>
public sealed class PdfBillTextExtractor : IBillTextExtractor
{
    public Task<string> ExtractTextAsync(Stream pdf, CancellationToken ct = default)
    {
        // PdfPig necesita un stream con seek; el de un form-data no siempre lo es.
        using var buffer = new MemoryStream();
        pdf.CopyTo(buffer);
        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
        }

        return Task.FromResult(text.ToString());
    }
}
