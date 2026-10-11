using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JevHrTriage.Compare.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>HTTP plumbing shared by the live contenders: auth, timing and error text.</summary>
internal static class ContenderHttp
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed record Result(bool Ok, string Body, int StatusCode, long LatencyMs);

    public static async Task<Result> PostJsonAsync(
        HttpClient http, EndpointOptions endpoint, object payload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint.Path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        if (endpoint.RequiresKey)
        {
            var key = endpoint.ResolveApiKey();
            if (string.Equals(endpoint.AuthHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue(endpoint.AuthScheme, key);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(endpoint.AuthHeader, key);
            }
        }

        // Latency is measured around the full round trip, including reading the body,
        // because that is what the calling application actually waits for.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return new Result(response.IsSuccessStatusCode, body, (int)response.StatusCode, elapsed);
    }

    /// <summary>Best-effort extraction of an error message from the common error envelopes.</summary>
    public static string DescribeError(int statusCode, string body)
    {
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                detail = error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : error.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            }
            else if (root.TryGetProperty("detail", out var d))
            {
                detail = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            }
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = body.Length > 200 ? body[..200] + "…" : body;
        }

        return $"HTTP {statusCode}: {detail}";
    }

    public static string? Truncate(string? text, int max = 2000)
        => text is null || text.Length <= max ? text : text[..max] + "…";

    public static (decimal? Cost, bool Estimated) ResolveCost(decimal? reported, int? inputTokens, int? outputTokens, ContenderOptions options)
    {
        if (reported.HasValue)
        {
            return (reported, false);
        }

        if (options.InputPricePerMTok is null && options.OutputPricePerMTok is null)
        {
            return (null, false);
        }

        var cost = (inputTokens ?? 0) * (options.InputPricePerMTok ?? 0m) / 1_000_000m
                 + (outputTokens ?? 0) * (options.OutputPricePerMTok ?? 0m) / 1_000_000m;
        return (cost, true);
    }
}
