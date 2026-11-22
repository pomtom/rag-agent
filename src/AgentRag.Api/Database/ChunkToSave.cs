// One chunk ready to be written to dbo.DocumentChunks: its text and its embedding vector.

namespace AgentRag.Api.Database;

public sealed class ChunkToSave
{
    public ChunkToSave(int chunkIndex, string content, ReadOnlyMemory<float> embedding)
    {
        ChunkIndex = chunkIndex;
        Content = content;
        Embedding = embedding;
    }

    public int ChunkIndex { get; }

    public string Content { get; }

    public ReadOnlyMemory<float> Embedding { get; }
}
