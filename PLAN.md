# Agent RAG — Requirements & Build Plan

## 1. The request, rewritten

Build a **very simple, easy-to-read RAG (Retrieval-Augmented Generation) application in C# / .NET 10**.

**What it does**
1. A user uploads files (`.txt`, `.md`, `.csv`, `.pdf`, `.docx`, `.xlsx`) from a web page.
2. A **background service** picks up each new file, extracts its text, splits it into chunks,
   creates embeddings with **`text-embedding-3-small`** (Microsoft Foundry), and saves them in a vector store.
3. A user chats with a **chat bot** in the web page. The bot answers from the uploaded documents
   using **`gpt-5-mini`** (Microsoft Foundry) and shows which files the answer came from.

**How it is built**
- **.NET Aspire** runs everything locally (AppHost + dashboard). **No Docker** — it is not installed / not allowed.
- **Web UI (Blazor)** → sends every request to → **Web API**. The UI has no AI code and no database code.
- **SQL Server 2025 (already installed locally)** is the vector store, using its native `VECTOR(1536)` type.
  - Table 1 `Documents`: one row per uploaded file + its ingestion status (was it embedded or not?).
  - Table 2 `DocumentChunks`: the text chunks and their vectors.
- **Microsoft.Extensions.AI** (`IChatClient`, `IEmbeddingGenerator`) is the only AI abstraction.
  Providers supported: **Microsoft Foundry** (main) and **OpenAI** (switch in `appsettings.json`).
- The chat bot can also use **Skills** (`SKILL.md` files) and **MCP server tools**.

**Code style rules (the main goal)**
- Simple, readable C# a new developer can follow top to bottom. A short "why" comment at the top of each file.
- **Single Responsibility**: each class does one job (read a PDF, chunk text, save chunks, …).
- **No extra projects** for factories, providers, embeddings, abstractions. Plain classes in folders.
- **All dependency registration in ONE place** (`DependencyInjection.cs` in the API).
- **One small model factory class** instead of provider interfaces + keyed services + factories.
- No interfaces unless there are really two implementations. No generic repositories, no MediatR, no ORM magic.

### What changes compared to `hosted-agent-playground`

| hosted-agent-playground | agent-rag (this project) |
|---|---|
| ~12 library projects (`Abstractions`, `Core`, `Providers`, `Rag`, `Skills`, `Tools`, …) | **4 projects** total |
| `IChatClientProvider` + `IEmbeddingProvider` + `ChatClientFactory` + `EmbeddingGeneratorFactory` + keyed DI | **1 class** `AiClientFactory` with a `switch` on `"Foundry"` / `"OpenAI"` |
| 5 providers (AzureOpenAI, FoundryProject, OpenAI, Ollama, FoundryLocal) | 2 providers: Foundry, OpenAI |
| Separate Foundry hosted-agent project + Agent Framework | Chat runs **inside the API** with plain `IChatClient` + function calling |
| PostgreSQL + pgvector in a Docker container | **Local SQL Server 2025**, no container |
| DI spread over many `Add...()` extension methods in many projects | **One** `AddRagApplication()` method |

---

## 2. Architecture

```
 Browser
   │
   ▼
 AgentRag.Web  (Blazor Server)          pages: Documents, Chat
   │  HttpClient "https+http://api"  (Aspire service discovery)
   ▼
 AgentRag.Api  (ASP.NET Core Web API)
   ├─ Endpoints      /api/documents, /api/chat
   ├─ IngestionWorker (BackgroundService)  ── polls Documents WHERE Status = 'Pending'
   │      read text → chunk → embed (text-embedding-3-small) → save chunks
   ├─ ChatService    question → search tool → gpt-5-mini → answer + sources
   │      tools: search_documents, load_skill, MCP tools
   └─ SQL Server 2025  (VECTOR(1536), VECTOR_DISTANCE)

 AgentRag.AppHost (Aspire) starts Api + Web, passes the SQL connection string
 and the Foundry settings. SQL Server is an EXISTING server (AddConnectionString), not a container.
```

