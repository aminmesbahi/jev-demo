using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using JevHrTriage.Api.Services;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Tests;

public sealed class JevDecisionProviderTests
{
    private static readonly TriageRequest Request = new()
    {
        Subject = "Overtime missing",
        Message = "My overtime is missing from my latest paycheck."
    };

    [Fact]
    public async Task EvaluateAsync_MapsHttpClientTimeout_To504()
    {
        var provider = BuildLiveProvider(new StubClient(_ => throw new TaskCanceledException("HttpClient.Timeout elapsed")));

        var ex = await Assert.ThrowsAsync<TriageException>(() => provider.EvaluateAsync(Request, CancellationToken.None));

        Assert.Equal(504, ex.StatusCode);
    }

    [Fact]
    public async Task EvaluateAsync_LetsCallerCancellationPropagate()
    {
        using var cts = new CancellationTokenSource();
        var provider = BuildLiveProvider(new StubClient(ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EvaluateAsync(Request, cts.Token));
    }

    [Fact]
    public async Task EvaluateAsync_CarriesUpstreamRequestId()
    {
        var provider = BuildLiveProvider(new StubClient(_ => new DecisionsResponse
        {
            Id = "gen-dec-42",
            Model = "typesafe/jev-1.13",
            Answers = new Dictionary<string, JsonElement>
            {
                ["category"] = Json("""{ "type": "choice", "choice": "payroll", "confidence": 0.9, "probabilities": { "payroll": 0.92, "other_unclear": 0.08 } }"""),
                ["urgency"] = Json("""{ "type": "score", "score": 0.4, "confidence": 0.8, "probabilities": { "0": 0.7, "1": 0.2, "2": 0.1 } }"""),
                ["multiple_topics"] = Json("""{ "type": "noul", "noul": 0.1 }"""),
                ["missing_information"] = Json("""{ "type": "noul", "noul": 0.2 }""")
            }
        }));

        var result = await provider.EvaluateAsync(Request, CancellationToken.None);

        Assert.Equal("gen-dec-42", result.RequestId);
        Assert.Equal("payroll", result.Judgments.Category.Choice);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JevDecisionProvider BuildLiveProvider(IOpenRouterDecisionsClient client)
        => new(client, Options.Create(new TriageDemoOptions { Mode = "Live" }));

    private sealed class StubClient : IOpenRouterDecisionsClient
    {
        private readonly Func<CancellationToken, DecisionsResponse> _responder;

        public StubClient(Func<CancellationToken, DecisionsResponse> responder)
        {
            _responder = responder;
        }

        public Task<DecisionsResponse> SubmitAsync(DecisionsRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(cancellationToken));
    }
}
