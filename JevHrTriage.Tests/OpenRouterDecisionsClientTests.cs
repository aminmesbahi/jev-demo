using System.Net;
using System.Text;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using JevHrTriage.Api.Services;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Tests;

public sealed class OpenRouterDecisionsClientTests
{
    [Fact]
    public async Task SubmitAsync_ParsesSuccessfulResponse()
    {
        const string json = """
        {
          "id": "gen-dec-1",
          "model": "typesafe/jev-1.13",
          "provider": "TypeSafe",
          "answers": {
            "category": {
              "type": "choice",
              "choice": "payroll",
              "confidence": 0.82,
              "probabilities": {
                "payroll": 0.85,
                "leave": 0.05,
                "other_unclear": 0.1
              }
            }
          },
          "usage": {
            "cost": 0.0001,
            "input_tokens": 120,
            "output_tokens": 40
          }
        }
        """;

        var client = BuildClient(HttpStatusCode.OK, json);
        var response = await client.SubmitAsync(new DecisionsRequest
        {
            Model = "typesafe/jev-1.13",
            State = "test",
            Questions = new Dictionary<string, object>
            {
                ["category"] = new { type = "choice", instructions = "test", criteria = new { payroll = "p" } }
            }
        }, CancellationToken.None);

        Assert.Equal("typesafe/jev-1.13", response.Model);
        Assert.True(response.Answers.ContainsKey("category"));
        Assert.Equal(120, response.Usage?.InputTokens);
    }

    [Fact]
    public async Task SubmitAsync_ThrowsRateLimitError()
    {
        const string errorJson = """
        {
          "error": {
            "code": 429,
            "message": "Rate limit exceeded"
          }
        }
        """;

        var client = BuildClient((HttpStatusCode)429, errorJson);

        var ex = await Assert.ThrowsAsync<TriageException>(() => client.SubmitAsync(new DecisionsRequest
        {
            Model = "typesafe/jev-1.13",
            State = "test",
            Questions = new Dictionary<string, object> { ["x"] = new { type = "noul", instructions = "i" } }
        }, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("Rate", ex.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_ThrowsForMalformedSuccessPayload()
    {
        const string malformed = """
        {
          "model": "typesafe/jev-1.13",
          "answers": {},
          "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;

        var client = BuildClient(HttpStatusCode.OK, malformed);

        var ex = await Assert.ThrowsAsync<TriageException>(() => client.SubmitAsync(new DecisionsRequest
        {
            Model = "typesafe/jev-1.13",
            State = "test",
            Questions = new Dictionary<string, object> { ["x"] = new { type = "noul", instructions = "i" } }
        }, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Contains("Malformed", ex.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_RetriesRateLimit_ThenSucceeds()
    {
        const string json = """
        {
          "id": "gen-dec-2",
          "model": "typesafe/jev-1.13",
          "answers": { "x": { "type": "noul", "noul": 0.2 } }
        }
        """;

        var calls = 0;
        var client = BuildClient(_ => ++calls == 1
            ? JsonResponse((HttpStatusCode)429, """{ "error": { "code": 429, "message": "slow down" } }""")
            : JsonResponse(HttpStatusCode.OK, json));

        var response = await client.SubmitAsync(NoulRequest(), CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("gen-dec-2", response.Id);
    }

    [Fact]
    public async Task SubmitAsync_GivesUp_AfterMaxRetries()
    {
        var calls = 0;
        var client = BuildClient(_ =>
        {
            calls++;
            return JsonResponse((HttpStatusCode)529, """{ "error": { "code": 529, "message": "Overloaded" } }""");
        });

        var ex = await Assert.ThrowsAsync<TriageException>(() => client.SubmitAsync(NoulRequest(), CancellationToken.None));

        Assert.Equal(3, calls); // first attempt + MaxRetries (2)
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task SubmitAsync_MapsValidationError_WithoutRetrying()
    {
        var calls = 0;
        var client = BuildClient(_ =>
        {
            calls++;
            return JsonResponse(HttpStatusCode.UnprocessableEntity, """{ "error": { "code": 422, "message": "questions.x.type is invalid" } }""");
        });

        var ex = await Assert.ThrowsAsync<TriageException>(() => client.SubmitAsync(NoulRequest(), CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Equal(502, ex.StatusCode);
        Assert.Equal("Invalid Decisions Request", ex.Title);
        Assert.Contains("questions.x.type", ex.Detail);
    }

    [Fact]
    public async Task SubmitAsync_MapsConnectionFailure_ToBadGateway()
    {
        var client = BuildClient(_ => throw new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<TriageException>(() => client.SubmitAsync(NoulRequest(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.Equal("Upstream Unreachable", ex.Title);
    }

    private static DecisionsRequest NoulRequest() => new()
    {
        Model = "typesafe/jev-1.13",
        State = "test",
        Questions = new Dictionary<string, object> { ["x"] = new { type = "noul", instructions = "i" } }
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static OpenRouterDecisionsClient BuildClient(HttpStatusCode statusCode, string responseBody)
        => BuildClient(_ => JsonResponse(statusCode, responseBody));

    private static OpenRouterDecisionsClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder))
        {
            BaseAddress = new Uri("https://openrouter.ai")
        };

        var options = Options.Create(new OpenRouterOptions
        {
            DecisionsPath = "/api/alpha/decisions",
            ApiKey = "test-key",
            RetryBaseDelayMs = 0
        });

        return new OpenRouterDecisionsClient(httpClient, options);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
