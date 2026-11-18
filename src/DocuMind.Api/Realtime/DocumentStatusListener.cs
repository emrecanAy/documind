using System.Text.Json;
using DocuMind.Application.Abstractions;
using DocuMind.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace DocuMind.Api.Realtime;

/// <summary>Bridges Postgres NOTIFY (published by the worker) to SignalR groups.</summary>
internal sealed class DocumentStatusListener(
    IConfiguration configuration,
    IHubContext<DocumentsHub> hub,
    ILogger<DocumentStatusListener> logger) : BackgroundService
{
    private const string Channel = "document_status";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.DatabaseConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("No database connection string; live document status disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, e) => _ = ForwardAsync(e.Payload);

                await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                logger.LogInformation("Listening for document status notifications");
                while (!stoppingToken.IsCancellationRequested)
                    await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Status listener connection lost; reconnecting");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ForwardAsync(string payload)
    {
        try
        {
            var change = JsonSerializer.Deserialize<DocumentStatusChanged>(payload, JsonOptions);
            if (change is not null)
                await hub.Clients.Group(DocumentsHub.GroupName(change.WorkspaceId)).SendAsync("documentStatus", change);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to forward document status notification");
        }
    }
}
