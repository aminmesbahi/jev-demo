using JevHrTriage.Api.Models;
using JevHrTriage.Compare.Contenders;
using JevHrTriage.Compare.Data;

namespace JevHrTriage.Compare.Services;

public sealed class ComparisonRunRequest
{
    /// <summary>Sample ids to run; null or empty = all labeled samples.</summary>
    public List<string>? SampleIds { get; set; }

    /// <summary>Contender names; null or empty = every enabled contender.</summary>
    public List<string>? Contenders { get; set; }

    /// <summary>Ask each question this many times to measure consistency.</summary>
    public int Repeats { get; set; } = 1;

    /// <summary>Optional ad-hoc request (unlabeled), run alongside the samples.</summary>
    public TriageRequest? CustomRequest { get; set; }
}

public sealed class ComparisonReport
{
    public string Mode { get; set; } = "Mock";
    public bool IsSynthetic { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public long WallClockMs { get; set; }
    public int Repeats { get; set; }
    public string? ReferenceContender { get; set; }
    public List<ContenderInfo> Contenders { get; set; } = new();
    public List<ComparisonRow> Rows { get; set; } = new();
    public List<ContenderSummary> Summary { get; set; } = new();
}

public sealed class ComparisonRow
{
    public string SampleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public TriageRequest Request { get; set; } = new();
    public ExpectedAnswer? Expected { get; set; }
    public List<ComparisonCell> Cells { get; set; } = new();
}

public sealed class ComparisonCell
{
    public string Contender { get; set; } = string.Empty;
    public List<ContenderCall> Runs { get; set; } = new();

    /// <summary>First successful run's category is acceptable. Null when unlabeled or every run failed.</summary>
    public bool? CategoryCorrect { get; set; }

    /// <summary>All successful runs agree on category. Null with fewer than two successful runs.</summary>
    public bool? Consistent { get; set; }
}

public sealed class ContenderSummary
{
    public string Contender { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    public int Calls { get; set; }
    public int Errors { get; set; }

    // Quality, on labeled samples, over every successful run.
    public int LabeledRuns { get; set; }
    public decimal? CategoryAccuracy { get; set; }
    public decimal? MeanProbabilityOnCorrect { get; set; }
    public decimal? MultipleTopicsAccuracy { get; set; }
    public decimal? UrgencyAccuracy { get; set; }

    // Behaviour of the downstream policy.
    public decimal? AutoRouteRate { get; set; }
    public decimal? AgreementWithReference { get; set; }
    public decimal? ConsistencyRate { get; set; }
    public decimal? MeanCategorySpread { get; set; }

    // Performance.
    public long? LatencyP50Ms { get; set; }
    public long? LatencyP95Ms { get; set; }
    public long? LatencyMeanMs { get; set; }
    public bool LatencySimulated { get; set; }
    public decimal? TotalCostUsd { get; set; }
    public decimal? CostPer1kRequestsUsd { get; set; }
    public bool CostEstimated { get; set; }
    public int? MeanInputTokens { get; set; }
}
