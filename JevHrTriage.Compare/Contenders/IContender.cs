using JevHrTriage.Api.Models;
using JevHrTriage.Compare.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>One model behind one endpoint, asked the shared triage questions.</summary>
public interface IContender
{
    ContenderInfo Info { get; }
    Task<ContenderCall> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken);
}

public sealed class ContenderInfo
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = ContenderKinds.SystemOne;
    public string Endpoint { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public bool Enabled { get; init; }

    /// <summary>False when the endpoint needs a key that isn't set, or has no base URL.</summary>
    public bool Ready { get; init; }
    public string? NotReadyReason { get; init; }
}

/// <summary>The outcome of a single call: judgments on success, an error message otherwise.</summary>
public sealed class ContenderCall
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public long LatencyMs { get; set; }
    public bool LatencySimulated { get; set; }
    public bool IsSynthetic { get; set; }

    public JevJudgments? Judgments { get; set; }
    public AppRecommendation? Recommendation { get; set; }

    public string? ResolvedModel { get; set; }
    public string? RequestId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public bool CostEstimated { get; set; }

    /// <summary>Raw model text for the chat baseline, kept (truncated) so parse failures can be inspected.</summary>
    public string? RawOutput { get; set; }

    public static ContenderCall Failed(string error, long latencyMs = 0) => new() { Ok = false, Error = error, LatencyMs = latencyMs };
}
