// .csv files: one block of text per row, with the column names repeated in every row.
//
//   Name,Price,Stock            Name: Kettle | Price: 29.99 | Stock: 12
//   Kettle,29.99,12      ->
//   Toaster,49.00,0             Name: Toaster | Price: 49.00 | Stock: 0
//
// WHY: a chunk may hold rows from the middle of the file. Without the column names next to the
// values, "Toaster, 49.00, 0" means nothing to the model.
// Rows are separated by a blank line, so the chunker never cuts a row in half.
//
// Handles "quoted, values" and "" inside quotes. A quoted value that spans several lines is not supported.

using System.Text;

namespace AgentRag.Api.Ingestion.Readers;

public sealed class CsvReader
{
    private readonly PlainTextReader _plainTextReader;

    public CsvReader(PlainTextReader plainTextReader)
    {
        _plainTextReader = plainTextReader;
    }

    public string Read(byte[] content)
    {
        string text = _plainTextReader.Read(content);
        string[] lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        List<string> headers = ParseLine(lines[0]);
        StringBuilder result = new StringBuilder();

        for (int i = 1; i < lines.Length; i++)
        {
            List<string> values = ParseLine(lines[i]);
            result.AppendLine(RowFormatter.Format(headers, values));
            result.AppendLine();
        }

        return result.ToString();
    }

    // Splits one CSV line on commas that are not inside "quotes".
    private static List<string> ParseLine(string line)
    {
        List<string> values = new List<string>();
        StringBuilder current = new StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"' && insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');   // "" inside quotes = one " character
                i++;
            }
            else if (c == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if (c == ',' && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
