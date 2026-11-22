// THE ONLY PLACE where services are registered.
//
// If you want to know "where does X come from?", the answer is in this file.
// Every class is registered as itself (no interfaces) because each one has exactly one implementation.
// Only the two AI clients use interfaces - the standard Microsoft.Extensions.AI ones.
//
// Everything is a Singleton (one instance for the whole app): the classes keep no per-request state,
// so one instance can safely serve every request and the background service at the same time.

using AgentRag.Api.Ai;
using AgentRag.Api.Chat;
using AgentRag.Api.Database;
using AgentRag.Api.Ingestion;
using AgentRag.Api.Ingestion.Readers;
using AgentRag.Api.Mcp;
using AgentRag.Api.Search;
using AgentRag.Api.Skills;
using Microsoft.Extensions.AI;
using ModelContextProtocol.AspNetCore;

namespace AgentRag.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddRagApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Settings (appsettings.json sections -> C# classes).
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        // 2. AI models: ONE factory builds both clients from the "Ai" section (Foundry or OpenAI).
        //    The rest of the app only sees the standard Microsoft.Extensions.AI interfaces.
        services.AddSingleton<AiClientFactory>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<AiClientFactory>().CreateChatClient());
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp => sp.GetRequiredService<AiClientFactory>().CreateEmbeddingGenerator());

        // 3. Database (SQL Server, plain SQL).
        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<DocumentRepository>();
        services.AddSingleton<ChunkRepository>();

        // 4. Ingestion: upload saves the file as Pending; a background service turns it into chunks + vectors.
        services.AddSingleton<DocumentUploader>();
        services.AddSingleton<PlainTextReader>();
        services.AddSingleton<CsvReader>();
        services.AddSingleton<PdfReader>();
        services.AddSingleton<WordReader>();
        services.AddSingleton<ExcelReader>();
        services.AddSingleton<TextExtractor>();
        services.AddSingleton<TextChunker>();
        services.AddSingleton<DocumentIngestor>();
        services.AddHostedService<IngestionWorker>();

        // 5. Search: question -> vector -> closest chunks. Used by the chat bot AND the MCP server.
        services.AddSingleton<DocumentSearch>();

        // 6. Chat bot: model + tools (search_documents, load_skill, tools from other MCP servers).
        services.AddSingleton<SkillLoader>();
        services.AddSingleton<McpToolLoader>();
        services.AddSingleton<ChatService>();

        // 7. Our own MCP server at /mcp. Stateless: every request stands alone, nothing to lose on restart.
        services.AddMcpServer()
            .WithHttpTransport(ConfigureMcpTransport)
            .WithTools<DocumentMcpTools>();

        return services;
    }

    private static void ConfigureMcpTransport(HttpServerTransportOptions options)
    {
        options.Stateless = true;
    }
}