### Ingestion flow (background)
1. `POST /api/documents` saves the file bytes + metadata in `Documents` with `Status = Pending`. Returns immediately.
2. `IngestionWorker` wakes up every 5 seconds, takes the oldest `Pending` row, sets `Processing`.
3. `TextExtractor` picks the right reader by file extension and returns plain text.
4. `TextChunker` splits the text (~2,000 characters, 200 overlap, breaking on paragraphs).
5. `IEmbeddingGenerator` (text-embedding-3-small) creates one 1,536-number vector per chunk (batched).
6. `ChunkRepository` inserts the chunks; `DocumentRepository` sets `Completed` + `ChunkCount` (or `Failed` + `ErrorMessage`).

Polling the table (instead of an in-memory queue) is simple, and nothing is lost if the app restarts.

### Chat flow
1. Web sends `POST /api/chat { message, history }`.
2. `ChatService` calls `gpt-5-mini` with a system prompt and the tools.
3. The model calls `search_documents(query)` → embed the query → `VECTOR_DISTANCE('cosine', …)` top 5 chunks.
4. The model answers using only those chunks and cites the file names. The API returns `{ answer, sources[] }`.
5. If a request matches a skill, the model calls `load_skill(name)` and follows that skill's instructions.
6. Tools from configured MCP servers are added to the same tool list.

---

## 3. Solution layout

> **Built (2026-11-22).** Small differences from this plan: `schema.sql` lives in `src/AgentRag.Api/Database/`,
> SKILL.md folders sit next to `Skills/SkillLoader.cs`, search moved to `Search/DocumentSearch.cs` (shared by chat and MCP),
> and there is a separate `CsvReader`. README.md has the final file guide.

```
agent-rag/
  AgentRag.slnx
  Directory.Build.props          net10.0, nullable, shared settings
  Directory.Packages.props       ALL NuGet versions in one place
  database/
    schema.sql                   the two tables (also run automatically at startup)
  sample-docs/                   a few files to try
  src/
    AgentRag.AppHost/
      Program.cs                 Api + Web + connection string + Foundry parameters
    AgentRag.ServiceDefaults/
      Extensions.cs              Aspire standard: health checks, OpenTelemetry, service discovery
    AgentRag.Api/
      Program.cs                 ~20 lines: AddRagApplication(), map endpoints, run
      DependencyInjection.cs     ★ the ONLY place services are registered
      appsettings.json
      Ai/
        AiOptions.cs             "Ai" config section (Provider, Endpoint, ApiKey, ChatModel, EmbeddingModel)
        AiClientFactory.cs       builds IChatClient + IEmbeddingGenerator for Foundry or OpenAI
      Database/
        DatabaseInitializer.cs   creates the tables if they do not exist
        DocumentRepository.cs    Documents table: insert, list, get next pending, set status, delete
        ChunkRepository.cs       DocumentChunks table: insert chunks, vector search
      Ingestion/
        IngestionWorker.cs       BackgroundService: the loop, nothing else
        DocumentIngestor.cs      one document: extract → chunk → embed → save
        TextExtractor.cs         picks a reader by extension
        Readers/
          PlainTextReader.cs     .txt .md .csv
          PdfReader.cs           .pdf  (PdfPig)
          WordReader.cs          .docx (DocumentFormat.OpenXml)
          ExcelReader.cs         .xlsx (DocumentFormat.OpenXml) — one line per row, "Sheet: col | col"
        TextChunker.cs
      Chat/
        ChatService.cs           builds the message list, calls the model, returns answer + sources
        SearchDocumentsTool.cs   the search_documents function the model can call
        SystemPrompt.cs          the instructions text
      Skills/
        SkillLoader.cs           reads skills/*/SKILL.md, exposes the load_skill tool
      Mcp/
        McpToolLoader.cs         connects to MCP servers from config, returns their tools
      Endpoints/
        DocumentEndpoints.cs     upload, list, delete, re-process
        ChatEndpoints.cs         chat
      skills/
        summarize-document/SKILL.md     (example skill)
    AgentRag.Web/
      Program.cs
      RagApiClient.cs            the ONLY class that talks to the API
      Components/Pages/Documents.razor   upload + status table (auto-refresh)
      Components/Pages/Chat.razor        chat window with sources
```

