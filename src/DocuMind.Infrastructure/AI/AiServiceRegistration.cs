using System.ClientModel;
using System.Data.Common;
using Anthropic;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;
using OpenAI;

namespace DocuMind.Infrastructure.AI;

/// <summary>
/// Provider-agnostic AI wiring. Everything downstream depends only on Microsoft.Extensions.AI's
/// <see cref="IChatClient"/> / <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>, so switching between
/// Ollama, OpenAI and Anthropic is configuration only. Middleware adds tool calling, OpenTelemetry and logging.
/// </summary>
internal static class AiServiceRegistration
{
    public const string TelemetrySourceName = "DocuMind.AI";

    public static void AddAiServices(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(AiOptions.SectionName);
        builder.Services.Configure<AiOptions>(section);
        var options = section.Get<AiOptions>() ?? new AiOptions();

        // Aspire injects the Ollama endpoint as a connection string.
        var ollamaConnection = builder.Configuration.GetConnectionString("ollama");
        if (!string.IsNullOrWhiteSpace(ollamaConnection))
            options.Ollama.Endpoint = ParseEndpoint(ollamaConnection) ?? options.Ollama.Endpoint;

        var sensitiveTelemetry = builder.Environment.IsDevelopment();

        builder.Services
            .AddChatClient(_ => CreateChatClient(options))
            .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 6)
            .UseOpenTelemetry(sourceName: TelemetrySourceName, configure: c => c.EnableSensitiveData = sensitiveTelemetry)
            .UseLogging();

        builder.Services
            .AddEmbeddingGenerator(_ => CreateEmbeddingGenerator(options))
            .UseOpenTelemetry(sourceName: TelemetrySourceName)
            .UseLogging();

        builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
    }

    private static IChatClient CreateChatClient(AiOptions options) => options.ChatProvider.ToLowerInvariant() switch
    {
        "ollama" => new OllamaApiClient(CreateOllamaHttpClient(options), options.ChatModel),
        "openai" => CreateOpenAIClient(options).GetChatClient(options.ChatModel).AsIChatClient(),
        "anthropic" => new AnthropicClient { ApiKey = Require(options.Anthropic.ApiKey, "AI:Anthropic:ApiKey") }
            .AsIChatClient(options.ChatModel, defaultMaxOutputTokens: 4096),
        _ => throw new InvalidOperationException($"Unknown chat provider '{options.ChatProvider}'. Use ollama, openai or anthropic."),
    };

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiOptions options) =>
        options.EmbeddingProvider.ToLowerInvariant() switch
        {
            "ollama" => new OllamaApiClient(CreateOllamaHttpClient(options), options.EmbeddingModel),
            "openai" => CreateOpenAIClient(options).GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator(),
            _ => throw new InvalidOperationException(
                $"Unknown embedding provider '{options.EmbeddingProvider}'. Use ollama or openai (Anthropic has no embedding API)."),
        };

    private static HttpClient CreateOllamaHttpClient(AiOptions options) => new()
    {
        BaseAddress = new Uri(options.Ollama.Endpoint ?? "http://localhost:11434"),
        Timeout = TimeSpan.FromMinutes(10), // local models on CPU can be slow
    };

    private static OpenAIClient CreateOpenAIClient(AiOptions options)
    {
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.OpenAI.Endpoint))
            clientOptions.Endpoint = new Uri(options.OpenAI.Endpoint); // Azure OpenAI / LM Studio / vLLM compatible
        return new OpenAIClient(new ApiKeyCredential(Require(options.OpenAI.ApiKey, "AI:OpenAI:ApiKey")), clientOptions);
    }

    private static string Require(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Configuration value '{key}' is required for the selected AI provider.")
            : value;

    private static string? ParseEndpoint(string connectionString)
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            return uri.ToString();
        var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return parsed.TryGetValue("Endpoint", out var endpoint) ? endpoint?.ToString() : null;
    }
}
