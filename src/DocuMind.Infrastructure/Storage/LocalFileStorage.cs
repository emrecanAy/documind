using DocuMind.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace DocuMind.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder shared by the API (writes uploads) and the Worker (reads them).</summary>
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "uploads");
}

/// <summary>Local disk implementation. Swap for S3 / Azure Blob in production behind the same interface.</summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.Root);

    public async Task<string> SaveAsync(Guid workspaceId, string fileName, Stream content, CancellationToken ct)
    {
        var safeName = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var relative = Path.Combine(workspaceId.ToString("N"), $"{Guid.CreateVersion7():N}_{safeName}");
        var fullPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        // path traversal guard
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid storage path.");
        return full;
    }
}