### The single DI place (`DependencyInjection.cs`), roughly

```csharp
public static IServiceCollection AddRagApplication(this IServiceCollection services, IConfiguration config)
{
    // Settings
    services.Configure<AiOptions>(config.GetSection("Ai"));

    // AI models (Microsoft.Extensions.AI)
    services.AddSingleton<AiClientFactory>();
    services.AddSingleton(sp => sp.GetRequiredService<AiClientFactory>().CreateChatClient());
    services.AddSingleton(sp => sp.GetRequiredService<AiClientFactory>().CreateEmbeddingGenerator());

    // Database
    services.AddSingleton<DatabaseInitializer>();
    services.AddSingleton<DocumentRepository>();
    services.AddSingleton<ChunkRepository>();

    // Ingestion
    services.AddSingleton<TextExtractor>();
    services.AddSingleton<TextChunker>();
    services.AddSingleton<DocumentIngestor>();
    services.AddHostedService<IngestionWorker>();

    // Chat
    services.AddSingleton<SkillLoader>();
    services.AddSingleton<McpToolLoader>();
    services.AddSingleton<SearchDocumentsTool>();
    services.AddScoped<ChatService>();

    return services;
}
```

### `AiClientFactory` — the whole "provider" story in one class

```csharp
// "Ai": { "Provider": "Foundry", "Endpoint": "https://<resource>.services.ai.azure.com",
//         "ApiKey": "", "ChatModel": "gpt-5-mini", "EmbeddingModel": "text-embedding-3-small" }
public IChatClient CreateChatClient() =>
    CreateOpenAIClient().GetChatClient(_options.ChatModel).AsIChatClient()
        .AsBuilder().UseFunctionInvocation().UseLogging(_loggerFactory).Build();

public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator() =>
    CreateOpenAIClient().GetEmbeddingClient(_options.EmbeddingModel).AsIEmbeddingGenerator();

private OpenAIClient CreateOpenAIClient()
{
    switch (_options.Provider)
    {
        case "Foundry": // Foundry exposes an OpenAI-compatible endpoint: <resource>/openai/v1/
            ...API key, or Entra ID (az login) when ApiKey is empty
        case "OpenAI":
            return new OpenAIClient(_options.ApiKey);
        default:
            throw new InvalidOperationException("Ai:Provider must be 'Foundry' or 'OpenAI'.");
    }
}
```

Both providers go through the same OpenAI SDK, so this is the only code that knows which provider is used.

---

## 4. Database (SQL Server 2025)

```sql
CREATE TABLE dbo.Documents
(
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    FileName      NVARCHAR(260)  NOT NULL,
    FileExtension NVARCHAR(10)   NOT NULL,          -- .pdf, .docx, ...
    ContentType   NVARCHAR(100)  NULL,
    FileSizeBytes BIGINT         NOT NULL,
    FileHash      CHAR(64)       NOT NULL,          -- SHA-256, to spot the same file uploaded twice
    FileContent   VARBINARY(MAX) NOT NULL,          -- the original file (worker reads it from here)
    Status        NVARCHAR(20)   NOT NULL DEFAULT 'Pending',  -- Pending | Processing | Completed | Failed
    ChunkCount    INT            NOT NULL DEFAULT 0,
    ErrorMessage  NVARCHAR(MAX)  NULL,
    UploadedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedAt   DATETIME2      NULL
);

CREATE TABLE dbo.DocumentChunks
(
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    DocumentId  INT            NOT NULL REFERENCES dbo.Documents(Id) ON DELETE CASCADE,
    ChunkIndex  INT            NOT NULL,            -- 0, 1, 2 ... order inside the file
    Content     NVARCHAR(MAX)  NOT NULL,            -- the chunk text shown to the model
    Embedding   VECTOR(1536)   NOT NULL,            -- text-embedding-3-small
    CreatedAt   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_DocumentChunks_DocumentId ON dbo.DocumentChunks(DocumentId);
```

"Did we generate embeddings for this file?" → `Documents.Status = 'Completed'` and `ChunkCount > 0`.

