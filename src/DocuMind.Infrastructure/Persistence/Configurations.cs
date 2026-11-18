using System.Text.Json;
using DocuMind.Domain.Conversations;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;
using DocuMind.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocuMind.Infrastructure.Persistence;

internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a value object / list to a jsonb column with value semantics for change tracking.</summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property)
    {
        property.HasConversion(
                v => JsonSerializer.Serialize(v, Options),
                v => JsonSerializer.Deserialize<T>(v, Options)!,
                new ValueComparer<T>(
                    (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!))
            .HasColumnType("jsonb");
        return property;
    }
}

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(500);
        builder.Property(w => w.Settings).HasJsonConversion().IsRequired();
        builder.HasMany(w => w.Members).WithOne().HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.HasKey(m => new { m.WorkspaceId, m.UserId });
        builder.HasIndex(m => m.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(d => d.EmbeddingModel).HasMaxLength(150);
        builder.Property(d => d.Error).HasMaxLength(2000);
        builder.HasIndex(d => new { d.WorkspaceId, d.Sha256 }).IsUnique();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.Section).HasMaxLength(500);
        builder.HasIndex(c => new { c.DocumentId, c.Index });
        builder.HasIndex(c => c.WorkspaceId);
        builder.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class IngestionJobConfiguration : IEntityTypeConfiguration<IngestionJob>
{
    public void Configure(EntityTypeBuilder<IngestionJob> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.LastError).HasMaxLength(2000);
        // Partial index keeps queue polling cheap no matter how many completed jobs accumulate.
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt })
            .HasFilter("status IN ('Pending', 'Running')");
        builder.HasIndex(j => j.DocumentId);
        builder.HasOne<Document>().WithMany().HasForeignKey(j => j.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => new { c.WorkspaceId, c.UserId, c.UpdatedAt });
        builder.HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.Citations).HasJsonConversion();
        builder.Property(m => m.AgentSteps).HasJsonConversion();
        builder.Property(m => m.Usage).HasJsonConversion();
        builder.Property(m => m.FeedbackComment).HasMaxLength(1000);
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}

internal sealed class EvalRunConfiguration : IEntityTypeConfiguration<EvalRun>
{
    public void Configure(EntityTypeBuilder<EvalRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.DatasetName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.DatasetJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.SettingsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Metrics).HasJsonConversion();
        builder.Property(r => r.Results).HasJsonConversion();
        builder.HasIndex(r => new { r.WorkspaceId, r.CreatedAt });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.Ignore(t => t.IsActive);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
    }
}
