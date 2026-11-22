// One chunk found by a search, with where it came from (for citations).

namespace AgentRag.Api.Search;

public sealed class SearchResult
{
    public int DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Cosine distance: 0 = same meaning. Smaller is better.</summary>
    public double Distance { get; set; }

    /// <summary>A label the model can cite, e.g. "return-policy.md#2".</summary>
    public string Source
    {
        get { return FileName + "#" + ChunkIndex; }
    }
}
