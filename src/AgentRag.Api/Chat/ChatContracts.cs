// The JSON of POST /api/chat - the contract between the web app and the API.
//
//   request   { "message": "...", "history": [ { "role": "user", "text": "..." }, { "role": "assistant", "text": "..." } ] }
//   response  { "answer": "...", "sources": [ ... ], "toolCalls": [ "search_documents" ] }
//
// The web app keeps the conversation and sends it with every question: the API stores no chat state.

namespace AgentRag.Api.Chat;

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;

    public List<ChatTurn> History { get; set; } = new List<ChatTurn>();
}

public sealed class ChatTurn
{
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";

    public string Text { get; set; } = string.Empty;
}

public sealed class ChatAnswer
{
    public string Answer { get; set; } = string.Empty;

    /// <summary>The chunks the search tool gave the model while answering.</summary>
    public List<ChatSource> Sources { get; set; } = new List<ChatSource>();

    /// <summary>The tools the model called, in order - handy to SEE how the bot worked.</summary>
    public List<string> ToolCalls { get; set; } = new List<string>();
}

public sealed class ChatSource
{
    public string FileName { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public double Distance { get; set; }
}
