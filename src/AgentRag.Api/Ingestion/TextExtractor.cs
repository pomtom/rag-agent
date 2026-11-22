// STEP 1 of ingestion: a file -> plain text.
//
// Picks the right reader by file extension. Everything after this step only sees plain text,
// so the chunker and the embeddings never care about the file type.
//
// To support a new file type: write a small reader class, register it in DependencyInjection.cs,
// add it to the switch below and to SupportedExtensions.

using AgentRag.Api.Ingestion.Readers;

namespace AgentRag.Api.Ingestion;

public sealed class TextExtractor
{
    public static readonly string[] SupportedExtensions = new string[] { ".txt", ".md", ".csv", ".pdf", ".docx", ".xlsx" };

    private readonly PlainTextReader _plainTextReader;
    private readonly CsvReader _csvReader;
    private readonly PdfReader _pdfReader;
    private readonly WordReader _wordReader;
    private readonly ExcelReader _excelReader;

    public TextExtractor(
        PlainTextReader plainTextReader, CsvReader csvReader, PdfReader pdfReader, WordReader wordReader, ExcelReader excelReader)
    {
        _plainTextReader = plainTextReader;
        _csvReader = csvReader;
        _pdfReader = pdfReader;
        _wordReader = wordReader;
        _excelReader = excelReader;
    }

    public static bool IsSupported(string extension)
    {
        return SupportedExtensions.Contains(extension.ToLowerInvariant());
    }

    public string Extract(string extension, byte[] content)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".txt":
            case ".md":
                return _plainTextReader.Read(content);
            case ".csv":
                return _csvReader.Read(content);
            case ".pdf":
                return _pdfReader.Read(content);
            case ".docx":
                return _wordReader.Read(content);
            case ".xlsx":
                return _excelReader.Read(content);
            default:
                throw new NotSupportedException(
                    "File type '" + extension + "' is not supported. Supported: " + string.Join(", ", SupportedExtensions) + ".");
        }
    }
}
