using JevHrTriage.Api.Options;
using JevHrTriage.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TriageDemoOptions>(builder.Configuration.GetSection(TriageDemoOptions.SectionName));
builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Jev HR Triage Demo API", Version = "v1" });
    c.EnableAnnotations();
});

builder.Services.AddHttpClient<IOpenRouterDecisionsClient, OpenRouterDecisionsClient>((sp, client) =>
{
    var triage = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TriageDemoOptions>>().Value;
    var openRouter = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>().Value;
    client.BaseAddress = new Uri(openRouter.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, triage.LiveTimeoutSeconds));
});

builder.Services.AddSingleton<IJevDecisionProvider, JevDecisionProvider>();
builder.Services.AddSingleton<ITriageWorkflowService, TriageWorkflowService>();
builder.Services.AddSingleton<ISampleDataService, SampleDataService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/health", (Microsoft.Extensions.Options.IOptions<TriageDemoOptions> options) =>
{
    return Results.Ok(new
    {
        status = "ok",
        mode = options.Value.Mode,
        model = options.Value.Model,
        note = "Health endpoint does not call Jev or OpenRouter."
    });
})
.WithName("Health")
.WithTags("Health");

app.Run();
