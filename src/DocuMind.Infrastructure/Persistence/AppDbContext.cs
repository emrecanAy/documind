using DocuMind.Application.Abstractions;
using DocuMind.Domain.Common;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;
using DocuMind.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<EvalRun> EvalRuns => Set<EvalRun>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are generated client-side (UUIDv7, time ordered => index friendly). Telling EF the key is never
        // store-generated makes entities added through navigations (e.g. conversation.AddMessage) INSERTs, not UPDATEs.
        foreach (var entity in builder.Model.GetEntityTypes().Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
            builder.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        builder.Entity<RefreshToken>().Property(t => t.Id).ValueGeneratedNever();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as readable strings (the job queue's raw SQL relies on this).
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
