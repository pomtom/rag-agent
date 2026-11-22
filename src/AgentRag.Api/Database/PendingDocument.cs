// A file waiting for ingestion: just what the background service needs to process it.

namespace AgentRag.Api.Database;

public sealed class PendingDocument
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public byte[] FileContent { get; set; } = Array.Empty<byte>();
}
