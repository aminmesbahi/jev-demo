using JevHrTriage.Api.Options;
using JevHrTriage.Compare.Contenders;
using JevHrTriage.Compare.Data;
using JevHrTriage.Compare.Options;
using JevHrTriage.Compare.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CompareOptions>(builder.Configuration.GetSection(CompareOptions.SectionName));
builder.Services.Configure<TriageDemoOptions>(builder.Configuration.GetSection(TriageDemoOptions.SectionName));

// One named HttpClient per configured endpoint. No retry handler on purpose: retries would hide
// rate limits and inflate latency, and both are part of what this app is meant to show.
var compareOptions = builder.Configuration.GetSection(CompareOptions.SectionName).Get<CompareOptions>() ?? new CompareOptions();
foreach (var (name, endpoint) in compareOptions.Endpoints)
{
    builder.Services.AddHttpClient(ContenderFactory.HttpClientName(name), client =>
    {
        if (Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }

        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, compareOptions.TimeoutSeconds));
    });
}

builder.Services.AddSingleton<ContenderFactory>();
builder.Services.AddSingleton<ComparisonRunner>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/compare/config", (ContenderFactory factory, IOptions<CompareOptions> options) => Results.Ok(new
{
    mode = options.Value.IsLive ? "Live" : "Mock",
    isSynthetic = !options.Value.IsLive,
    referenceContender = options.Value.ReferenceContender,
    maxRepeats = options.Value.MaxRepeats,
    contenders = factory.Describe(),
    samples = LabeledSamples.All
}));

app.MapPost("/api/compare/run", async (ComparisonRunRequest request, ComparisonRunner runner, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await runner.RunAsync(request, cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(title: "Invalid comparison request", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapGet("/health", (IOptions<CompareOptions> options) => Results.Ok(new { status = "ok", mode = options.Value.Mode }));

app.Run();
