// The Web API. Read this file first, then DependencyInjection.cs (every class the app uses).
//
//   POST   /api/documents                 upload files -> saved as "Pending"
//   GET    /api/documents                 all files and their ingestion status
//   POST   /api/documents/{id}/reprocess  run ingestion again for one file
//   DELETE /api/documents/{id}            remove a file and its chunks
//   POST   /api/chat                      ask the chat bot a question
//   /mcp                                  MCP server with the search_documents tool (for VS Code / other agents)
//
// In the background, IngestionWorker turns "Pending" files into chunks + vectors.

using AgentRag.Api;
using AgentRag.Api.Database;
using AgentRag.Api.Endpoints;
using AgentRag.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddRagApplication(builder.Configuration);

WebApplication app = builder.Build();

// Create the database and the two tables if they do not exist yet.
await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

app.MapDefaultEndpoints();
app.MapDocumentEndpoints();
app.MapChatEndpoints();
app.MapMcp("/mcp");

app.Run();
