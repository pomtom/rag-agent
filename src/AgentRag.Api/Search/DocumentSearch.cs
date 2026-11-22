// Question -> the most relevant chunks. Used by the chat bot AND by our MCP server.
//
//   "How long do I have to return a kettle?"
//      --embedding model--> [0.01, -0.2, ...]                 the meaning of the question as numbers
//      --ChunkRepository--> the 5 chunks with the closest meaning (cosine distance)

using System.Globalization;
using System.Text;
using AgentRag.Api.Database;
using Microsoft.Extensions.AI;

namespace AgentRag.Api.Search;

public sealed class DocumentSearch
{
    public const int DefaultTop = 5;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ChunkRepository _chunks;

    public DocumentSearch(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, ChunkRepository chunks)
    {
        _embeddingGenerator = embeddingGenerator;
        _chunks = chunks;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int top, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<float> queryVector = await _embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);
        return await _chunks.SearchAsync(queryVector, top, cancellationToken);
    }

    /// <summary>
    /// The search results as plain text for a model, each chunk labelled with its source,
    /// so the model can cite it: [return-policy.md#2]
    /// </summary>
    public static string FormatForModel(List<SearchResult> results)
    {
        if (results.Count == 0)
        {
            return "No matching passages found in the uploaded documents.";
        }

        StringBuilder text = new StringBuilder();
        foreach (SearchResult result in results)
        {
            text.Append('[').Append(result.Source).Append("] (distance ")
                .Append(result.Distance.ToString("0.000", CultureInfo.InvariantCulture)).AppendLine(")");
            text.AppendLine(result.Content);
            text.AppendLine();
        }

        return text.ToString();
    }
}
