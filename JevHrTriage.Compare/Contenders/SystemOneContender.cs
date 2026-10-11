using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Services;
using JevHrTriage.Compare.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>
/// Any decision model that speaks the System One wire format (state + typed questions):
/// OpenRouter's Decisions API, Microsoft Foundry's /providers/microsoft/v1/systemone,
/// Ollama's /v1/systemone, and other compatible servers. Only the endpoint differs.
/// </summary>
public sealed class SystemOneContender : IContender
{
    private readonly HttpClient _http;
    private readonly EndpointOptions _endpoint;
    private readonly ContenderOptions _options;

    public SystemOneContender(HttpClient http, EndpointOptions endpoint, ContenderOptions options, ContenderInfo info)
    {
        _http = http;
        _endpoint = endpoint;
        _options = options;
        Info = info;
    }

    public ContenderInfo Info { get; }

    public async Task<ContenderCall> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        var payload = new DecisionsRequest
        {
            Model = _options.Model,
            State = TriageQuestions.BuildState(request),
            Questions = TriageQuestions.BuildQuestions()
        };

        var result = await ContenderHttp.PostJsonAsync(_http, _endpoint, payload, cancellationToken);
        if (!result.Ok)
        {
            return ContenderCall.Failed(ContenderHttp.DescribeError(result.StatusCode, result.Body), result.LatencyMs);
        }

        DecisionsResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<DecisionsResponse>(result.Body, ContenderHttp.JsonOptions);
        }
        catch (JsonException ex)
        {
            return ContenderCall.Failed($"Invalid JSON from endpoint: {ex.Message}", result.LatencyMs);
        }

        if (response is null || response.Answers.Count == 0)
        {
            return ContenderCall.Failed("Response contained no answers.", result.LatencyMs);
        }

        JevJudgments judgments;
        try
        {
            judgments = TriageQuestions.ParseJudgments(response.Answers);
        }
        catch (TriageException ex)
        {
            return ContenderCall.Failed(ex.Detail, result.LatencyMs);
        }

        var (cost, estimated) = ContenderHttp.ResolveCost(
            response.Usage?.Cost, response.Usage?.InputTokens, response.Usage?.OutputTokens, _options);

        return new ContenderCall
        {
            Ok = true,
            LatencyMs = result.LatencyMs,
            Judgments = judgments,
            ResolvedModel = response.Model,
            RequestId = response.Id,
            InputTokens = response.Usage?.InputTokens,
            OutputTokens = response.Usage?.OutputTokens,
            CostUsd = cost,
            CostEstimated = estimated
        };
    }
}
