// .txt and .md files: they are already text, we only need to decode the bytes.
//
// Markdown is kept as it is: the # headings and lists help the model understand the structure.

using System.Text;

namespace AgentRag.Api.Ingestion.Readers;

public sealed class PlainTextReader
{
    public string Read(byte[] content)
    {
        // UTF-8 unless the file starts with a byte order mark that says otherwise (UTF-16, ...).
        using (MemoryStream stream = new MemoryStream(content))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            return reader.ReadToEnd();
        }
    }
}
