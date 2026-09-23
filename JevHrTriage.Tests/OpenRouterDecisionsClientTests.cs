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

    private static OpenRouterDecisionsClient BuildClient(HttpStatusCode statusCode, string responseBody)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai")
        };

        var options = Options.Create(new OpenRouterOptions
        {
            DecisionsPath = "/api/alpha/decisions",
            ApiKey = "test-key"
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
