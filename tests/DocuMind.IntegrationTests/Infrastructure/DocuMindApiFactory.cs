using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocuMind.Application.Abstractions;
using DocuMind.Application.Features.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Qdrant;

[assembly: AssemblyFixture(typeof(DocuMind.IntegrationTests.Infrastructure.DocuMindApiFactory))]

namespace DocuMind.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against real Postgres + Qdrant containers (Testcontainers). Only the LLM and the embedding
/// model are replaced with deterministic fakes, so tests are fast, free and reproducible.
/// </summary>
public sealed class DocuMindApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly QdrantContainer _qdrant = new QdrantBuilder("qdrant/qdrant:v1.17.0").Build();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "documind-tests", Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _qdrant.StartAsync());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:documind", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:qdrant", $"Endpoint={_qdrant.GetGrpcConnectionString()}");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Storage:Root", _storage);
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("AI:ChatModel", FakeChatClient.Model);
        builder.UseSetting("AI:EmbeddingModel", "fake-embedding");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatClient>();
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IChatClient>(new FunctionInvokingChatClient(new FakeChatClient()));
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator());
        });
    }

    /// <summary>Runs the ingestion worker loop in-process until the queue is empty.</summary>
    public async Task DrainIngestionQueueAsync()
    {
        while (true)
        {
            await using var scope = Services.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IIngestionQueue>();
            var job = await queue.ClaimNextAsync(CancellationToken.None);
            if (job is null)
                return;
            await scope.ServiceProvider.GetRequiredService<IngestionPipeline>().ProcessAsync(job, CancellationToken.None);
        }
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string? email = null)
    {
        var client = CreateClient();
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Passw0rd!", displayName = "Test User" }, Json);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _qdrant.DisposeAsync();
        if (Directory.Exists(_storage))
            Directory.Delete(_storage, recursive: true);
    }
}
