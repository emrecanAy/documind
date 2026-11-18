using DocuMind.Domain.Conversations;
using DocuMind.Domain.Documents;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Ingestion;
using DocuMind.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Workspace> Workspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentChunk> DocumentChunks { get; }
    DbSet<IngestionJob> IngestionJobs { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<EvalRun> EvalRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