Search (exact cosine search; fast enough for thousands of chunks, no vector index needed):

```sql
SELECT TOP (@top) c.Content, d.FileName,
       VECTOR_DISTANCE('cosine', c.Embedding, CAST(@queryVector AS VECTOR(1536))) AS Distance
FROM dbo.DocumentChunks c JOIN dbo.Documents d ON d.Id = c.DocumentId
ORDER BY Distance;
```

Data access: **Microsoft.Data.SqlClient + plain SQL** (no ORM), so every query is visible in the code.
Vectors are passed as a JSON array string (`"[0.1, 0.2, …]"`) and cast to `VECTOR(1536)`.

---

## 5. Packages (versions pinned once in `Directory.Packages.props`)

| Package | Used by | Why |
|---|---|---|
| Aspire.AppHost.Sdk 13.x (not 13.6.0 — DCP bug on Windows) | AppHost | local orchestration + dashboard, no Docker |
| Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI | Api | `IChatClient`, `IEmbeddingGenerator`, function calling |
| Azure.Identity | Api | Entra ID sign-in to Foundry (only if no API key) |
| ModelContextProtocol (+ .AspNetCore if we expose MCP) | Api | MCP tools |
| Microsoft.Data.SqlClient | Api | SQL Server |
| PdfPig | Api | PDF text |
| DocumentFormat.OpenXml | Api | .docx and .xlsx text |
| Microsoft.Extensions.ServiceDiscovery, OpenTelemetry.* | ServiceDefaults | Aspire defaults |

---

## 6. Build steps (in order, each one runnable)

1. **Skeleton**: solution, 4 projects, central packages, Aspire AppHost starts Api + Web, dashboard opens.
2. **Database**: `schema.sql` + `DatabaseInitializer` creates the tables on startup.
3. **Upload + list**: `DocumentEndpoints` + `DocumentRepository` + Documents page (files show as `Pending`).
4. **Readers + chunker**: the four readers, `TextExtractor`, `TextChunker`.
5. **AI factory**: `AiOptions` + `AiClientFactory` (Foundry + OpenAI).
6. **Background ingestion**: `IngestionWorker` + `DocumentIngestor` + `ChunkRepository` → files become `Completed`.
7. **Chat**: `SearchDocumentsTool` + `ChatService` + `ChatEndpoints` + Chat page with sources.
8. **Skills**: `SkillLoader` + one example `SKILL.md`.
9. **MCP**: `McpToolLoader` (+ optional `/mcp` endpoint).
10. **README**: how to configure Foundry, run, and follow the code; sample docs; end-to-end check
    (upload each file type → Completed → ask a question → answer cites the file).

---

## 7. Decisions (confirmed)

- **Foundry sign-in**: use `Ai:ApiKey` when it is set; when it is empty, use Entra ID (`az login` / Visual Studio).
  The key is stored in user-secrets, never in `appsettings.json`.
- **Chat bot runs inside the Web API**: plain `IChatClient` + function calling. No separate agent project.
- **MCP in both directions**:
  - *Consume*: `McpToolLoader` adds tools from the servers listed under `Mcp:Servers` in appsettings.
  - *Expose*: the API publishes `search_documents` at `/mcp` (`Mcp/McpServerSetup.cs`), so VS Code or other agents can search your documents.
- **SQL Server**: `.\SQLEXPRESS` (SQL Server 2025 Express, Windows authentication):
  `Server=.\SQLEXPRESS;Database=AgentRag;Trusted_Connection=True;TrustServerCertificate=True`

## 8. Defaults I will use unless you say otherwise

- .NET 10, C# kept plain (no primary constructors / collection expressions — like your reference repo).
- Minimal API endpoints (grouped in 2 small files) instead of controllers.
- Original file stored in the database (`FileContent`), not on disk — nothing else to configure.
- Chat answer returned in one response (no streaming) for the first version.
- Chat history kept in the browser page only (not saved in the database).
- Max upload size 20 MB; same file (same hash) uploaded twice → rejected with a message.
- Database name `AgentRag`, created automatically if missing.
