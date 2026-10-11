using System.Net;
using System.Text;
using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Compare.Contenders;
using JevHrTriage.Compare.Data;
using JevHrTriage.Compare.Options;
using JevHrTriage.Compare.Services;

namespace JevHrTriage.Tests;

public sealed class LlmAnswerParserTests
{
    [Fact]
    public void Parse_FencedJson_NormalizesAndDerivesTypedAnswers()
    {
        const string content = """
        ```json
        {
          "category": { "payroll": 0.6, "hr_system_access": 0.3, "other_unclear": 0.1 },
          "urgency": { "0": 0.0, "1": 0.0, "2": 1.0 },
          "multiple_topics": { "probability": 0.8 },
          "missing_information": "0.25"
        }
        ```
        """;

        var judgments = LlmAnswerParser.Parse(content);

        Assert.Equal("payroll", judgments.Category.Choice);
        Assert.Equal(6, judgments.Category.Probabilities.Count); // unmentioned options filled in with 0
        Assert.Equal(1m, judgments.Category.Probabilities.Values.Sum(), 2);
        Assert.Equal(2m, judgments.Urgency.Score);
        Assert.Equal(1m, judgments.Urgency.Confidence);
        Assert.Equal(0.8m, judgments.MultipleTopics.Noul);
        Assert.Equal(0.25m, judgments.MissingInfoInMessage.Noul);
    }

    [Fact]
    public void Parse_Prose_Throws()
    {
        Assert.Throws<FormatException>(() => LlmAnswerParser.Parse("This looks like a payroll question."));
    }

    [Fact]
    public void Parse_MissingQuestion_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => LlmAnswerParser.Parse("""{ "category": { "payroll": 1 } }"""));
        Assert.Contains("urgency", ex.Message);
    }
}

public sealed class ProbabilityMathTests
{
    [Fact]
    public void ChoiceConfidence_IsZeroForUniform_AndOneForCertain()
    {
        Assert.Equal(0m, ProbabilityMath.ChoiceConfidence([0.25m, 0.25m, 0.25m, 0.25m]));
        Assert.Equal(1m, ProbabilityMath.ChoiceConfidence([1m, 0m, 0m, 0m]));
    }

    [Fact]
    public void ScoreConfidence_PenalizesDistantMassMoreThanAdjacentMass()
    {
        var adjacent = ProbabilityMath.ScoreConfidence([0.0m, 0.5m, 0.5m]);
        var distant = ProbabilityMath.ScoreConfidence([0.5m, 0.0m, 0.5m]);

        Assert.True(adjacent > distant);
    }
}

public sealed class SystemOneContenderTests
{
    [Fact]
    public async Task EvaluateAsync_SendsSharedQuestions_WithConfiguredAuthHeader()
    {
        string? sentBody = null;
        string? sentKey = null;
        var handler = new StubHandler(async request =>
        {
            sentBody = await request.Content!.ReadAsStringAsync();
            sentKey = request.Headers.TryGetValues("api-key", out var v) ? v.Single() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "id": "dec-1",
                  "model": "microsoft-decision-1",
                  "answers": {
                    "category": { "type": "choice", "choice": "leave", "confidence": 0.9, "probabilities": { "leave": 0.92, "other_unclear": 0.08 } },
                    "urgency": { "type": "score", "score": 0.2, "confidence": 0.8, "probabilities": { "0": 0.8, "1": 0.2, "2": 0.0 } },
                    "multiple_topics": { "type": "noul", "noul": 0.05 },
                    "missing_information": { "type": "noul", "noul": 0.1 }
                  },
                  "usage": { "input_tokens": 400, "output_tokens": 0 }
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var endpoint = new EndpointOptions { BaseUrl = "https://example.test", Path = "/providers/microsoft/v1/systemone", AuthHeader = "api-key", ApiKey = "test-key" };
        var options = new ContenderOptions { Name = "Foundry", Model = "my-deployment", InputPricePerMTok = 0.042m };
        var contender = new SystemOneContender(
            new HttpClient(handler) { BaseAddress = new Uri(endpoint.BaseUrl) }, endpoint, options, new ContenderInfo { Name = "Foundry" });

        var call = await contender.EvaluateAsync(new TriageRequest { Message = "How many vacation days do I have left?" }, CancellationToken.None);

        Assert.True(call.Ok, call.Error);
        Assert.Equal("leave", call.Judgments!.Category.Choice);
        Assert.Equal("test-key", sentKey);

        using var doc = JsonDocument.Parse(sentBody!);
        Assert.Equal("my-deployment", doc.RootElement.GetProperty("model").GetString());
        Assert.True(doc.RootElement.GetProperty("questions").TryGetProperty("multiple_topics", out _));

        // No cost reported by the endpoint, so it is estimated from tokens and flagged as such.
        Assert.True(call.CostEstimated);
        Assert.Equal(400 * 0.042m / 1_000_000m, call.CostUsd);
    }

    [Fact]
    public async Task EvaluateAsync_ReportsHttpErrors_AsFailedCall()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("""{ "error": { "message": "Rate limit exceeded" } }""", Encoding.UTF8, "application/json")
        }));
        var endpoint = new EndpointOptions { BaseUrl = "https://example.test", Path = "/v1/systemone", AuthHeader = "" };
        var contender = new SystemOneContender(
            new HttpClient(handler) { BaseAddress = new Uri(endpoint.BaseUrl) }, endpoint, new ContenderOptions { Model = "m" }, new ContenderInfo());

        var call = await contender.EvaluateAsync(new TriageRequest { Message = "hello there" }, CancellationToken.None);

        Assert.False(call.Ok);
        Assert.Equal("HTTP 429: Rate limit exceeded", call.Error);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}

