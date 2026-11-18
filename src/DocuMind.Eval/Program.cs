using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// DocuMind evaluation CLI
// Runs a golden dataset against a running API and prints retrieval + answer quality metrics.
// Usage:
//   dotnet run --project src/DocuMind.Eval -- --dataset eval/datasets/sample-golden.json [--seed samples/docs]
//          [--api http://localhost:5080] [--email demo@documind.local] [--password Demo123!]
//          [--workspace <id>] [--no-judge] [--min-hit-rate 0.8] [--out eval-report.md]

var options = CliOptions.Parse(args);
if (options is null)
    return 2;

using var http = new HttpClient { BaseAddress = new Uri(options.Api), Timeout = TimeSpan.FromMinutes(5) };
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

try
{
    Console.WriteLine($"→ Signing in to {options.Api} as {options.Email}");
    var login = await PostJsonAsync("/api/auth/login", new { options.Email, options.Password });
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!["accessToken"]!.GetValue<string>());

    var workspaceId = options.WorkspaceId ?? await ResolveWorkspaceAsync();
    Console.WriteLine($"→ Workspace {workspaceId}");

    if (options.SeedPath is not null)
        await SeedAsync(workspaceId, options.SeedPath);
    await WaitForIndexingAsync(workspaceId);

    var dataset = JsonNode.Parse(await File.ReadAllTextAsync(options.DatasetPath))!;
    Console.WriteLine($"→ Starting evaluation '{dataset["name"]}' ({dataset["items"]!.AsArray().Count} questions, judge: {!options.NoJudge})");
    var run = await PostJsonAsync($"/api/workspaces/{workspaceId}/evals", new { dataset, useLlmJudge = !options.NoJudge });
    var runId = run!["id"]!.GetValue<string>();

    JsonNode details;
    var started = DateTime.UtcNow;
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        details = (await http.GetFromJsonAsync<JsonNode>($"/api/workspaces/{workspaceId}/evals/{runId}", json))!;
        var status = details["run"]!["status"]!.GetValue<string>();
        Console.Write($"\r  status: {status,-10} elapsed: {(DateTime.UtcNow - started).TotalSeconds:F0}s   ");
        if (status is "Completed" or "Failed")
            break;
    }

    Console.WriteLine();
    if (details["run"]!["status"]!.GetValue<string>() == "Failed")
    {
        Console.Error.WriteLine($"✗ Evaluation failed: {details["run"]!["error"]}");
        return 1;
    }

    var report = Report.Build(details);
    Console.WriteLine(report);
    if (options.OutPath is not null)
    {
        await File.WriteAllTextAsync(options.OutPath, report);
        Console.WriteLine($"→ Report written to {options.OutPath}");
    }

    var hitRate = details["run"]!["metrics"]!["hitRate"]!.GetValue<double>();
    if (hitRate < options.MinHitRate)
    {
        Console.Error.WriteLine($"✗ Hit rate {hitRate:P0} is below the threshold {options.MinHitRate:P0}");
        return 1;
    }

    return 0;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"✗ {ex.Message}");
    return 1;
}

async Task<JsonNode?> PostJsonAsync(string url, object body)
{
    var response = await http.PostAsJsonAsync(url, body, json);
    if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"POST {url} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    return await response.Content.ReadFromJsonAsync<JsonNode>(json);
}

async Task<string> ResolveWorkspaceAsync()
{
    var workspaces = (await http.GetFromJsonAsync<JsonArray>("/api/workspaces", json))!;
    if (workspaces.Count == 0)
        throw new HttpRequestException("The user has no workspace. Create one in the UI or pass --workspace.");
    return workspaces[0]!["id"]!.GetValue<string>();
}

async Task SeedAsync(string workspaceId, string folder)
{
    foreach (var file in Directory.EnumerateFiles(folder))
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(await File.ReadAllBytesAsync(file));
        form.Add(content, "files", Path.GetFileName(file));
        var response = await http.PostAsync($"/api/workspaces/{workspaceId}/documents", form);
        var outcome = response.StatusCode switch
        {
            HttpStatusCode.OK => "uploaded",
            HttpStatusCode.Conflict => "already present",
            _ => $"failed ({(int)response.StatusCode})",
        };
        Console.WriteLine($"  {Path.GetFileName(file)}: {outcome}");
    }
}

async Task WaitForIndexingAsync(string workspaceId)
{
    Console.WriteLine("→ Waiting for documents to be indexed");
    for (var i = 0; i < 400; i++)
    {
        var documents = (await http.GetFromJsonAsync<JsonArray>($"/api/workspaces/{workspaceId}/documents", json))!;
        var pending = documents.Count(d => d!["status"]!.GetValue<string>() is "Queued" or "Processing");
        if (pending == 0)
        {
            Console.WriteLine($"  {documents.Count} documents ready");
            return;
        }

        Console.Write($"\r  {pending} document(s) still processing…   ");
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    throw new HttpRequestException("Timed out waiting for indexing.");
}

internal sealed record CliOptions(string Api, string Email, string Password, string DatasetPath, string? WorkspaceId,
    string? SeedPath, bool NoJudge, double MinHitRate, string? OutPath)
{
    public static CliOptions? Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        var dataset = Get("--dataset", "eval/datasets/sample-golden.json");
        if (!File.Exists(dataset))
        {
            Console.Error.WriteLine($"Dataset not found: {dataset}");
            return null;
        }

        var seed = Get("--seed", string.Empty);
        var workspace = Get("--workspace", string.Empty);
        var outPath = Get("--out", string.Empty);
        return new CliOptions(
            Get("--api", "http://localhost:5080"),
            Get("--email", "demo@documind.local"),
            Get("--password", "Demo123!"),
            dataset,
            workspace.Length == 0 ? null : workspace,
            seed.Length == 0 ? null : seed,
            args.Contains("--no-judge"),
            double.Parse(Get("--min-hit-rate", "0"), CultureInfo.InvariantCulture),
            outPath.Length == 0 ? null : outPath);
    }
}

internal static class Report
{
    public static string Build(JsonNode details)
    {
        var run = details["run"]!;
        var m = run["metrics"]!;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Evaluation: {run["datasetName"]}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Configuration: `{run["settingsSnapshot"]}`");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Questions | {m["items"]} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Hit rate | {Pct(m["hitRate"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Recall | {Pct(m["recall"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| MRR | {Num(m["mrr"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Relevance (1-5) | {Num(m["relevance"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Groundedness (1-5) | {Num(m["groundedness"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Coherence (1-5) | {Num(m["coherence"])} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Avg latency | {Num(m["avgLatencyMs"])} ms |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Total tokens | {m["totalTokens"]} |");
        sb.AppendLine();
        sb.AppendLine("| # | Question | Hit | RR | Rel | Grd | Retrieved |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        var i = 1;
        foreach (var r in details["results"]!.AsArray())
        {
            var retrieved = string.Join(", ", r!["retrievedDocuments"]!.AsArray().Select(d => d!.GetValue<string>()));
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {i++} | {r["question"]} | {(r["hit"]!.GetValue<bool>() ? "✓" : "✗")} | {Num(r["reciprocalRank"])} | {Num(r["relevance"])} | {Num(r["groundedness"])} | {retrieved} |");
        }

        return sb.ToString();
    }

    private static string Pct(JsonNode? n) => n is null ? "–" : n.GetValue<double>().ToString("P0", CultureInfo.InvariantCulture);

    private static string Num(JsonNode? n) => n is null ? "–" : n.GetValue<double>().ToString("0.##", CultureInfo.InvariantCulture);
}
