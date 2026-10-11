using System.Diagnostics;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Api.Services;

public sealed class JevDecisionProvider : IJevDecisionProvider
{
    private readonly IOpenRouterDecisionsClient _client;
    private readonly TriageDemoOptions _options;

    public JevDecisionProvider(IOpenRouterDecisionsClient client, IOptions<TriageDemoOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string Mode => string.Equals(_options.Mode, "Live", StringComparison.OrdinalIgnoreCase) ? "Live" : "Mock";

    public bool IsSynthetic => !string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);

    public async Task<JevDecisionSet> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            return new JevDecisionSet
            {
                Model = _options.Model,
                Provider = "MockProvider",
                LatencyMs = 7,
                Usage = null,
                Judgments = MockJudgments.Build(request)
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var decisionsRequest = new DecisionsRequest
        {
            Model = _options.Model,
            State = TriageQuestions.BuildState(request),
            Questions = TriageQuestions.BuildQuestions()
        };

        DecisionsResponse response;
        try
        {
            response = await _client.SubmitAsync(decisionsRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only HttpClient's own timeout lands here. If the caller cancelled, the exception
            // propagates untouched so it isn't misreported as an upstream timeout.
            throw new TriageException(504, "Upstream Timeout", "The OpenRouter request timed out.");
        }
        finally
        {
            stopwatch.Stop();
        }

        return new JevDecisionSet
        {
            Model = response.Model ?? _options.Model,
            RequestId = response.Id,
            Provider = response.Provider,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            Usage = new UsageSnapshot
            {
                CostUsd = response.Usage?.Cost,
                InputTokens = response.Usage?.InputTokens,
                OutputTokens = response.Usage?.OutputTokens,
                Provider = response.Provider
            },
            Judgments = TriageQuestions.ParseJudgments(response.Answers)
        };
    }
}