public sealed class ComparisonSummaryTests
{
    [Fact]
    public void Summarize_ScoresAccuracyAgreementAndLatency_OverSuccessfulRunsOnly()
    {
        var rows = new List<ComparisonRow>
        {
            Row(new ExpectedAnswer(["payroll"], false, false), Ok("payroll", 100), Ok("payroll", 1000)),
            Row(new ExpectedAnswer(["leave", "benefits"], true, null), Ok("benefits", 200), Ok("leave", 3000)),
            Row(new ExpectedAnswer(["onboarding"], false, false), Ok("other_unclear", 300), Failed(9000))
        };

        var reference = ComparisonRunner.Summarize(new ContenderInfo { Name = "ref" }, 0, rows, referenceColumn: 0);
        var other = ComparisonRunner.Summarize(new ContenderInfo { Name = "other" }, 1, rows, referenceColumn: 0);

        Assert.Equal(0.667m, reference.CategoryAccuracy);
        Assert.Null(reference.AgreementWithReference);
        Assert.Equal(200, reference.LatencyP50Ms);

        Assert.Equal(1m, other.CategoryAccuracy);      // two labeled successes, both acceptable
        Assert.Equal(1, other.Errors);
        Assert.Equal(0.5m, other.AgreementWithReference); // agrees on row 1, not row 2, row 3 failed
        Assert.Equal(3000, other.LatencyP95Ms);          // the 9s failure is excluded
    }

    [Fact]
    public void Percentile_UsesNearestRank()
    {
        long[] sorted = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        Assert.Equal(50, ComparisonRunner.Percentile(sorted, 0.50));
        Assert.Equal(100, ComparisonRunner.Percentile(sorted, 0.95));
        Assert.Null(ComparisonRunner.Percentile([], 0.5));
    }

    private static ComparisonRow Row(ExpectedAnswer expected, params ContenderCall[] perContender) => new()
    {
        Expected = expected,
        Cells = perContender.Select((call, i) => new ComparisonCell { Contender = $"c{i}", Runs = [call] }).ToList()
    };

    private static ContenderCall Ok(string category, long latencyMs) => new()
    {
        Ok = true,
        LatencyMs = latencyMs,
        Judgments = new JevJudgments
        {
            Category = new ChoiceJudgment { Choice = category, Confidence = 0.9m, Probabilities = new() { [category] = 0.9m } },
            MultipleTopics = new NoulJudgment { Noul = 0.1m }
        },
        Recommendation = new AppRecommendation { ReviewStatus = "AutoRoute" }
    };

    private static ContenderCall Failed(long latencyMs) => ContenderCall.Failed("boom", latencyMs);
}
