using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// ---------- Infrastructure ----------
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("documind-postgres")
    .WithLifetime(ContainerLifetime.Persistent);
var database = postgres.AddDatabase("documind");

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("documind-qdrant")
    .WithLifetime(ContainerLifetime.Persistent);

var chatModel = builder.Configuration["AI:ChatModel"] ?? "llama3.2:3b";
var embeddingModel = builder.Configuration["AI:EmbeddingModel"] ?? "nomic-embed-text";
var usesOllama = string.Equals(builder.Configuration["AI:ChatProvider"] ?? "ollama", "ollama", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(builder.Configuration["AI:EmbeddingProvider"] ?? "ollama", "ollama", StringComparison.OrdinalIgnoreCase);

// Ollama: "container" (default, zero install) or "local" (uses an Ollama already installed on the host — much faster with a GPU).
(IResourceBuilder<IResourceWithConnectionString> Server, IResourceBuilder<OllamaModelResource> Chat,
    IResourceBuilder<OllamaModelResource> Embedding)? ollama = null;
if (usesOllama)
{
    if (builder.Configuration["Ollama:Mode"] == "local")
    {
        var local = builder.AddOllamaLocal("ollama");
        ollama = (local, local.AddModel("chat-model", chatModel), local.AddModel("embedding-model", embeddingModel));
    }
    else
    {
        var container = builder.AddOllama("ollama").WithDataVolume("documind-ollama").WithLifetime(ContainerLifetime.Persistent);
        if (builder.Configuration.GetValue<bool>("Ollama:Gpu"))
            container.WithGPUSupport();
        ollama = (container, container.AddModel("chat-model", chatModel), container.AddModel("embedding-model", embeddingModel));
    }
}

// ---------- Application ----------
var api = builder.AddProject<Projects.DocuMind_Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(qdrant).WaitFor(qdrant)
    .WithEnvironment("Storage__Root", Path.Combine(repoRoot, ".data", "uploads"))
    .WithEnvironment("Seed__SamplesPath", Path.Combine(repoRoot, "samples", "docs"))
    .WithExternalHttpEndpoints();

var worker = builder.AddProject<Projects.DocuMind_Worker>("worker")
    .WithReference(database)
    .WithReference(qdrant).WaitFor(qdrant)
    .WithEnvironment("Storage__Root", Path.Combine(repoRoot, ".data", "uploads"))
    .WaitFor(api); // the API applies database migrations on startup

if (ollama is { } o)
{
    api.WithReference(o.Server).WaitFor(o.Chat).WaitFor(o.Embedding);
    worker.WithReference(o.Server).WaitFor(o.Embedding);
}

// Forward the AI section (provider, models, API keys from user-secrets) to both services.
foreach (var (key, value) in builder.Configuration.GetSection("AI").AsEnumerable(makePathsRelative: true))
{
    if (value is null)
        continue;
    var name = "AI__" + key.Replace(":", "__", StringComparison.Ordinal);
    api.WithEnvironment(name, value);
    worker.WithEnvironment(name, value);
}

builder.AddViteApp("web", Path.Combine(repoRoot, "web"))
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
