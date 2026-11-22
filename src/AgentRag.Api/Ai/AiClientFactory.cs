// Builds the two AI clients the app uses, from the "Ai" settings:
//
//   IChatClient                              gpt-5-mini              (answers questions, calls tools)
//   IEmbeddingGenerator<string, Embedding>   text-embedding-3-small  (text -> 1536 numbers)
//
// Both are STANDARD Microsoft.Extensions.AI interfaces. This is the only class that knows
// which provider (Foundry or OpenAI) is behind them - switching is a config change.
//
// WHY ONE OpenAI SDK FOR BOTH PROVIDERS
//   Microsoft Foundry offers the same API as OpenAI at https://<resource>/openai/v1/.
//   So both providers are "an OpenAIClient", only the address and the sign-in differ.
//
// SIGN-IN
//   ApiKey set                -> API key (Foundry or OpenAI).
//   ApiKey empty + Foundry    -> Entra ID: your `az login` (or Visual Studio) account.
//                                That account needs the "Cognitive Services OpenAI User" role on the resource.

using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AgentRag.Api.Ai;

public sealed class AiClientFactory
{
    /// <summary>The OpenTelemetry source name: model calls show up as traces in the Aspire dashboard.</summary>
    public const string TelemetrySourceName = "Microsoft.Extensions.AI";

    private const string FoundryTokenScope = "https://cognitiveservices.azure.com/.default";

    private readonly AiOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public AiClientFactory(IOptions<AiOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public IChatClient CreateChatClient()
    {
        IChatClient client = CreateOpenAIClient()
            .GetChatClient(_options.ChatModel)
            .AsIChatClient();

        // A pipeline around the client (like ASP.NET Core middleware):
        //   UseFunctionInvocation  when the model asks for a tool, run it and send the result back - automatically
        //   UseOpenTelemetry       a trace per call in the Aspire dashboard (with prompts: this is a local learning app)
        //   UseLogging             request/response logs at Debug level
        return client.AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: TelemetrySourceName, configure: telemetry => telemetry.EnableSensitiveData = true)
            .UseLogging(_loggerFactory)
            .Build();
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator = CreateOpenAIClient()
            .GetEmbeddingClient(_options.EmbeddingModel)
            .AsIEmbeddingGenerator();

        return generator.AsBuilder()
            .UseOpenTelemetry(sourceName: TelemetrySourceName)
            .UseLogging(_loggerFactory)
            .Build();
    }

    private OpenAIClient CreateOpenAIClient()
    {
        switch (_options.Provider)
        {
            case AiProviders.Foundry:
                return CreateFoundryClient();
            case AiProviders.OpenAI:
                return CreatePlainOpenAIClient();
            default:
                throw new InvalidOperationException(
                    "Ai:Provider is '" + _options.Provider + "'. Use '" + AiProviders.Foundry + "' or '" + AiProviders.OpenAI + "'.");
        }
    }

    private OpenAIClient CreateFoundryClient()
    {
        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            Endpoint = GetFoundryV1Endpoint(_options.Endpoint)
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new OpenAIClient(new ApiKeyCredential(_options.ApiKey), clientOptions);
        }

        // No key: sign in with Entra ID. Azure CLI first (az login), then the usual
        // DefaultAzureCredential chain (Visual Studio, environment variables, managed identity in Azure).
        TokenCredential credential = new ChainedTokenCredential(new AzureCliCredential(), new DefaultAzureCredential());
        BearerTokenPolicy tokenPolicy = new BearerTokenPolicy(credential, FoundryTokenScope);
        return new OpenAIClient(tokenPolicy, clientOptions);
    }

    private OpenAIClient CreatePlainOpenAIClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Ai:ApiKey is empty. OpenAI needs an API key: dotnet user-secrets set \"Ai:ApiKey\" \"sk-...\" --project src/AgentRag.Api");
        }

        OpenAIClientOptions clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(_options.Endpoint);   // only for OpenAI-compatible servers
        }

        return new OpenAIClient(new ApiKeyCredential(_options.ApiKey), clientOptions);
    }

    // Accepts any of the URLs the Foundry portal shows and turns it into the OpenAI-compatible address:
    //   https://my-res.services.ai.azure.com
    //   https://my-res.openai.azure.com
    //   https://my-res.services.ai.azure.com/api/projects/my-project
    //   -> https://my-res.services.ai.azure.com/openai/v1/
    private static Uri GetFoundryV1Endpoint(string? endpoint)
    {
        Uri? uri;
        if (string.IsNullOrWhiteSpace(endpoint)
            || endpoint.Contains("YOUR-RESOURCE", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
        {
            throw new InvalidOperationException(
                "Ai:Endpoint is not set. Put your Foundry resource URL in src/AgentRag.Api/appsettings.json, "
                + "for example https://my-resource.services.ai.azure.com (got '" + endpoint + "').");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/openai/v1/");
    }
}
