// .pdf files, using PdfPig (open source, pure .NET), page by page.
//
// A scanned PDF is only pictures and has no text inside: it gives back an empty string
// (it would need OCR first, e.g. Azure AI Document Intelligence).

using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AgentRag.Api.Ingestion.Readers;

public sealed class PdfReader
{
    public string Read(byte[] content)
    {
        StringBuilder text = new StringBuilder();

        using (PdfDocument pdf = PdfDocument.Open(content))
        {
            foreach (Page page in pdf.GetPages())
            {
                // Keeps the reading order and the spaces between words.
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
                text.AppendLine();
            }
        }

        return text.ToString();
    }
}
