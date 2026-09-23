namespace JevHrTriage.Api.Options;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai";
    public string DecisionsPath { get; set; } = "/api/alpha/decisions";
    public string? ApiKey { get; set; }
}
