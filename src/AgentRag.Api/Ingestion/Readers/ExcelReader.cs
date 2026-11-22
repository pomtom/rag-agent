// .xlsx files (Excel 2007 and later), using Microsoft's Open XML SDK.
//
// Every sheet becomes text; the FIRST row of a sheet is treated as the column names,
// and every other row is written like the CSV reader does:
//
//   Sheet: Products
//
//   Name: Kettle | Price: 29.99 | Stock: 12
//
//   Name: Toaster | Price: 49 | Stock: 0
//
// Formulas give their last calculated value (what Excel saved), not the formula itself.

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AgentRag.Api.Ingestion.Readers;

public sealed class ExcelReader
{
    public string Read(byte[] content)
    {
        StringBuilder text = new StringBuilder();

        using (MemoryStream stream = new MemoryStream(content))
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false))
        {
            WorkbookPart? workbookPart = document.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets == null)
            {
                return string.Empty;
            }

            // Excel stores most text once, in a "shared strings" table, and cells point to it by number.
            SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

            foreach (Sheet sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                if (sheet.Id?.Value == null)
                {
                    continue;
                }

                WorksheetPart worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                text.AppendLine("Sheet: " + sheet.Name);
                text.AppendLine();
                AppendRows(worksheetPart, sharedStrings, text);
            }
        }

        return text.ToString();
    }

    private static void AppendRows(WorksheetPart worksheetPart, SharedStringTable? sharedStrings, StringBuilder text)
    {
        List<string>? headers = null;

        foreach (Row row in worksheetPart.Worksheet.Descendants<Row>())
        {
            List<string> values = ReadRow(row, sharedStrings);
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (headers == null)
            {
                headers = values;   // the first non-empty row = column names
                continue;
            }

            text.AppendLine(RowFormatter.Format(headers, values));
            text.AppendLine();
        }
    }

    // The cell values of one row, by column position. Empty cells are not stored in the file,
    // so the column letters ("C" in "C7") tell us where each value belongs.
    private static List<string> ReadRow(Row row, SharedStringTable? sharedStrings)
    {
        List<string> values = new List<string>();

        foreach (Cell cell in row.Elements<Cell>())
        {
            int column = GetColumnIndex(cell.CellReference?.Value);
            while (values.Count < column)
            {
                values.Add(string.Empty);
            }

            values.Add(GetCellText(cell, sharedStrings));
        }

        return values;
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        string value = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null && int.TryParse(value, out int index))
        {
            return sharedStrings.ElementAt(index).InnerText;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        return value;   // numbers (dates are stored as numbers too)
    }

    // "A1" -> 0, "B7" -> 1, "AA3" -> 26. Without a reference, assume "the next column".
    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        int column = 0;
        foreach (char c in cellReference)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            column = column * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return column - 1;
    }
}
