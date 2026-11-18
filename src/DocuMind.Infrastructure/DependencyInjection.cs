using DocuMind.Application.Abstractions;
using DocuMind.Infrastructure.AI;
using DocuMind.Infrastructure.Identity;
using DocuMind.Infrastructure.Parsing;
using DocuMind.Infrastructure.Persistence;
using DocuMind.Infrastructure.Storage;
using DocuMind.Infrastructure.VectorStore;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DocuMind.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "documind";
    public const string QdrantConnectionName = "qdrant";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        // Postgres via the Aspire client integration: health checks, OpenTelemetry tracing and retries included.
        builder.AddNpgsqlDbContext<AppDbContext>(DatabaseConnectionName, configureDbContextOptions: options =>
            options.UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Qdrant (gRPC) via Aspire client integration.
        builder.AddQdrantClient(QdrantConnectionName);
        services.AddScoped<IVectorStore, QdrantVectorStore>();

        builder.AddAiServices();

        services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        services.AddSingleton<IDocumentParser, DocxDocumentParser>();
        services.AddSingleton<IDocumentParser, MarkdownDocumentParser>();
        services.AddSingleton<IDocumentParser, HtmlDocumentParser>();
        services.AddSingleton<IDocumentParser, PlainTextDocumentParser>();

        services.AddScoped<IIngestionQueue, PostgresIngestionQueue>();
        services.AddScoped<IDocumentStatusNotifier, PostgresDocumentStatusNotifier>();

        // Identity (users + password hashing). Authentication itself is JWT bearer, configured in the API.
        services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return builder;
    }
}
