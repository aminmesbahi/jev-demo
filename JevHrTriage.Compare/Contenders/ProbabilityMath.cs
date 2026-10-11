namespace JevHrTriage.Compare.Contenders;

/// <summary>
/// Confidence and score formulas as documented for System One decision models, so values
/// derived from an LLM's stated probabilities are comparable with native decision-model output.
/// </summary>
public static class ProbabilityMath
{
    /// <summary>Clamps to [0,1] and rescales so values sum to 1. Uniform if everything is zero.</summary>
    public static Dictionary<string, decimal> Normalize(IEnumerable<string> keys, IReadOnlyDictionary<string, decimal> raw)
    {
        var keyList = keys.ToList();
        var clamped = keyList.ToDictionary(k => k, k => raw.TryGetValue(k, out var v) ? Math.Clamp(v, 0m, 1m) : 0m);
        var sum = clamped.Values.Sum();

        return sum == 0m
            ? keyList.ToDictionary(k => k, _ => Math.Round(1m / keyList.Count, 4))
            : clamped.ToDictionary(p => p.Key, p => Math.Round(p.Value / sum, 4));
    }

    /// <summary>Choice confidence: (p_max − 1/n) / (1 − 1/n). 1 = all mass on one option, 0 = uniform.</summary>
    public static decimal ChoiceConfidence(IReadOnlyCollection<decimal> probabilities)
    {
        var n = probabilities.Count;
        if (n < 2)
        {
            return 1m;
        }

        var uniform = 1m / n;
        return Math.Round(Math.Clamp((probabilities.Max() - uniform) / (1m - uniform), 0m, 1m), 4);
    }

    /// <summary>Probability-weighted level index.</summary>
    public static decimal ExpectedLevel(IReadOnlyList<decimal> levelProbabilities)
        => Math.Round(levelProbabilities.Select((p, i) => p * i).Sum(), 4);

    /// <summary>
    /// Score confidence: 1 − (weighted distance from the most likely level) / (that distance under a
    /// uniform spread), floored at 0. Mass on neighbouring levels costs less than mass far away.
    /// </summary>
    public static decimal ScoreConfidence(IReadOnlyList<decimal> levelProbabilities)
    {
        var n = levelProbabilities.Count;
        if (n < 2)
        {
            return 1m;
        }

        var mode = 0;
        for (var i = 1; i < n; i++)
        {
            if (levelProbabilities[i] > levelProbabilities[mode])
            {
                mode = i;
            }
        }

        var spread = levelProbabilities.Select((p, i) => p * Math.Abs(i - mode)).Sum();
        var uniformSpread = Enumerable.Range(0, n).Sum(i => (decimal)Math.Abs(i - mode)) / n;
        return uniformSpread == 0m ? 1m : Math.Round(Math.Max(0m, 1m - spread / uniformSpread), 4);
    }
}
