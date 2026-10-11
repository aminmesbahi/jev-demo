using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Api.Services;

public sealed class TriageWorkflowService : ITriageWorkflowService
{
    private readonly IJevDecisionProvider _provider;
    private readonly TriagePolicy _policy;
    private readonly ILogger<TriageWorkflowService> _logger;

    public TriageWorkflowService(IJevDecisionProvider provider, IOptions<TriageDemoOptions> options, ILogger<TriageWorkflowService> logger)
    {
        _provider = provider;
        _policy = new TriagePolicy(options.Value);
        _logger = logger;
    }

    public async Task<TriageResponse> AnalyzeAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new TriageException(400, "Invalid Input", "Message is required.");
        }

        var decisionSet = await _provider.EvaluateAsync(request, cancellationToken);
        var app = _policy.Recommend(request, decisionSet.Judgments);
        LogDecision(decisionSet, app);

        return new TriageResponse
        {
            Mode = _provider.Mode,
            IsSynthetic = _provider.IsSynthetic,
            SimulatedRoutingOnly = true,
            Model = decisionSet.Model,
            UpstreamRequestId = decisionSet.RequestId,
            UpstreamLatencyMs = decisionSet.LatencyMs,
            Usage = decisionSet.Usage,
            JevJudgments = decisionSet.Judgments,
            ApplicationRecommendation = app
        };
    }

    // Audit trail for threshold tuning. Logs judgments, thresholds and the outcome only;
    // the request text and employee fields are deliberately left out.
    private void LogDecision(JevDecisionSet decisionSet, AppRecommendation app)
    {
        var judgments = decisionSet.Judgments;
        _logger.LogInformation(
            "Triage decision. Mode={Mode} Model={Model} UpstreamRequestId={UpstreamRequestId} LatencyMs={LatencyMs} " +
            "Category={Category} CategoryProbabilities={CategoryProbabilities} Urgency={Urgency} " +
            "MultipleTopics={MultipleTopics} MissingInfo={MissingInfo} Queue={Queue} ReviewStatus={ReviewStatus} " +
            "ReviewReasons={ReviewReasons} AppliedRules={AppliedRules}",
            _provider.Mode,
            decisionSet.Model,
            decisionSet.RequestId,
            decisionSet.LatencyMs,
            judgments.Category.Choice,
            FormatProbabilities(judgments.Category.Probabilities),
            judgments.Urgency.Score,
            judgments.MultipleTopics.Noul,
            judgments.MissingInfoInMessage.Noul,
            app.SuggestedQueue,
            app.ReviewStatus,
            string.Join(" | ", app.ReviewReasons),
            string.Join(" | ", app.AppliedRules));
    }

    private static string FormatProbabilities(Dictionary<string, decimal> probabilities)
        => string.Join(", ", probabilities.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value:0.00}"));
}
