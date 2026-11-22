// The "Ai" section of appsettings.json, as a C# class (the options pattern).
//
//   "Ai": {
//     "Provider": "Foundry",                                   Foundry or OpenAI
//     "Endpoint": "https://my-resource.services.ai.azure.com", Foundry: your resource URL. OpenAI: leave empty
//     "ApiKey": "",                                            keep it in user-secrets! Empty + Foundry = Entra ID (az login)
//     "ChatModel": "gpt-5-mini",                               the DEPLOYMENT name in Foundry
//     "EmbeddingModel": "text-embedding-3-small"               the DEPLOYMENT name in Foundry
//   }
//
// Set the key without putting it in a file in the repo:
//   dotnet user-secrets set "Ai:ApiKey" "<key>" --project src/AgentRag.Api

namespace AgentRag.Api.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = AiProviders.Foundry;

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string ChatModel { get; set; } = "gpt-5-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public static class AiProviders
{
    public const string Foundry = "Foundry";
    public const string OpenAI = "OpenAI";
}
