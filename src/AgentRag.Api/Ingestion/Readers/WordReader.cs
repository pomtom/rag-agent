// .docx files (Word 2007 and later), using Microsoft's Open XML SDK.
//
// A .docx is a zip of XML files. We walk the document body in order:
//   paragraph -> its text, followed by a blank line (so the chunker keeps paragraphs together)
//   table     -> one line per row, cells separated by " | "
// The old binary .doc format is not supported.

using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AgentRag.Api.Ingestion.Readers;

public sealed class WordReader
{
    public string Read(byte[] content)
    {
        StringBuilder text = new StringBuilder();

        using (MemoryStream stream = new MemoryStream(content))
        using (WordprocessingDocument document = WordprocessingDocument.Open(stream, false))
        {
            Body? body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                return string.Empty;
            }

            foreach (OpenXmlElement element in body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    string line = paragraph.InnerText.Trim();
                    if (line.Length > 0)
                    {
                        text.AppendLine(line);
                        text.AppendLine();
                    }
                }
                else if (element is Table table)
                {
                    foreach (TableRow row in table.Elements<TableRow>())
                    {
                        IEnumerable<string> cells = row.Elements<TableCell>().Select(cell => cell.InnerText.Trim());
                        text.AppendLine(string.Join(" | ", cells));
                    }

                    text.AppendLine();
                }
            }
        }

        return text.ToString();
    }
}
