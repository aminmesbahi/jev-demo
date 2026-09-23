using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Api.Services;

public sealed class OpenRouterDecisionsClient : IOpenRouterDecisionsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;

    public OpenRouterDecisionsClient(HttpClient httpClient, IOptions<OpenRouterOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<DecisionsResponse> SubmitAsync(DecisionsRequest request, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        using var message = new HttpRequestMessage(HttpMethod.Post, _options.DecisionsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildError(response.StatusCode, body);
        }

        DecisionsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DecisionsResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new TriageException(502, "Malformed Upstream Response", $"OpenRouter returned invalid JSON: {ex.Message}");
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Model) || parsed.Answers.Count == 0)
        {
            throw new TriageException(502, "Malformed Upstream Response", "OpenRouter response did not include required decision fields.");
        }

        return parsed;
    }

    private string ResolveApiKey()
    {
        var fromConfig = _options.ApiKey;
        var fromEnv = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var apiKey = string.IsNullOrWhiteSpace(fromConfig) ? fromEnv : fromConfig;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TriageException(500, "Missing Configuration", "OPENROUTER_API_KEY is required in Live mode.");
        }

        return apiKey;
    }

    private static TriageException BuildError(HttpStatusCode statusCode, string body)
    {
        string? message = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<OpenRouterErrorEnvelope>(body, JsonOptions);
            message = envelope?.Error?.Message;
        }
        catch (JsonException)
        {
            message = null;
        }

        var detail = string.IsNullOrWhiteSpace(message) ? "OpenRouter returned an error." : message;

        return statusCode switch
        {
            HttpStatusCode.BadRequest => new TriageException(400, "Invalid Decisions Request", detail),
            HttpStatusCode.Unauthorized => new TriageException(401, "Authentication Failed", detail),
            HttpStatusCode.PaymentRequired => new TriageException(402, "Insufficient Credits", detail),
            HttpStatusCode.Forbidden => new TriageException(403, "Forbidden", detail),
            HttpStatusCode.NotFound => new TriageException(404, "Endpoint Not Found", detail),
            HttpStatusCode.RequestEntityTooLarge => new TriageException(413, "Payload Too Large", detail),
            (HttpStatusCode)429 => new TriageException(429, "Rate Limited", detail),
            HttpStatusCode.BadGateway => new TriageException(502, "Provider Error", detail),
            HttpStatusCode.ServiceUnavailable => new TriageException(503, "Service Unavailable", detail),
            (HttpStatusCode)524 => new TriageException(504, "Upstream Timeout", detail),
            (HttpStatusCode)529 => new TriageException(503, "Provider Overloaded", detail),
            _ => new TriageException(502, "Upstream Failure", detail)
        };
    }
}
