# Agent RAG

A deliberately **simple** RAG (Retrieval-Augmented Generation) application in C# / .NET 10.
Upload documents, a background service turns them into vectors in SQL Server, then chat with a bot
that answers **only from your documents** and shows its sources.

- **Models**: `gpt-5-mini` (chat) and `text-embedding-3-small` (embeddings) from **Microsoft Foundry** — or OpenAI.
- **Vector store**: your local **SQL Server 2025** (`VECTOR(1536)` + `VECTOR_DISTANCE`). **No Docker.**
- **Orchestration**: **.NET Aspire** (AppHost + dashboard).
- **AI building blocks**: Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`), **Skills** (`SKILL.md`), **MCP** (client and server).

The code is written to be read: one job per class, a short "why" comment at the top of every file,
plain C# 10, and **all dependencies registered in one file**.

---

## 1. Run it

**You need:** .NET 10 SDK, SQL Server 2025 (Express is fine), and access to a Foundry resource
with the deployments `gpt-5-mini` and `text-embedding-3-small`.

1. **Database** — the connection string is in [src/AgentRag.AppHost/appsettings.json](src/AgentRag.AppHost/appsettings.json)
   (`.\SQLEXPRESS`, Windows sign-in). The database `AgentRag` and its tables are created automatically on first start.
2. **Models** — set your Foundry resource URL in [src/AgentRag.Api/appsettings.json](src/AgentRag.Api/appsettings.json) (`Ai:Endpoint`).
   Then **either** sign in with the Azure CLI (your account needs the *Cognitive Services OpenAI User* role on the resource):
   ```powershell
   az login
   ```
   **or** store an API key (never in appsettings.json):
   ```powershell
   dotnet user-secrets set "Ai:ApiKey" "<your-key>" --project src/AgentRag.Api
   ```
3. **Start** — press F5 on `AgentRag.AppHost` in Visual Studio, or:
   ```powershell
   dotnet run --project src/AgentRag.AppHost
   ```
   The Aspire dashboard opens. Click the **web** link (https://localhost:7400).
4. On **Documents**, upload the files from [sample-docs](sample-docs) (one of every supported type).
   Watch them go `Pending → Processing → Completed`. Then ask questions on **Chat**.

> Don't build the solution in Visual Studio while it is running from the command line (or the other way round):
> the second build cannot overwrite the running app's files and the app breaks.

---

## 2. How it fits together

```
 Browser
   │
   ▼
 AgentRag.Web (Blazor Server)       Documents page, Chat page
   │  RagApiClient → https+http://api   (Aspire service discovery)
   ▼
 AgentRag.Api (ASP.NET Core)
   ├─ /api/documents   upload → saved as Pending in SQL
   ├─ IngestionWorker  (background) Pending → read → chunk → embed → save chunks → Completed
   ├─ /api/chat        gpt-5-mini + tools: search_documents, load_skill, other MCP servers' tools
   └─ /mcp             our own MCP server: search_documents for VS Code / other agents
   │
   ▼
 SQL Server 2025       dbo.Documents (files + status)   dbo.DocumentChunks (text + VECTOR(1536))
