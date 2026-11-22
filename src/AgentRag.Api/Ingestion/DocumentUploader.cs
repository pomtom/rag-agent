// Accepts ONE uploaded file: checks it, and saves it as "Pending" for the background service.
//
// It does NOT read, chunk or embed the file: that can take a while (a big PDF = many embedding calls),
// so the upload returns at once and IngestionWorker does the slow part in the background.

using System.Security.Cryptography;
using AgentRag.Api.Database;

namespace AgentRag.Api.Ingestion;

public sealed class DocumentUploader
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;   // 20 MB

    private readonly DocumentRepository _documents;

    public DocumentUploader(DocumentRepository documents)
    {
        _documents = documents;
    }

    public async Task<UploadResult> UploadAsync(string fileName, string? contentType, byte[] content, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!TextExtractor.IsSupported(extension))
        {
            return UploadResult.Rejected(fileName,
                "File type '" + extension + "' is not supported. Supported: " + string.Join(", ", TextExtractor.SupportedExtensions) + ".");
        }

        if (content.Length == 0)
        {
            return UploadResult.Rejected(fileName, "The file is empty.");
        }

        if (content.Length > MaxFileSizeBytes)
        {
            return UploadResult.Rejected(fileName, "The file is larger than " + (MaxFileSizeBytes / 1024 / 1024) + " MB.");
        }

        // Same bytes = same hash, whatever the file is called. Stops the same file being embedded twice.
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        int? existingId = await _documents.FindByHashAsync(hash, cancellationToken);
        if (existingId != null)
        {
            return UploadResult.Rejected(fileName, "This file was already uploaded (document " + existingId + ").");
        }

        int id = await _documents.InsertAsync(Path.GetFileName(fileName), extension, contentType, hash, content, cancellationToken);
        return UploadResult.Accepted(fileName, id);
    }
}
