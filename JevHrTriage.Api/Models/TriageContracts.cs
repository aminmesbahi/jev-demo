using System.ComponentModel.DataAnnotations;

namespace JevHrTriage.Api.Models;

public sealed class TriageRequest
{
    [Required]
    [StringLength(6000, MinimumLength = 5)]
    public string Message { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Subject { get; set; }

    [StringLength(100)]
    public string? EmployeeId { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(100)]
    public string? EmploymentType { get; set; }

    [StringLength(100)]
    public string? HrSystemName { get; set; }

    [StringLength(100)]
    public string? LeaveType { get; set; }

    public DateOnly? LeaveStartDate { get; set; }
    public DateOnly? LeaveEndDate { get; set; }

    [StringLength(100)]
    public string? PayPeriod { get; set; }

    [StringLength(100)]
    public string? BenefitPlan { get; set; }

    public DateOnly? OnboardingStartDate { get; set; }

    [StringLength(100)]
    public string? ManagerId { get; set; }
}

public sealed class TriageResponse
{
    public string Mode { get; set; } = "Mock";
    public bool IsSynthetic { get; set; }
    public bool SimulatedRoutingOnly { get; set; } = true;
    public string Model { get; set; } = string.Empty;
    public string? UpstreamRequestId { get; set; }
    public long UpstreamLatencyMs { get; set; }
    public UsageSnapshot? Usage { get; set; }
    public JevJudgments JevJudgments { get; set; } = new();
    public AppRecommendation ApplicationRecommendation { get; set; } = new();
}

public sealed class UsageSnapshot
{
    public decimal? CostUsd { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Provider { get; set; }
}

public sealed class JevJudgments
{
    public ChoiceJudgment Category { get; set; } = new();
    public ScoreJudgment Urgency { get; set; } = new();
    public NoulJudgment MultipleTopics { get; set; } = new();
    public NoulJudgment MissingInfoInMessage { get; set; } = new();
}

public sealed class ChoiceJudgment
{
    public string Type { get; set; } = "choice";
    public string Choice { get; set; } = string.Empty;
    public decimal? Confidence { get; set; }
    public Dictionary<string, decimal> Probabilities { get; set; } = new();
    public bool IsSynthetic { get; set; }
}

public sealed class ScoreJudgment
{
    public string Type { get; set; } = "score";
    public decimal Score { get; set; }
    public decimal? Confidence { get; set; }
    public Dictionary<string, decimal> Probabilities { get; set; } = new();
    public Dictionary<string, string> Legend { get; set; } = new();
    public bool IsSynthetic { get; set; }
}

public sealed class NoulJudgment
{
    public string Type { get; set; } = "noul";
    public decimal Noul { get; set; }
    public bool IsSynthetic { get; set; }
}

public sealed class AppRecommendation
{
    public string SuggestedQueue { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = "AutoRoute";
    public bool IsUrgent { get; set; }
    public List<string> ReviewReasons { get; set; } = new();
    public List<string> MissingRequiredFields { get; set; } = new();
    public List<string> ClarificationPrompts { get; set; } = new();
    public List<string> AppliedRules { get; set; } = new();
}

public sealed class DemoSample
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CoverageTag { get; set; } = string.Empty;
    public TriageRequest Request { get; set; } = new();
}
