// The JSON the API sends and receives, as seen from the web app.
//
// The web app has its OWN copies of these small classes instead of referencing the API project:
// the JSON is the contract, so either side can change its internals freely.

namespace AgentRag.Web;

public sealed class DocumentDto
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>Pending, Processing, Completed or Failed.</summary>
    public string Status { get; set; } = string.Empty;

    public int ChunkCount { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

public sealed class UploadResultDto
{
    public string FileName { get; set; } = string.Empty;

    public bool IsAccepted { get; set; }

    public int? DocumentId { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class ChatRequestDto
{
    public string Message { get; set; } = string.Empty;

    public List<ChatTurnDto> History { get; set; } = new List<ChatTurnDto>();
}

public sealed class ChatTurnDto
{
    public string Role { get; set; } = "user";

    public string Text { get; set; } = string.Empty;
}

public sealed class ChatAnswerDto
{
    public string Answer { get; set; } = string.Empty;

    public List<ChatSourceDto> Sources { get; set; } = new List<ChatSourceDto>();

    public List<string> ToolCalls { get; set; } = new List<string>();
}

public sealed class ChatSourceDto
{
    public string FileName { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public double Distance { get; set; }
}
