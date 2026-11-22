// The "Mcp" section of appsettings.json: OTHER MCP servers whose tools the chat bot may use.
//
//   "Mcp": {
//     "Servers": {
//       "learn": "https://learn.microsoft.com/api/mcp"
//     }
//   }
//
// Name -> address of the server (Streamable HTTP). Empty = the chat bot uses only its own tools.

namespace AgentRag.Api.Mcp;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public Dictionary<string, string> Servers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
