// The chat bot: one question in, one answer (with sources) out.
//
//   1. Messages   system prompt + the earlier conversation + the new question
//   2. Tools      search_documents (our RAG search), load_skill (skills), tools of other MCP servers
//   3. The model  gpt-5-mini decides which tools to call; UseFunctionInvocation (see AiClientFactory)
//                 runs them and sends the results back until the model writes its answer
//   4. Answer     the text + the chunks the search returned + which tools were called
//
// RAG here is "agentic": the MODEL decides when and what to search (and can search twice),
// instead of the code always searching once with the raw question.

using AgentRag.Api.Mcp;
using AgentRag.Api.Search;
using AgentRag.Api.Skills;
using Microsoft.Extensions.AI;

namespace AgentRag.Api.Chat;

public sealed class ChatService
{
    private const string SearchToolName = "search_documents";

    private readonly IChatClient _chatClient;
    private readonly DocumentSearch _search;
    private readonly SkillLoader _skills;
    private readonly McpToolLoader _mcpTools;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IChatClient chatClient, DocumentSearch search, SkillLoader skills, McpToolLoader mcpTools, ILogger<ChatService> logger)
    {
        _chatClient = chatClient;
        _search = search;
        _skills = skills;
        _mcpTools = mcpTools;
        _logger = logger;
    }

    public async Task<ChatAnswer> AskAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // The sources of THIS answer. The search tool below adds to it every time the model calls it.
        List<SearchResult> sources = new List<SearchResult>();

        // 1. Messages.
        List<ChatMessage> messages = new List<ChatMessage>();
        messages.Add(new ChatMessage(ChatRole.System, SystemPrompt.Text));
        foreach (ChatTurn turn in request.History)
        {
            ChatRole role = turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, turn.Text));
        }

        messages.Add(new ChatMessage(ChatRole.User, request.Message));

        // 2. Tools. A local function, so it can add to "sources" of this request.
        async Task<string> SearchDocumentsAsync(
            [System.ComponentModel.Description("What to look for, in natural language. Rephrase the question into a clear search query.")] string query)
        {
            List<SearchResult> results = await _search.SearchAsync(query, DocumentSearch.DefaultTop, cancellationToken);
            sources.AddRange(results);
            return DocumentSearch.FormatForModel(results);
        }

        List<AITool> tools = new List<AITool>();
        tools.Add(AIFunctionFactory.Create(SearchDocumentsAsync, SearchToolName,
            "Searches the uploaded documents and returns the most relevant passages, each labelled with its source like [file.md#2]."));

        AITool? loadSkill = _skills.CreateLoadSkillTool();
        if (loadSkill != null)
        {
            tools.Add(loadSkill);
        }

        foreach (AITool mcpTool in await _mcpTools.GetToolsAsync(cancellationToken))
        {
            // Our own tools win if an MCP server offers a tool with the same name (e.g. our own /mcp).
            if (tools.Any(tool => tool.Name == mcpTool.Name))
            {
                _logger.LogDebug("Skipping MCP tool {Tool}: a tool with that name already exists.", mcpTool.Name);
                continue;
            }

            tools.Add(mcpTool);
        }

        // 3. Ask the model (tool calls happen inside this one call).
        ChatOptions options = new ChatOptions { Tools = tools };
        ChatResponse response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);

        // 4. The answer.
        return new ChatAnswer
        {
            Answer = response.Text,
            Sources = ToChatSources(sources),
            ToolCalls = GetToolCalls(response)
        };
    }

    // The same chunk can be found by two searches: keep it once, best match first.
    private static List<ChatSource> ToChatSources(List<SearchResult> results)
    {
        return results
            .GroupBy(result => result.Source)
            .Select(group => group.OrderBy(result => result.Distance).First())
            .OrderBy(result => result.Distance)
            .Select(result => new ChatSource
            {
                FileName = result.FileName,
                ChunkIndex = result.ChunkIndex,
                Source = result.Source,
                Content = result.Content,
                Distance = result.Distance
            })
            .ToList();
    }

    // The response contains every message of the tool-calling round trips; pick out the calls.
    private static List<string> GetToolCalls(ChatResponse response)
    {
        List<string> calls = new List<string>();
        foreach (ChatMessage message in response.Messages)
        {
            foreach (FunctionCallContent call in message.Contents.OfType<FunctionCallContent>())
            {
                string arguments = call.Arguments == null
                    ? string.Empty
                    : string.Join(", ", call.Arguments.Select(argument => argument.Key + ": " + argument.Value));
                calls.Add(call.Name + "(" + arguments + ")");
            }
        }

        return calls;
    }
}
