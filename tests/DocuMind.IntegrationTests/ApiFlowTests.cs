using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocuMind.IntegrationTests.Infrastructure;

namespace DocuMind.IntegrationTests;

public sealed class ApiFlowTests(DocuMindApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = DocuMindApiFactory.Json;

    private const string Handbook = """
        # Employee Handbook

        ## Annual Leave
        Full-time employees receive 20 working days of paid annual leave per year.

        ## Security
        Passwords must be at least 14 characters and MFA is mandatory for VPN access.
        """;

    [Fact]
    public async Task Auth_rejects_anonymous_and_supports_refresh_rotation()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/workspaces", TestContext.Current.CancellationToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var email = $"refresh-{Guid.NewGuid():N}@test.local";
        var register = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Passw0rd!", displayName = "R" }, Json, TestContext.Current.CancellationToken);
        var auth = await register.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        var refreshToken = auth.GetProperty("refreshToken").GetString();

        var refreshed = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, Json, TestContext.Current.CancellationToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // the rotated token cannot be used twice
        var reused = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, Json, TestContext.Current.CancellationToken);
        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var badLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" }, Json,
            TestContext.Current.CancellationToken);
        badLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_ingest_and_chat_returns_grounded_streamed_answer_with_citations()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateAuthenticatedClientAsync();
        var workspaceId = await CreateWorkspaceAsync(client, "HR");

        var documents = await UploadAsync(client, workspaceId, "handbook.md", Handbook);
        documents.GetArrayLength().ShouldBe(1);
        documents[0].GetProperty("status").GetString().ShouldBe("Queued");

        await factory.DrainIngestionQueueAsync();

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId}/documents", Json, ct);
        list[0].GetProperty("status").GetString().ShouldBe("Indexed");
        list[0].GetProperty("chunkCount").GetInt32().ShouldBeGreaterThan(0);

        var events = await AskAsync(client, workspaceId, "How many days of annual leave do employees get?");

        events.Select(e => e.Event).ShouldContain("conversation");
        events.Select(e => e.Event).ShouldContain("sources");
        events.Last().Event.ShouldBe("done");

        var sources = events.First(e => e.Event == "sources").Data.GetProperty("citations");
        sources.GetArrayLength().ShouldBeGreaterThan(0);
        sources[0].GetProperty("fileName").GetString().ShouldBe("handbook.md");
        sources[0].GetProperty("section").GetString()!.ShouldContain("Annual Leave");

        var answer = string.Concat(events.Where(e => e.Event == "delta").Select(e => e.Data.GetProperty("text").GetString()));
        answer.ShouldContain("20 working days");
        answer.ShouldContain("[1]");

        var done = events.Last().Data;
        done.GetProperty("usage").GetProperty("outputTokens").GetInt32().ShouldBe(30);

        // conversation + feedback are persisted
        var conversationId = events.First(e => e.Event == "conversation").Data.GetProperty("conversationId").GetString();
        var messageId = done.GetProperty("messageId").GetString();
        var feedback = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/conversations/{conversationId}/messages/{messageId}/feedback",
            new { score = 1 }, Json, ct);
        feedback.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var usage = await client.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId}/usage", Json, ct);
        usage.GetProperty("answers").GetInt32().ShouldBe(1);
        usage.GetProperty("positiveFeedback").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Tenants_are_isolated_in_the_api_and_in_the_vector_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();

        var aliceWorkspace = await CreateWorkspaceAsync(alice, "Alice secrets");
        await UploadAsync(alice, aliceWorkspace, "secret.md", "# Project Falcon\nThe Falcon launch code is ZEBRA-42.");
        var bobWorkspace = await CreateWorkspaceAsync(bob, "Bob");
        await UploadAsync(bob, bobWorkspace, "bob.md", "# Bob notes\nNothing interesting here about launches.");
        await factory.DrainIngestionQueueAsync();

        // Bob cannot see Alice's workspace at all (404, not 403, to avoid id probing)
        (await bob.GetAsync($"/api/workspaces/{aliceWorkspace}/documents", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var askOther = await bob.PostAsJsonAsync($"/api/workspaces/{aliceWorkspace}/chat", new { question = "launch code?" }, Json, ct);
        askOther.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Searching Bob's own workspace never returns Alice's chunks, even for an exact keyword match
        var events = await AskAsync(bob, bobWorkspace, "What is the Falcon launch code ZEBRA-42?");
        var citations = events.First(e => e.Event == "sources").Data.GetProperty("citations");
        citations.EnumerateArray().ShouldAllBe(c => c.GetProperty("fileName").GetString() == "bob.md");
    }

    [Fact]
    public async Task Viewer_cannot_upload_and_duplicate_uploads_are_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await factory.CreateAuthenticatedClientAsync();
        var viewerEmail = $"viewer-{Guid.NewGuid():N}@test.local";
        var viewer = await factory.CreateAuthenticatedClientAsync(viewerEmail);
        var workspaceId = await CreateWorkspaceAsync(owner, "Shared");

        var add = await owner.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members",
            new { email = viewerEmail, role = "Viewer" }, Json, ct);
        add.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await viewer.GetAsync($"/api/workspaces/{workspaceId}/documents", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UploadRawAsync(viewer, workspaceId, "a.md", "hello")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await UploadRawAsync(owner, workspaceId, "a.md", "same content")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UploadRawAsync(owner, workspaceId, "b.md", "same content")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await UploadRawAsync(owner, workspaceId, "virus.exe", "MZ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_requests_return_validation_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/workspaces", new { name = "" }, Json, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        problem.GetProperty("errors").TryGetProperty("Name", out _).ShouldBeTrue();
    }

    private static async Task<string> CreateWorkspaceAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspaces", new { name }, Json, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var workspace = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return workspace.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, string workspaceId, string fileName, string content)
    {
        var response = await UploadRawAsync(client, workspaceId, fileName, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, string workspaceId, string fileName, string content)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "files", fileName } };
        return client.PostAsync($"/api/workspaces/{workspaceId}/documents", form, TestContext.Current.CancellationToken);
    }

    private static async Task<List<(string Event, JsonElement Data)>> AskAsync(HttpClient client, string workspaceId, string question)
    {
        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/chat", new { question }, Json,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = new List<(string, JsonElement)>();
        foreach (var block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var name = lines.FirstOrDefault(l => l.StartsWith("event:", StringComparison.Ordinal))?[6..].Trim() ?? "message";
            var data = string.Join('\n', lines.Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()));
            events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }
}
