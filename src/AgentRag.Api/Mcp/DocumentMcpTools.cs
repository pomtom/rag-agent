// MCP SERVER: publishes our document search at /mcp, so OTHER clients can use it -
// VS Code / GitHub Copilot, Claude, another agent, ... (registered in DependencyInjection.cs, mapped in Program.cs).
//
// Try it in VS Code: add to .vscode/mcp.json
//   { "servers": { "agent-rag": { "type": "http", "url": "https://localhost:7300/mcp" } } }

using System.ComponentModel;
using AgentRag.Api.Search;
using ModelContextProtocol.Server;

namespace AgentRag.Api.Mcp;

[McpServerToolType]
public sealed class DocumentMcpTools
{
    private readonly DocumentSearch _search;

    public DocumentMcpTools(DocumentSearch search)
    {
        _search = search;
    }

    [McpServerTool(Name = "search_documents", ReadOnly = true)]
    [Description("Searches the uploaded documents and returns the most relevant passages, each labelled with its source like [file.md#2].")]
    public async Task<string> SearchDocumentsAsync(
        [Description("What to look for, in natural language.")] string query,
        CancellationToken cancellationToken)
    {
        List<SearchResult> results = await _search.SearchAsync(query, DocumentSearch.DefaultTop, cancellationToken);
        return DocumentSearch.FormatForModel(results);
    }
}
