// The web app (Blazor Server): the Documents page and the Chat page.
//
//   browser <-> this app (SignalR)  --RagApiClient (HTTP)-->  AgentRag.Api
//
// "https+http://api" is an Aspire service-discovery address: "api" is the name given in the AppHost,
// and Aspire tells this app the real URL. Prefer https, fall back to http.

using AgentRag.ServiceDefaults;
using AgentRag.Web;
using AgentRag.Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Blazor Server: the UI runs on the server and updates the browser over a SignalR connection.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The ONLY way this app talks to the API. A "typed" HttpClient: RagApiClient gets an HttpClient already
// pointed at the API (with the timeouts and service discovery from AddServiceDefaults).
builder.Services.AddHttpClient<RagApiClient>(client => client.BaseAddress = new Uri("https+http://api"));

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
