using System.Globalization;
using System.Text;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Services;
using JevHrTriage.Compare.Options;

namespace JevHrTriage.Compare.Contenders;

/// <summary>
/// Mock-mode stand-in so the comparison page works without API keys. Every contender gets the
/// same keyword-based judgments plus deterministic noise, and a simulated latency. None of it
/// reflects how the named model actually performs; results are flagged as synthetic throughout.
/// </summary>
public sealed class SyntheticContender : IContender
{
    private readonly ContenderOptions _options;

    public SyntheticContender(ContenderOptions options, ContenderInfo info)
    {
        _options = options;
        Info = info;
    }

    public ContenderInfo Info { get; }

    public Task<ContenderCall> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        var random = new Random(StableSeed(Info.Name, request.Message));
        var judgments = MockJudgments.Build(request);
        Perturb(judgments, random);

        var isLlm = Info.Kind == ContenderKinds.ChatLlm;
        var inputTokens = (request.Message.Length + (request.Subject?.Length ?? 0)) / 4 + (isLlm ? 520 : 310);
        var outputTokens = isLlm ? random.Next(60, 140) : 0;
        var (cost, _) = ContenderHttp.ResolveCost(null, inputTokens, outputTokens, _options);

        return Task.FromResult(new ContenderCall
        {
            Ok = true,
            IsSynthetic = true,
            LatencySimulated = true,
            LatencyMs = isLlm ? random.Next(700, 2400) : random.Next(70, 260),
            Judgments = judgments,
            ResolvedModel = Info.Model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = cost,
            CostEstimated = cost.HasValue
        });
    }

    private static void Perturb(JevJudgments judgments, Random random)
    {
        var noisy = judgments.Category.Probabilities.ToDictionary(
            p => p.Key, p => Math.Max(0m, p.Value + Noise(random, 0.10m)));
        var category = ProbabilityMath.Normalize(noisy.Keys, noisy);
        judgments.Category.Probabilities = category;
        judgments.Category.Choice = category.OrderByDescending(p => p.Value).First().Key;
        judgments.Category.Confidence = ProbabilityMath.ChoiceConfidence(category.Values);

        var levelKeys = judgments.Urgency.Probabilities.Keys.OrderBy(k => int.Parse(k, CultureInfo.InvariantCulture)).ToList();
        var noisyLevels = levelKeys.ToDictionary(k => k, k => Math.Max(0m, judgments.Urgency.Probabilities[k] + Noise(random, 0.08m)));
        var urgency = ProbabilityMath.Normalize(levelKeys, noisyLevels);
        var levels = levelKeys.Select(k => urgency[k]).ToList();
        judgments.Urgency.Probabilities = urgency;
        judgments.Urgency.Score = ProbabilityMath.ExpectedLevel(levels);
        judgments.Urgency.Confidence = ProbabilityMath.ScoreConfidence(levels);

        judgments.MultipleTopics.Noul = Math.Round(Math.Clamp(judgments.MultipleTopics.Noul + Noise(random, 0.12m), 0m, 1m), 4);
        judgments.MissingInfoInMessage.Noul = Math.Round(Math.Clamp(judgments.MissingInfoInMessage.Noul + Noise(random, 0.12m), 0m, 1m), 4);
    }

    private static decimal Noise(Random random, decimal amplitude)
        => ((decimal)random.NextDouble() * 2m - 1m) * amplitude;

    // string.GetHashCode is randomized per process; FNV-1a keeps mock output identical across runs.
    private static int StableSeed(string name, string message)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes($"{name}|{message}"))
            {
                hash = (hash ^ b) * 16777619u;
            }

            return (int)hash;
        }
    }
}
