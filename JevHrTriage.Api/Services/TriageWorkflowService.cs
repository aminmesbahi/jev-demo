using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Api.Services;

public sealed class TriageWorkflowService : ITriageWorkflowService
{
    private static readonly Dictionary<string, string> CategoryToQueue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["payroll"] = "queue-payroll-ops",
        ["leave"] = "queue-people-ops-leave",
        ["benefits"] = "queue-benefits-admin",
        ["onboarding"] = "queue-hr-onboarding",
        ["hr_system_access"] = "queue-hris-support",
        ["other_unclear"] = "queue-hr-general-review"
    };

    private static readonly Dictionary<string, string[]> RequiredFieldsByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["payroll"] = ["employeeId", "payPeriod"],
        ["leave"] = ["employeeId", "leaveType", "leaveStartDate", "leaveEndDate"],
        ["benefits"] = ["employeeId", "benefitPlan"],
        ["onboarding"] = ["employeeId", "onboardingStartDate", "managerId"],
        ["hr_system_access"] = ["employeeId", "hrSystemName"]
    };

    private static readonly Dictionary<string, string> ClarificationTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["employeeId"] = "Please provide your employee ID so we can locate your HR profile.",
        ["payPeriod"] = "Please share the affected pay period (for example, 2026-09-01 to 2026-09-15).",
        ["leaveType"] = "Please specify the leave type (vacation, sick leave, parental leave, etc.).",
        ["leaveStartDate"] = "Please provide the leave start date.",
        ["leaveEndDate"] = "Please provide the leave end date.",
        ["benefitPlan"] = "Please specify the benefit plan or program this request is about.",
        ["onboardingStartDate"] = "Please provide the onboarding or start date for the new hire.",
        ["managerId"] = "Please provide the manager identifier for this onboarding request.",
        ["hrSystemName"] = "Please specify which HR system you cannot access (for example, Workday)."
    };

    private readonly IJevDecisionProvider _provider;
    private readonly TriageDemoOptions _options;

    public TriageWorkflowService(IJevDecisionProvider provider, IOptions<TriageDemoOptions> options)
    {
        _provider = provider;
        _options = options.Value;
    }

    public async Task<TriageResponse> AnalyzeAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new TriageException(400, "Invalid Input", "Message is required.");
        }

        var decisionSet = await _provider.EvaluateAsync(request, cancellationToken);
        var category = decisionSet.Judgments.Category.Choice;

        if (!CategoryToQueue.TryGetValue(category, out var queue))
        {
            category = "other_unclear";
            queue = CategoryToQueue[category];
        }

        var app = BuildRecommendation(request, decisionSet.Judgments, category, queue);

        return new TriageResponse
        {
            Mode = _provider.Mode,
            IsSynthetic = _provider.IsSynthetic,
            SimulatedRoutingOnly = true,
            Model = decisionSet.Model,
            UpstreamLatencyMs = decisionSet.LatencyMs,
            Usage = decisionSet.Usage,
            JevJudgments = decisionSet.Judgments,
            ApplicationRecommendation = app
        };
    }

    private AppRecommendation BuildRecommendation(TriageRequest request, JevJudgments judgments, string category, string queue)
    {
        var recommendation = new AppRecommendation
        {
            SuggestedQueue = queue,
            IsUrgent = judgments.Urgency.Score >= _options.UrgentScoreThreshold,
            ReviewStatus = "AutoRoute"
        };

        var reviewReasons = recommendation.ReviewReasons;
        var rules = recommendation.AppliedRules;

        rules.Add("Queue is selected from explicit C# category-to-queue mapping, not from model team prediction.");

        var categoryConfidence = judgments.Category.Confidence ?? 0m;
        rules.Add($"Category confidence = {categoryConfidence:0.00}; auto-route threshold = {_options.AutoRouteMinConfidence:0.00}.");
        if (categoryConfidence < _options.AutoRouteMinConfidence)
        {
            reviewReasons.Add("Low category confidence.");
        }

        var topGap = ComputeTopProbabilityGap(judgments.Category.Probabilities);
        rules.Add($"Top category probability gap = {topGap:0.00}; review if below {_options.ReviewIfCategoryGapBelow:0.00}.");
        if (topGap < _options.ReviewIfCategoryGapBelow)
        {
            reviewReasons.Add("Category probabilities are too close (ambiguous category). ");
        }

        rules.Add($"Multiple topics noul = {judgments.MultipleTopics.Noul:0.00}; review threshold = {_options.ReviewIfMultipleTopicsAtLeast:0.00}.");
        if (judgments.MultipleTopics.Noul >= _options.ReviewIfMultipleTopicsAtLeast)
        {
            reviewReasons.Add("Message appears to contain multiple topics.");
        }

        rules.Add($"Message-level missing-info noul = {judgments.MissingInfoInMessage.Noul:0.00}; review threshold = {_options.ReviewIfMissingInfoNoulAtLeast:0.00}.");
        if (judgments.MissingInfoInMessage.Noul >= _options.ReviewIfMissingInfoNoulAtLeast)
        {
            reviewReasons.Add("Message text appears underspecified.");
        }

        if (recommendation.IsUrgent)
        {
            var urgencyConfidence = judgments.Urgency.Confidence ?? 0m;
            rules.Add($"Urgency score = {judgments.Urgency.Score:0.00}; urgent threshold = {_options.UrgentScoreThreshold:0.00}; urgency confidence = {urgencyConfidence:0.00}.");
            if (urgencyConfidence < _options.UrgentHighConfidenceThreshold)
            {
                reviewReasons.Add("Urgent result has moderate confidence.");
            }
        }

        var missing = DetermineMissingFields(request, category);
        recommendation.MissingRequiredFields = missing;

        if (missing.Count > 0)
        {
            rules.Add("Required fields are validated in deterministic C# rules by category.");
            reviewReasons.Add("Required structured fields are missing.");
            recommendation.ClarificationPrompts = missing
                .Select(m => ClarificationTemplates[m])
                .ToList();
        }

        if (string.Equals(category, "other_unclear", StringComparison.OrdinalIgnoreCase))
        {
            reviewReasons.Add("Category is Other/Unclear.");
        }

        recommendation.ReviewStatus = reviewReasons.Count > 0 ? "HumanReview" : "AutoRoute";
        return recommendation;
    }

    internal static decimal ComputeTopProbabilityGap(Dictionary<string, decimal> probabilities)
    {
        if (probabilities.Count < 2)
        {
            return 1m;
        }

        var top = probabilities.Values.OrderByDescending(v => v).Take(2).ToArray();
        return top[0] - top[1];
    }

    private static List<string> DetermineMissingFields(TriageRequest request, string category)
    {
        if (!RequiredFieldsByCategory.TryGetValue(category, out var requiredFields))
        {
            return new List<string>();
        }

        var missing = new List<string>();
        foreach (var field in requiredFields)
        {
            var hasValue = field switch
            {
                "employeeId" => !string.IsNullOrWhiteSpace(request.EmployeeId),
                "payPeriod" => !string.IsNullOrWhiteSpace(request.PayPeriod),
                "leaveType" => !string.IsNullOrWhiteSpace(request.LeaveType),
                "leaveStartDate" => request.LeaveStartDate.HasValue,
                "leaveEndDate" => request.LeaveEndDate.HasValue,
                "benefitPlan" => !string.IsNullOrWhiteSpace(request.BenefitPlan),
                "onboardingStartDate" => request.OnboardingStartDate.HasValue,
                "managerId" => !string.IsNullOrWhiteSpace(request.ManagerId),
                "hrSystemName" => !string.IsNullOrWhiteSpace(request.HrSystemName),
                _ => true
            };

            if (!hasValue)
            {
                missing.Add(field);
            }
        }

        return missing;
    }
}
