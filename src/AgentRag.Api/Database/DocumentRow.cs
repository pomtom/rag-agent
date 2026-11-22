// One row of dbo.Documents, WITHOUT the file content (that can be megabytes).
// Also the JSON returned by GET /api/documents.

namespace AgentRag.Api.Database;

public sealed class DocumentRow
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string Status { get; set; } = DocumentStatus.Pending;

    public int ChunkCount { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
