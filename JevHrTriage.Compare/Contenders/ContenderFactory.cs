using JevHrTriage.Compare.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>Builds the contender line-up from configuration.</summary>
public sealed class ContenderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CompareOptions _options;

    public ContenderFactory(IHttpClientFactory httpClientFactory, IOptions<CompareOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public static string HttpClientName(string endpoint) => $"endpoint:{endpoint}";

    public IReadOnlyList<ContenderInfo> Describe() => _options.Contenders.Select(DescribeOne).ToList();

    public IReadOnlyList<IContender> Create(IEnumerable<string>? names)
    {
        var selected = names?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<IContender>();

        foreach (var options in _options.Contenders)
        {
            var wanted = selected is null ? options.Enabled : selected.Contains(options.Name);
            if (!wanted)
            {
                continue;
            }

            var info = DescribeOne(options);
            if (!_options.IsLive)
            {
                result.Add(new SyntheticContender(options, info));
                continue;
            }

            if (!info.Ready)
            {
                result.Add(new UnavailableContender(info));
                continue;
            }

            var endpoint = _options.Endpoints[options.Endpoint];
            var http = _httpClientFactory.CreateClient(HttpClientName(options.Endpoint));
            result.Add(options.Kind == ContenderKinds.ChatLlm
                ? new ChatLlmContender(http, endpoint, options, info)
                : new SystemOneContender(http, endpoint, options, info));
        }

        return result;
    }

    private ContenderInfo DescribeOne(ContenderOptions options)
    {
        string? notReady = null;
        if (!_options.Endpoints.TryGetValue(options.Endpoint, out var endpoint))
        {
            notReady = $"Unknown endpoint '{options.Endpoint}'.";
        }
        else if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out _))
        {
            notReady = $"Endpoint '{options.Endpoint}' has no BaseUrl configured.";
        }
        else if (endpoint.RequiresKey && string.IsNullOrWhiteSpace(endpoint.ResolveApiKey()))
        {
            notReady = $"No API key: set {endpoint.ApiKeyEnvVar ?? $"Compare:Endpoints:{options.Endpoint}:ApiKey"}.";
        }

        return new ContenderInfo
        {
            Name = options.Name,
            Kind = options.Kind,
            Endpoint = options.Endpoint,
            Model = options.Model,
            Notes = options.Notes,
            Enabled = options.Enabled,
            Ready = notReady is null,
            NotReadyReason = notReady
        };
    }

    /// <summary>Live-mode placeholder for a contender that can't be called; shows up as an error column.</summary>
    private sealed class UnavailableContender : IContender
    {
        public UnavailableContender(ContenderInfo info) => Info = info;

        public ContenderInfo Info { get; }

        public Task<ContenderCall> EvaluateAsync(Api.Models.TriageRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ContenderCall.Failed(Info.NotReadyReason ?? "Not configured."));
    }
}
