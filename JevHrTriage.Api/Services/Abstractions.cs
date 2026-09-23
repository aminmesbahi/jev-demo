using JevHrTriage.Api.Models;

namespace JevHrTriage.Api.Services;

public interface IOpenRouterDecisionsClient
{
    Task<DecisionsResponse> SubmitAsync(DecisionsRequest request, CancellationToken cancellationToken);
}

public interface IJevDecisionProvider
{
    Task<JevDecisionSet> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken);
    string Mode { get; }
    bool IsSynthetic { get; }
}

public interface ITriageWorkflowService
{
    Task<TriageResponse> AnalyzeAsync(TriageRequest request, CancellationToken cancellationToken);
}

public interface ISampleDataService
{
    IReadOnlyCollection<DemoSample> GetSamples();
}

public sealed class TriageException : Exception
{
    public TriageException(int statusCode, string title, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
    }

    public int StatusCode { get; }
    public string Title { get; }
    public string Detail { get; }
}
