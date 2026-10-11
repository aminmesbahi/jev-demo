namespace JevHrTriage.Compare.Options;

public sealed class CompareOptions
{
    public const string SectionName = "Compare";

    /// <summary>"Mock" (synthetic, no keys needed) or "Live" (real API calls).</summary>
    public string Mode { get; set; } = "Mock";

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxParallelCalls { get; set; } = 6;
    public int MaxRepeats { get; set; } = 5;

    /// <summary>Contender whose answers the others are measured against for "agreement".</summary>
    public string ReferenceContender { get; set; } = "Jev 1.13";

    public Dictionary<string, EndpointOptions> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ContenderOptions> Contenders { get; set; } = new();

    public bool IsLive => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);
}

public sealed class EndpointOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Header that carries the key: "Authorization" (Bearer) or "api-key" (Azure). Empty = no auth (local runtimes).</summary>
    public string AuthHeader { get; set; } = "Authorization";
    public string AuthScheme { get; set; } = "Bearer";

    public string? ApiKey { get; set; }
    public string? ApiKeyEnvVar { get; set; }

    public bool RequiresKey => !string.IsNullOrWhiteSpace(AuthHeader);

    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        return string.IsNullOrWhiteSpace(ApiKeyEnvVar) ? null : Environment.GetEnvironmentVariable(ApiKeyEnvVar);
    }
}

public sealed class ContenderOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"SystemOne" (typed decision API) or "ChatLlm" (prompt-and-parse baseline).</summary>
    public string Kind { get; set; } = ContenderKinds.SystemOne;

    /// <summary>Key into <see cref="CompareOptions.Endpoints"/>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>Used to estimate cost when the endpoint doesn't report it (USD per million tokens).</summary>
    public decimal? InputPricePerMTok { get; set; }
    public decimal? OutputPricePerMTok { get; set; }
}

public static class ContenderKinds
{
    public const string SystemOne = "SystemOne";
    public const string ChatLlm = "ChatLlm";
}
