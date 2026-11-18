using DocuMind.Application.Abstractions;
using DocuMind.Application.Features.Evaluation;
using DocuMind.Application.Features.Retrieval;
using DocuMind.Domain.Evaluation;
using DocuMind.Domain.Workspaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DocuMind.UnitTests.Retrieval;

public sealed class RetrievalTests
{
    private static RetrievedChunk Chunk(string id, string file = "a.md", string content = "text", double score = 1) =>
        new(Guid.Parse(id.PadLeft(32, '0')), Guid.Empty, file, 0, null, null, content, score);

    [Fact]
    public void Bm25_document_vector_is_sorted_and_saturates_term_frequency()
    {
        var vector = Bm25SparseEncoder.EncodeDocument("password password password policy");

        vector.Indices.ShouldBe(vector.Indices.Order().ToArray());
        vector.Values.Length.ShouldBe(2);
        // tf=3 must score higher than tf=1 but less than 3x (saturation)
        var max = vector.Values.Max();
        var min = vector.Values.Min();
        max.ShouldBeGreaterThan(min);
        (max / min).ShouldBeLessThan(3);
    }

    [Fact]
    public void Bm25_query_ignores_stop_words_in_english_and_turkish()
    {
        Bm25SparseEncoder.Tokenize("What is the password policy?").ShouldBe(["password", "policy"]);
        Bm25SparseEncoder.Tokenize("Yurt dışı çalışma için izin nedir").ShouldBe(["yurt", "dışı", "çalışma", "izin"]);
    }

    [Fact]
    public void Bm25_keeps_codes_and_versions_as_single_tokens()
    {
        Bm25SparseEncoder.Tokenize("Policy SEC-POL-001 v4.2").ShouldBe(["policy", "sec-pol-001", "v4.2"]);
    }

    [Fact]
    public void Same_term_maps_to_same_index_for_query_and_document()
    {
        var doc = Bm25SparseEncoder.EncodeDocument("YubiKey required");
        var query = Bm25SparseEncoder.EncodeQuery("yubikey");
        doc.Indices.ShouldContain(query.Indices.Single());
    }

    [Fact]
    public void Rrf_rewards_documents_ranked_high_in_multiple_lists()
    {
        var a = Chunk("a");
        var b = Chunk("b");
        var c = Chunk("c");

        var fused = ReciprocalRankFusion.Fuse([[a, b, c], [b, c, a], [b, a]]);

        fused[0].ChunkId.ShouldBe(b.ChunkId);
        fused.Count.ShouldBe(3);
    }

    [Fact]
    public void Context_builder_numbers_sources_and_respects_budget()
    {
        var chunks = Enumerable.Range(1, 10)
            .Select(i => Chunk(i.ToString(), $"doc{i}.md", string.Join(' ', Enumerable.Repeat("word", 200))))
            .ToList();

        var context = ContextBuilder.Build(chunks, tokenBudget: 500);

        context.Citations.Count.ShouldBe(2);
        context.Citations.Select(c => c.Number).ShouldBe([1, 2]);
        context.Text.ShouldStartWith("[1] (doc1.md)");
        context.Tokens.ShouldBeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task Retriever_fetches_extra_candidates_and_reranks_to_top_k()
    {
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedQueryAsync(default!, default).ReturnsForAnyArgs([1f, 0f]);
        var store = Substitute.For<IVectorStore>();
        store.SearchAsync(default!, default).ReturnsForAnyArgs(
            Enumerable.Range(1, 9).Select(i => new VectorHit(Guid.NewGuid(), Guid.Empty, "f.md", i, null, null, $"c{i}", 1.0 / i)).ToList());
        var reranker = Substitute.For<IReranker>();
        reranker.RerankAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(call => call.Arg<IReadOnlyList<RetrievedChunk>>().Reverse().Take(call.ArgAt<int>(2)).ToList());

        var retriever = new HybridRetriever(embeddings, store, Substitute.For<IQueryTransformer>(), reranker);
        var settings = RagSettings.Default with { TopK = 3, UseReranker = true, UseHybridSearch = true };

        var result = await retriever.RetrieveAsync(new RetrievalRequest(Guid.NewGuid(), "leave policy", settings), TestContext.Current.CancellationToken);

        await store.Received(1).SearchAsync(Arg.Is<VectorQuery>(q => q.Limit == 9 && q.Sparse != null), Arg.Any<CancellationToken>());
        result.Chunks.Count.ShouldBe(3);
        result.Chunks[0].Content.ShouldBe("c9");
    }

    [Fact]
    public async Task Retriever_skips_sparse_vector_when_hybrid_disabled()
    {
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedQueryAsync(default!, default).ReturnsForAnyArgs([1f]);
        var store = Substitute.For<IVectorStore>();
        store.SearchAsync(default!, default).ReturnsForAnyArgs(new List<VectorHit>());

        var retriever = new HybridRetriever(embeddings, store, Substitute.For<IQueryTransformer>(), Substitute.For<IReranker>());
        var settings = RagSettings.Default with { UseHybridSearch = false, UseReranker = false, TopK = 4 };

        await retriever.RetrieveAsync(new RetrievalRequest(Guid.NewGuid(), "q", settings), TestContext.Current.CancellationToken);

        await store.Received(1).SearchAsync(Arg.Is<VectorQuery>(q => q.Sparse == null && q.Limit == 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Llm_reranker_orders_by_model_scores_and_falls_back_on_garbage()
    {
        var candidates = new[] { Chunk("1", content: "a"), Chunk("2", content: "b"), Chunk("3", content: "c") };

        var good = new StubChatClient("""{"scores":[{"id":1,"score":2},{"id":2,"score":9},{"id":3,"score":5}]}""");
        var reranked = await new LlmReranker(good, NullLogger<LlmReranker>.Instance).RerankAsync("q", candidates, 2, TestContext.Current.CancellationToken);
        reranked.Select(c => c.Content).ShouldBe(["b", "c"]);

        var bad = new StubChatClient("I cannot do that");
        var fallback = await new LlmReranker(bad, NullLogger<LlmReranker>.Instance).RerankAsync("q", candidates, 2, TestContext.Current.CancellationToken);
        fallback.Select(c => c.Content).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Retrieval_metrics()
    {
        string[] expected = ["handbook.md", "policy.md"];
        RetrievalMetrics.Hit(expected, ["x.md", "policy.md"]).ShouldBeTrue();
        RetrievalMetrics.Recall(expected, ["x.md", "policy.md"]).ShouldBe(0.5);
        RetrievalMetrics.ReciprocalRank(expected, ["x.md", "policy.md"]).ShouldBe(0.5);
        RetrievalMetrics.ReciprocalRank(expected, ["x.md"]).ShouldBe(0);
    }

    [Fact]
    public void Eval_aggregate_ignores_missing_judge_scores()
    {
        var results = new List<EvalItemResult>
        {
            new("q1", ["a"], ["a"], true, 1, 1, "x", 4, null, 5, 100, null),
            new("q2", ["a"], ["b"], false, 0, 0, "y", 2, null, null, 300, null),
        };

        var metrics = EvalRunner.Aggregate(results, 42);

        metrics.HitRate.ShouldBe(0.5);
        metrics.Mrr.ShouldBe(0.5);
        metrics.Relevance.ShouldBe(3);
        metrics.Groundedness.ShouldBeNull();
        metrics.Coherence.ShouldBe(5);
        metrics.AvgLatencyMs.ShouldBe(200);
    }

    private sealed class StubChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
