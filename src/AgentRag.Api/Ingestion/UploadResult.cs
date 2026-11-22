// What happened to one uploaded file. Returned as JSON by POST /api/documents.

namespace AgentRag.Api.Ingestion;

public sealed class UploadResult
{
    public string FileName { get; set; } = string.Empty;

    public bool IsAccepted { get; set; }

    /// <summary>The new row in dbo.Documents (only when accepted).</summary>
    public int? DocumentId { get; set; }

    public string Message { get; set; } = string.Empty;

    public static UploadResult Accepted(string fileName, int documentId)
    {
        return new UploadResult
        {
            FileName = fileName,
            IsAccepted = true,
            DocumentId = documentId,
            Message = "Uploaded. Embeddings will be generated in the background."
        };
    }

    public static UploadResult Rejected(string fileName, string reason)
    {
        return new UploadResult
        {
            FileName = fileName,
            IsAccepted = false,
            Message = reason
        };
    }
}
