using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using JevHrTriage.Api.Services;
using JevHrTriage.Compare.Contenders;
using JevHrTriage.Compare.Data;
using JevHrTriage.Compare.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Compare.Services;

/// <summary>
/// Asks every selected contender every selected question, applies the same routing policy to
/// each answer, and scores the results side by side.
/// </summary>
public sealed class ComparisonRunner
{
    private readonly ContenderFactory _factory;
    private readonly CompareOptions _options;
    private readonly TriagePolicy _policy;
    private readonly ILogger<ComparisonRunner> _logger;

    public ComparisonRunner(
        ContenderFactory factory,
        IOptions<CompareOptions> options,
        IOptions<TriageDemoOptions> triageOptions,
        ILogger<ComparisonRunner> logger)
    {
        _factory = factory;
        _options = options.Value;
        _policy = new TriagePolicy(triageOptions.Value);
        _logger = logger;
    }

    public async Task<ComparisonReport> RunAsync(ComparisonRunRequest run, CancellationToken cancellationToken)
    {
        var contenders = _factory.Create(run.Contenders is { Count: > 0 } ? run.Contenders : null);
        if (contenders.Count == 0)
        {
            throw new ArgumentException("No contenders selected.");
        }

        var samples = SelectSamples(run);
        if (samples.Count == 0)
        {
            throw new ArgumentException("No samples selected.");
        }

        var repeats = Math.Clamp(run.Repeats, 1, Math.Max(1, _options.MaxRepeats));
        var started = DateTimeOffset.UtcNow;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Every (sample, contender, repeat) is an independent call. A shared semaphore keeps the
        // number in flight bounded so we don't trip rate limits or skew latency with a burst.
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxParallelCalls));
        var rows = samples.Select(s => new ComparisonRow
        {
            SampleId = s.Id,
            Title = s.Title,
            Tag = s.Tag,
            Request = s.Request,
            Expected = s.Expected,
            Cells = contenders.Select(c => new ComparisonCell
            {
                Contender = c.Info.Name,
                Runs = Enumerable.Range(0, repeats).Select(_ => new ContenderCall()).ToList()
            }).ToList()
        }).ToList();

        var tasks = new List<Task>();
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < contenders.Count; c++)
            {
                for (var k = 0; k < repeats; k++)
                {
                    var (row, contender, cell, index) = (rows[r], contenders[c], rows[r].Cells[c], k);
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(cancellationToken);
                        try
                        {
                            cell.Runs[index] = await CallAsync(contender, row.Request, cancellationToken);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, cancellationToken));
                }
            }
        }

        await Task.WhenAll(tasks);
        clock.Stop();

        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
            {
                ScoreCell(row, cell);
            }
        }

        var referenceIndex = Math.Max(0, contenders.ToList().FindIndex(
            c => string.Equals(c.Info.Name, _options.ReferenceContender, StringComparison.OrdinalIgnoreCase)));
        var reference = contenders[referenceIndex];

        return new ComparisonReport
        {
            Mode = _options.IsLive ? "Live" : "Mock",
            IsSynthetic = !_options.IsLive,
            StartedAtUtc = started,
            WallClockMs = clock.ElapsedMilliseconds,
            Repeats = repeats,
            ReferenceContender = reference.Info.Name,
            Contenders = contenders.Select(c => c.Info).ToList(),
            Rows = rows,
            Summary = contenders.Select((c, i) => Summarize(c.Info, i, rows, referenceIndex)).ToList()
        };
    }

    private async Task<ContenderCall> CallAsync(IContender contender, TriageRequest request, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        long Elapsed() => (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        ContenderCall call;
        try
        {
            call = await contender.EvaluateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            call = ContenderCall.Failed($"Timed out after {_options.TimeoutSeconds}s.", Elapsed());
        }
        catch (HttpRequestException ex)
        {
            call = ContenderCall.Failed($"Endpoint unreachable: {ex.Message}", Elapsed());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Contender {Contender} failed unexpectedly.", contender.Info.Name);
            call = ContenderCall.Failed($"Unexpected error: {ex.Message}", Elapsed());
        }

        // The same deterministic policy runs on every model's judgments, so differences in the
        // recommendation come from the judgments alone.
        if (call is { Ok: true, Judgments: not null })
        {
            call.Recommendation = _policy.Recommend(request, call.Judgments);
        }

        return call;
    }

    private static List<LabeledSample> SelectSamples(ComparisonRunRequest run)
    {
        var wanted = run.SampleIds is { Count: > 0 } ? run.SampleIds.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var samples = LabeledSamples.All.Where(s => wanted is null || wanted.Contains(s.Id)).ToList();

        if (run.CustomRequest is { } custom && !string.IsNullOrWhiteSpace(custom.Message))
        {
            samples.Add(new LabeledSample("custom", "Your message", "custom", "custom", custom, Expected: null));
        }

        return samples;
    }

    private static void ScoreCell(ComparisonRow row, ComparisonCell cell)
    {
        var ok = cell.Runs.Where(r => r is { Ok: true, Judgments: not null }).ToList();
        if (ok.Count > 0 && row.Expected is { } expected)
        {
            cell.CategoryCorrect = IsAcceptable(expected, ok[0].Judgments!.Category.Choice);
        }

        if (ok.Count >= 2)
        {
            cell.Consistent = ok.Select(r => r.Judgments!.Category.Choice).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        }
    }

    private static bool IsAcceptable(ExpectedAnswer expected, string category)
        => expected.Categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    internal static ContenderSummary Summarize(ContenderInfo info, int column, IReadOnlyList<ComparisonRow> rows, int referenceColumn)
    {
        var cells = rows.Select(r => (Row: r, Cell: r.Cells[column])).ToList();
        var allRuns = cells.SelectMany(x => x.Cell.Runs).ToList();
        var okRuns = allRuns.Where(r => r is { Ok: true, Judgments: not null }).ToList();

        var labeled = cells
            .Where(x => x.Row.Expected is not null)
            .SelectMany(x => x.Cell.Runs.Where(r => r is { Ok: true, Judgments: not null }).Select(r => (Expected: x.Row.Expected!, Run: r)))
            .ToList();

        var urgencyLabeled = labeled.Where(x => x.Expected.Urgent.HasValue).ToList();

        // Only successful calls count toward latency: a refused connection or a timeout measures the failure, not the model.
        var latencies = okRuns.Select(r => r.LatencyMs).OrderBy(l => l).ToList();
        var costed = okRuns.Where(r => r.CostUsd.HasValue).ToList();

        decimal? agreement = null;
        if (column != referenceColumn)
        {
            var pairs = rows
                .Select(r => (Mine: FirstOk(r.Cells[column]), Ref: FirstOk(r.Cells[referenceColumn])))
                .Where(p => p.Mine is not null && p.Ref is not null)
                .ToList();
            agreement = Rate(pairs.Count(p => string.Equals(p.Mine!.Judgments!.Category.Choice, p.Ref!.Judgments!.Category.Choice, StringComparison.OrdinalIgnoreCase)), pairs.Count);
        }

        var consistencyCells = cells.Where(x => x.Cell.Consistent.HasValue).ToList();
        var spreads = cells
            .Select(x => x.Cell.Runs.Where(r => r is { Ok: true, Judgments: not null }).ToList())
            .Where(runs => runs.Count >= 2)
            .Select(runs =>
            {
                var choice = runs[0].Judgments!.Category.Choice;
                var p = runs.Select(r => r.Judgments!.Category.Probabilities.GetValueOrDefault(choice)).ToList();
                return p.Max() - p.Min();
            })
            .ToList();

        return new ContenderSummary
        {
            Contender = info.Name,
            Kind = info.Kind,
            Model = info.Model,
            Calls = allRuns.Count,
            Errors = allRuns.Count(r => !r.Ok),
            LabeledRuns = labeled.Count,
            CategoryAccuracy = Rate(labeled.Count(x => IsAcceptable(x.Expected, x.Run.Judgments!.Category.Choice)), labeled.Count),
            MeanProbabilityOnCorrect = labeled.Count == 0 ? null : Math.Round(labeled.Average(x =>
                x.Expected.Categories.Sum(c => x.Run.Judgments!.Category.Probabilities.GetValueOrDefault(c))), 3),
            MultipleTopicsAccuracy = Rate(labeled.Count(x => (x.Run.Judgments!.MultipleTopics.Noul >= 0.5m) == x.Expected.MultipleTopics), labeled.Count),
            UrgencyAccuracy = Rate(urgencyLabeled.Count(x => x.Run.Recommendation?.IsUrgent == x.Expected.Urgent), urgencyLabeled.Count),
            AutoRouteRate = Rate(okRuns.Count(r => r.Recommendation?.ReviewStatus == "AutoRoute"), okRuns.Count),
            AgreementWithReference = agreement,
            ConsistencyRate = Rate(consistencyCells.Count(x => x.Cell.Consistent == true), consistencyCells.Count),
            MeanCategorySpread = spreads.Count == 0 ? null : Math.Round(spreads.Average(), 3),
            LatencyP50Ms = Percentile(latencies, 0.50),
            LatencyP95Ms = Percentile(latencies, 0.95),
            LatencyMeanMs = latencies.Count == 0 ? null : (long)latencies.Average(),
            LatencySimulated = allRuns.Any(r => r.LatencySimulated),
            TotalCostUsd = costed.Count == 0 ? null : costed.Sum(r => r.CostUsd!.Value),
            CostPer1kRequestsUsd = costed.Count == 0 ? null : Math.Round(costed.Average(r => r.CostUsd!.Value) * 1000m, 6),
            CostEstimated = costed.Any(r => r.CostEstimated),
            MeanInputTokens = okRuns.Any(r => r.InputTokens.HasValue) ? (int)okRuns.Where(r => r.InputTokens.HasValue).Average(r => r.InputTokens!.Value) : null
        };
    }

    private static ContenderCall? FirstOk(ComparisonCell cell) => cell.Runs.FirstOrDefault(r => r is { Ok: true, Judgments: not null });

    private static decimal? Rate(int hits, int total) => total == 0 ? null : Math.Round((decimal)hits / total, 3);

    /// <summary>Nearest-rank percentile over an ascending list.</summary>
    internal static long? Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
