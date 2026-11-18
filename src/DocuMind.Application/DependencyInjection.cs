using DocuMind.Application.Features.Chat;
using DocuMind.Application.Features.Documents;
using DocuMind.Application.Features.Evaluation;
using DocuMind.Application.Features.Ingestion;
using DocuMind.Application.Features.Ingestion.Chunking;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Application.Features.Workspaces;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DocuMind.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Features
        services.AddScoped<IWorkspaceAuthorizer, WorkspaceAuthorizer>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ChatService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<EvalService>();
        services.AddScoped<EvalRunner>();
        services.AddSingleton<IEvalQueue, ChannelEvalQueue>();
        services.AddSingleton<CostCalculator>();

        // Ingestion
        services.AddScoped<IngestionPipeline>();
        services.AddScoped<IChunker, FixedTokenChunker>();
        services.AddScoped<IChunker, RecursiveChunker>();
        services.AddScoped<IChunker, SemanticChunker>();
        services.AddScoped<IChunkerFactory, ChunkerFactory>();

        // Retrieval
        services.AddScoped<IRetriever, HybridRetriever>();
        services.AddScoped<IQueryTransformer, QueryTransformer>();
        services.AddScoped<IReranker, LlmReranker>();

        return services;
    }
}
