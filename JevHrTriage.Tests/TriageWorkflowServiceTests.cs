using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using JevHrTriage.Api.Services;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Tests;

public sealed class TriageWorkflowServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_AutoRoutes_WhenConfidenceAndFieldsAreStrong()
    {
        var provider = new StubProvider(new JevDecisionSet
        {
            Model = "typesafe/jev-1.13",
            LatencyMs = 12,
            Judgments = new JevJudgments
            {
                Category = new ChoiceJudgment
                {
                    Choice = "payroll",
                    Confidence = 0.92m,
                    Probabilities = new Dictionary<string, decimal>
                    {
                        ["payroll"] = 0.93m,
                        ["hr_system_access"] = 0.03m,
                        ["other_unclear"] = 0.02m
                    }
                },
                Urgency = new ScoreJudgment { Score = 1.10m, Confidence = 0.86m },
                MultipleTopics = new NoulJudgment { Noul = 0.10m },
                MissingInfoInMessage = new NoulJudgment { Noul = 0.10m }
            }
        });

        var service = BuildService(provider);
        var response = await service.AnalyzeAsync(new TriageRequest
        {
            Message = "Overtime was not included in this pay period.",
            EmployeeId = "EMP-1",
            PayPeriod = "2026-09-01 to 2026-09-15"
        }, CancellationToken.None);

        Assert.Equal("AutoRoute", response.ApplicationRecommendation.ReviewStatus);
        Assert.Equal("queue-payroll-ops", response.ApplicationRecommendation.SuggestedQueue);
        Assert.Empty(response.ApplicationRecommendation.ReviewReasons);
    }

    [Fact]
    public async Task AnalyzeAsync_Reviews_WhenMultipleTopicsDetected()
    {
        var provider = new StubProvider(new JevDecisionSet
        {
            Model = "typesafe/jev-1.13",
            LatencyMs = 9,
            Judgments = new JevJudgments
            {
                Category = new ChoiceJudgment
                {
                    Choice = "payroll",
                    Confidence = 0.88m,
                    Probabilities = new Dictionary<string, decimal>
                    {
                        ["payroll"] = 0.60m,
                        ["hr_system_access"] = 0.32m,
                        ["other_unclear"] = 0.06m
                    }
                },
                Urgency = new ScoreJudgment { Score = 1.90m, Confidence = 0.91m },
                MultipleTopics = new NoulJudgment { Noul = 0.94m },
                MissingInfoInMessage = new NoulJudgment { Noul = 0.20m }
            }
        });

        var service = BuildService(provider);
        var response = await service.AnalyzeAsync(new TriageRequest
        {
            Message = "Overtime is missing and I cannot access the HR portal.",
            EmployeeId = "EMP-2",
            PayPeriod = "2026-09-01 to 2026-09-15"
        }, CancellationToken.None);

        Assert.Equal("HumanReview", response.ApplicationRecommendation.ReviewStatus);
        Assert.Contains(response.ApplicationRecommendation.ReviewReasons, x => x.Contains("multiple topics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_AddsTemplateClarifications_ForMissingRequiredFields()
    {
        var provider = new StubProvider(new JevDecisionSet
        {
            Model = "typesafe/jev-1.13",
            LatencyMs = 11,
            Judgments = new JevJudgments
            {
                Category = new ChoiceJudgment
                {
                    Choice = "hr_system_access",
                    Confidence = 0.89m,
                    Probabilities = new Dictionary<string, decimal>
                    {
                        ["hr_system_access"] = 0.90m,
                        ["other_unclear"] = 0.05m,
                        ["payroll"] = 0.03m
                    }
                },
                Urgency = new ScoreJudgment { Score = 0.80m, Confidence = 0.81m },
                MultipleTopics = new NoulJudgment { Noul = 0.15m },
                MissingInfoInMessage = new NoulJudgment { Noul = 0.30m }
            }
        });

        var service = BuildService(provider);
        var response = await service.AnalyzeAsync(new TriageRequest
        {
            Message = "I cannot access the portal.",
            EmployeeId = "EMP-3"
        }, CancellationToken.None);

        Assert.Equal("HumanReview", response.ApplicationRecommendation.ReviewStatus);
        Assert.Contains("hrSystemName", response.ApplicationRecommendation.MissingRequiredFields);
        Assert.Contains(response.ApplicationRecommendation.ClarificationPrompts, p => p.Contains("which HR system", StringComparison.OrdinalIgnoreCase));
    }

    private static TriageWorkflowService BuildService(IJevDecisionProvider provider)
    {
        var options = Options.Create(new TriageDemoOptions
        {
            AutoRouteMinConfidence = 0.75m,
            ReviewIfCategoryGapBelow = 0.15m,
            ReviewIfMultipleTopicsAtLeast = 0.55m,
            ReviewIfMissingInfoNoulAtLeast = 0.60m,
            UrgentScoreThreshold = 1.6m,
            UrgentHighConfidenceThreshold = 0.80m
        });

        return new TriageWorkflowService(provider, options);
    }

    private sealed class StubProvider : IJevDecisionProvider
    {
        private readonly JevDecisionSet _decisionSet;

        public StubProvider(JevDecisionSet decisionSet)
        {
            _decisionSet = decisionSet;
        }

        public Task<JevDecisionSet> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_decisionSet);

        public string Mode => "Mock";
        public bool IsSynthetic => true;
    }
}
