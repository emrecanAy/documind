using System.Security.Cryptography;
using DocuMind.Application.Abstractions;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;
using DocuMind.Infrastructure.Identity;
using DocuMind.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Background;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string DemoEmail { get; set; } = "demo@documind.local";
    public string DemoPassword { get; set; } = "Demo123!";

    /// <summary>Folder with sample documents that are uploaded into the demo workspace on first start.</summary>
    public string? SamplesPath { get; set; }
}

/// <summary>Applies EF Core migrations and (optionally) seeds a demo user, workspace and documents.</summary>
internal static class DatabaseInitializer
{
    public static async Task InitializeAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = services.GetRequiredService<AppDbContext>();

        if (app.Configuration.GetValue("Database:AutoMigrate", true))
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync();
        }

        var seed = app.Configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
        if (!seed.Enabled)
            return;

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(seed.DemoEmail);
        if (user is not null)
            return;

        user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = seed.DemoEmail,
            UserName = seed.DemoEmail,
            DisplayName = "Demo User",
            EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, seed.DemoPassword);
        if (!created.Succeeded)
        {
            logger.LogWarning("Demo user could not be created: {Errors}", string.Join(", ", created.Errors.Select(e => e.Description)));
            return;
        }

        var workspace = Workspace.Create("Acme Corp Knowledge Base", "Demo workspace with HR, IT and product documents.", user.Id);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo user {Email} / {Password}", seed.DemoEmail, seed.DemoPassword);

        if (string.IsNullOrWhiteSpace(seed.SamplesPath) || !Directory.Exists(seed.SamplesPath))
            return;

        var storage = services.GetRequiredService<IFileStorage>();
        foreach (var file in Directory.EnumerateFiles(seed.SamplesPath))
        {
            await using var content = File.OpenRead(file);
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(content));
            content.Position = 0;
            var path = await storage.SaveAsync(workspace.Id, Path.GetFileName(file), content, CancellationToken.None);
            var document = Document.Create(workspace.Id, Path.GetFileName(file), "application/octet-stream",
                new FileInfo(file).Length, path, sha, user.Id);
            db.Documents.Add(document);
            db.IngestionJobs.Add(IngestionJob.Create(document.Id, workspace.Id));
        }

        await db.SaveChangesAsync();
        await services.GetRequiredService<IIngestionQueue>().SignalAsync(CancellationToken.None);
        logger.LogInformation("Queued sample documents from {Path}", seed.SamplesPath);
    }
}
