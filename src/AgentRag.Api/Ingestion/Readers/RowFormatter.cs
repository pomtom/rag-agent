// Turns one table row into text the model understands: "Header: value | Header: value".
// Shared by CsvReader and ExcelReader. Empty values are left out.

namespace AgentRag.Api.Ingestion.Readers;

public static class RowFormatter
{
    public static string Format(List<string> headers, List<string> values)
    {
        List<string> parts = new List<string>();

        for (int column = 0; column < values.Count; column++)
        {
            if (string.IsNullOrWhiteSpace(values[column]))
            {
                continue;
            }

            string header = column < headers.Count && headers[column].Trim().Length > 0
                ? headers[column].Trim()
                : "Column " + (column + 1);

            parts.Add(header + ": " + values[column].Trim());
        }

        return string.Join(" | ", parts);
    }
}
