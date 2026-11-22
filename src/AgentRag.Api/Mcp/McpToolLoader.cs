// MCP CLIENT: connects to the servers in "Mcp:Servers" and returns their tools for the chat bot.
//
// An MCP tool looks to the chat model exactly like our own tools (it is an AIFunction).
// When the model calls it, the call goes over HTTP to that MCP server.
//
// One connection per server, opened on first use and reused. A server that cannot be reached is
// skipped with a warning (and tried again on the next question), so the chat bot keeps working.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AgentRag.Api.Mcp;

public sealed class McpToolLoader : IAsyncDisposable
{
    private readonly McpOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpToolLoader> _logger;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly Dictionary<string, McpClient> _clients = new Dictionary<string, McpClient>(StringComparer.OrdinalIgnoreCase);

    public McpToolLoader(IOptions<McpOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpToolLoader>();
    }

    /// <summary>The tools of every configured MCP server that is reachable right now.</summary>
    public async Task<List<AITool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        List<AITool> tools = new List<AITool>();

        foreach (KeyValuePair<string, string> server in _options.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Value))
            {
                continue;
            }

            try
            {
                McpClient client = await GetClientAsync(server.Key, server.Value, cancellationToken);
                IList<McpClientTool> serverTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                tools.AddRange(serverTools);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MCP server '{Server}' ({Url}) is not available; continuing without its tools.", server.Key, server.Value);
                await ForgetClientAsync(server.Key);
            }
        }

        return tools;
    }

    private async Task<McpClient> GetClientAsync(string name, string url, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            McpClient? existing;
            if (_clients.TryGetValue(name, out existing))
            {
                return existing;
            }

            HttpClientTransportOptions transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(url),
                Name = name
            };

            HttpClientTransport transport = new HttpClientTransport(transportOptions, _loggerFactory);
            McpClient client = await McpClient.CreateAsync(transport, null, _loggerFactory, cancellationToken);

            _clients[name] = client;
            _logger.LogInformation("Connected to MCP server '{Server}' at {Url}.", name, url);
            return client;
        }
        finally
        {
            _lock.Release();
        }
    }

    // A broken connection is thrown away, so the next question opens a fresh one.
    private async Task ForgetClientAsync(string name)
    {
        await _lock.WaitAsync();
        try
        {
            McpClient? client;
            if (_clients.Remove(name, out client))
            {
                await client.DisposeAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpClient client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
        _lock.Dispose();
    }
}