```

| Project | What it is |
|---|---|
| `AgentRag.AppHost` | Aspire: starts `api` + `web`, hands them the connection string and each other's address |
| `AgentRag.ServiceDefaults` | telemetry, health checks, service discovery, HTTP timeouts — one call: `AddServiceDefaults()` |
| `AgentRag.Api` | everything that is not UI |
| `AgentRag.Web` | the UI only: no AI SDK, no database |

---

## 3. Read the code in this order

**Start here**
1. [Api/Program.cs](src/AgentRag.Api/Program.cs) — 20 lines: what the API exposes.
2. [Api/DependencyInjection.cs](src/AgentRag.Api/DependencyInjection.cs) — **every** class the app uses, in one place.

**Upload and background ingestion**
3. [Endpoints/DocumentEndpoints.cs](src/AgentRag.Api/Endpoints/DocumentEndpoints.cs) → [Ingestion/DocumentUploader.cs](src/AgentRag.Api/Ingestion/DocumentUploader.cs) — check the file, save it as *Pending*.
4. [Ingestion/IngestionWorker.cs](src/AgentRag.Api/Ingestion/IngestionWorker.cs) — the background loop and the status changes.
5. [Ingestion/DocumentIngestor.cs](src/AgentRag.Api/Ingestion/DocumentIngestor.cs) — one file: read → chunk → embed → save.
6. [Ingestion/TextExtractor.cs](src/AgentRag.Api/Ingestion/TextExtractor.cs) and [Ingestion/Readers/](src/AgentRag.Api/Ingestion/Readers) — one small class per file type.
7. [Ingestion/TextChunker.cs](src/AgentRag.Api/Ingestion/TextChunker.cs) — why and how text is split.

**Database**
8. [Database/schema.sql](src/AgentRag.Api/Database/schema.sql) — the two tables.
9. [Database/DocumentRepository.cs](src/AgentRag.Api/Database/DocumentRepository.cs), [Database/ChunkRepository.cs](src/AgentRag.Api/Database/ChunkRepository.cs) — all the SQL, including the vector search.

**AI models**
10. [Ai/AiClientFactory.cs](src/AgentRag.Api/Ai/AiClientFactory.cs) — the *only* class that knows about Foundry vs OpenAI.

**Chat**
11. [Chat/ChatService.cs](src/AgentRag.Api/Chat/ChatService.cs) — messages + tools → model → answer + sources.
12. [Chat/SystemPrompt.cs](src/AgentRag.Api/Chat/SystemPrompt.cs) — how the bot is told to behave.
13. [Search/DocumentSearch.cs](src/AgentRag.Api/Search/DocumentSearch.cs) — question → vector → closest chunks.
14. [Skills/SkillLoader.cs](src/AgentRag.Api/Skills/SkillLoader.cs) and the `SKILL.md` folders next to it.
15. [Mcp/McpToolLoader.cs](src/AgentRag.Api/Mcp/McpToolLoader.cs) (client) and [Mcp/DocumentMcpTools.cs](src/AgentRag.Api/Mcp/DocumentMcpTools.cs) (server).

**Web**
16. [Web/RagApiClient.cs](src/AgentRag.Web/RagApiClient.cs) — every call to the API.
17. [Pages/Documents.razor](src/AgentRag.Web/Components/Pages/Documents.razor), [Pages/Chat.razor](src/AgentRag.Web/Components/Pages/Chat.razor).

---

## 4. The database

| `dbo.Documents` | |
|---|---|
| `Id`, `FileName`, `FileExtension`, `ContentType`, `FileSizeBytes` | the upload |
| `FileHash` | SHA-256 of the content — the same file cannot be uploaded twice |
| `FileContent` | the original file; the background service reads it from here |
| `Status` | `Pending` → `Processing` → `Completed` / `Failed` — **"did we generate embeddings?"** |
| `ChunkCount`, `ErrorMessage`, `UploadedAt`, `ProcessedAt` | the result of ingestion |

| `dbo.DocumentChunks` | |
|---|---|
| `DocumentId` | which file (deleting the file deletes its chunks) |
| `ChunkIndex`, `Content` | the piece of text, in order |
| `Embedding` | `VECTOR(1536)` from `text-embedding-3-small` |

Useful queries in SSMS:

```sql
SELECT Id, FileName, Status, ChunkCount, ErrorMessage FROM dbo.Documents;          -- what is embedded?
UPDATE dbo.Documents SET Status = 'Pending' WHERE Status = 'Failed';               -- retry failures
```

---

## 5. Common changes

| I want to… | Change |
|---|---|
| use OpenAI instead of Foundry | `Ai:Provider` = `OpenAI`, `Ai:Endpoint` empty, set `Ai:ApiKey` in user-secrets |
| use other deployment names | `Ai:ChatModel`, `Ai:EmbeddingModel` in `appsettings.json` |
| change how the bot answers | [Chat/SystemPrompt.cs](src/AgentRag.Api/Chat/SystemPrompt.cs) |
| add a skill | create `src/AgentRag.Api/Skills/<name>/SKILL.md` (copy an existing one) — no code change |
| give the bot tools from another MCP server | add it under `Mcp:Servers`, e.g. `"learn": "https://learn.microsoft.com/api/mcp"` |
| use our search from VS Code / Copilot | `.vscode/mcp.json`: `{ "servers": { "agent-rag": { "type": "http", "url": "https://localhost:7300/mcp" } } }` |
| support another file type | a new reader in `Ingestion/Readers`, register it in `DependencyInjection.cs`, add it to `TextExtractor` |
| change chunk size | `ChunkSize` / `ChunkOverlap` in [Ingestion/TextChunker.cs](src/AgentRag.Api/Ingestion/TextChunker.cs) |
| use an embedding model with another size | change `VECTOR(1536)` in `schema.sql` and `ChunkRepository` (and re-create the table) |

---

## 6. Limits (on purpose, to keep it simple)

- Scanned PDFs have no text (they would need OCR first). Old `.doc` / `.xls` files are not supported.
- One background worker processes one file at a time.
- The chat answer comes back in one piece (no streaming), and the conversation lives only in the browser page.
- Vector search is exact (compares with every chunk): fine for thousands of chunks, no vector index needed.
