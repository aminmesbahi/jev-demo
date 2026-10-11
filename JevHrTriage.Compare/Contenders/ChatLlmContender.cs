using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Services;
using JevHrTriage.Compare.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>
/// Baseline: a general chat LLM asked the same four questions through an OpenAI-compatible
/// chat-completions endpoint, then made to report probabilities as JSON. This is the
/// prompt-and-parse approach that decision models replace.
/// </summary>
public sealed class ChatLlmContender : IContender
{
    private const string SystemPrompt =
        "You are a classifier for an HR service desk. You receive an employee request as JSON and a set of " +
        "typed questions as JSON. Answer every question with probabilities, never prose.\n" +
        "- choice question: a probability for every listed option, summing to 1.\n" +
        "- score question: a probability for every level index (\"0\" is the first criterion), summing to 1.\n" +
        "- noul question: the probability (0 to 1) that the statement is true.\n" +
        "Reply with a single JSON object and nothing else, exactly in this shape:\n" +
        LlmAnswerParser.ExpectedShape;

    private readonly HttpClient _http;
    private readonly EndpointOptions _endpoint;
    private readonly ContenderOptions _options;

    public ChatLlmContender(HttpClient http, EndpointOptions endpoint, ContenderOptions options, ContenderInfo info)
    {
        _http = http;
        _endpoint = endpoint;
        _options = options;
        Info = info;
    }

    public ContenderInfo Info { get; }

    public async Task<ContenderCall> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        var userPrompt =
            "Request:\n" + JsonSerializer.Serialize(TriageQuestions.BuildState(request), ContenderHttp.JsonOptions) +
            "\n\nQuestions:\n" + JsonSerializer.Serialize(TriageQuestions.BuildQuestions(), ContenderHttp.JsonOptions);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt }
            },
            ["temperature"] = 0,
            ["max_tokens"] = 1200,
            ["response_format"] = new { type = "json_object" },
            // OpenRouter extensions; other OpenAI-compatible servers ignore unknown fields.
            ["reasoning"] = new { effort = "low" },
            ["usage"] = new { include = true }
        };

        var result = await ContenderHttp.PostJsonAsync(_http, _endpoint, payload, cancellationToken);
        if (!result.Ok)
        {
            return ContenderCall.Failed(ContenderHttp.DescribeError(result.StatusCode, result.Body), result.LatencyMs);
        }

        string? content;
        string? model = null, id = null;
        int? inputTokens = null, outputTokens = null;
        decimal? reportedCost = null;
        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            var root = doc.RootElement;
            model = root.TryGetProperty("model", out var m) ? m.GetString() : null;
            id = root.TryGetProperty("id", out var i) ? i.GetString() : null;
            content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var pti) ? pti : null;
                outputTokens = usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var cti) ? cti : null;
                reportedCost = usage.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : null;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return ContenderCall.Failed($"Unexpected chat-completions response: {ex.Message}", result.LatencyMs);
        }

        var (cost, estimated) = ContenderHttp.ResolveCost(reportedCost, inputTokens, outputTokens, _options);
        var call = new ContenderCall
        {
            LatencyMs = result.LatencyMs,
            ResolvedModel = model,
            RequestId = id,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = cost,
            CostEstimated = estimated,
            RawOutput = ContenderHttp.Truncate(content)
        };

        if (string.IsNullOrWhiteSpace(content))
        {
            call.Error = "Model returned empty content.";
            return call;
        }

        try
        {
            call.Judgments = LlmAnswerParser.Parse(content);
            call.Ok = true;
        }
        catch (FormatException ex)
        {
            call.Error = $"Could not parse model output: {ex.Message}";
        }

        return call;
    }
}
