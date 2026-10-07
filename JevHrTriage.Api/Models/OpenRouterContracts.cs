using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevHrTriage.Api.Models;

public sealed class DecisionsRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public object State { get; set; } = new();

    [JsonPropertyName("questions")]
    public Dictionary<string, object> Questions { get; set; } = new();
}

public sealed class DecisionsResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("answers")]
    public Dictionary<string, JsonElement> Answers { get; set; } = new();

    [JsonPropertyName("usage")]
    public DecisionsUsage? Usage { get; set; }
}

public sealed class DecisionsUsage
{
    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }
}

public sealed class OpenRouterErrorEnvelope
{
    [JsonPropertyName("error")]
    public OpenRouterErrorData? Error { get; set; }
}

public sealed class OpenRouterErrorData
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class JevDecisionSet
{
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string? Provider { get; set; }
    public long LatencyMs { get; set; }
    public UsageSnapshot? Usage { get; set; }
    public JevJudgments Judgments { get; set; } = new();
}
