using System.Globalization;
using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Services;

namespace JevHrTriage.Compare.Contenders;

/// <summary>
/// Turns a chat model's JSON answer into the same typed judgments a decision model returns.
/// This is the "prompt-and-parse" step decision models make unnecessary, so failures here are
/// reported as errors rather than patched over.
/// </summary>
public static class LlmAnswerParser
{
    public const string ExpectedShape = """
    {
      "category": { "payroll": 0.0, "leave": 0.0, "benefits": 0.0, "onboarding": 0.0, "hr_system_access": 0.0, "other_unclear": 0.0 },
      "urgency": { "0": 0.0, "1": 0.0, "2": 0.0 },
      "multiple_topics": 0.0,
      "missing_information": 0.0
    }
    """;

    public static JevJudgments Parse(string content)
    {
        var json = StripCodeFence(content);

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Output was not valid JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Output was not a JSON object.");
        }

        var categoryKeys = TriageQuestions.CategoryCriteria.Keys.ToList();
        var categoryRaw = ReadDistribution(root, TriageQuestions.Category);
        var unknown = categoryRaw.Keys.Except(categoryKeys).ToList();
        if (unknown.Count > 0 && categoryRaw.Keys.Intersect(categoryKeys).Count() == 0)
        {
            throw new FormatException($"Category used unknown options: {string.Join(", ", unknown)}.");
        }

        var category = ProbabilityMath.Normalize(categoryKeys, categoryRaw);
        var choice = category.OrderByDescending(p => p.Value).First().Key;

        var levelKeys = Enumerable.Range(0, TriageQuestions.UrgencyCriteria.Count)
            .Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var urgency = ProbabilityMath.Normalize(levelKeys, ReadDistribution(root, TriageQuestions.Urgency));
        var levels = levelKeys.Select(k => urgency[k]).ToList();

        return new JevJudgments
        {
            Category = new ChoiceJudgment
            {
                Choice = choice,
                Confidence = ProbabilityMath.ChoiceConfidence(category.Values),
                Probabilities = category
            },
            Urgency = new ScoreJudgment
            {
                Score = ProbabilityMath.ExpectedLevel(levels),
                Confidence = ProbabilityMath.ScoreConfidence(levels),
                Probabilities = urgency,
                Legend = levelKeys.ToDictionary(k => k, k => TriageQuestions.UrgencyCriteria[int.Parse(k, CultureInfo.InvariantCulture)])
            },
            MultipleTopics = new NoulJudgment { Noul = ReadProbability(root, TriageQuestions.MultipleTopics) },
            MissingInfoInMessage = new NoulJudgment { Noul = ReadProbability(root, TriageQuestions.MissingInformation) }
        };
    }

    private static Dictionary<string, decimal> ReadDistribution(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var elem) || elem.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Missing '{key}' probability object.");
        }

        var result = new Dictionary<string, decimal>();
        foreach (var p in elem.EnumerateObject())
        {
            if (TryReadNumber(p.Value, out var value))
            {
                result[p.Name] = value;
            }
        }

        if (result.Count == 0)
        {
            throw new FormatException($"'{key}' contained no numeric probabilities.");
        }

        return result;
    }

    private static decimal ReadProbability(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var elem))
        {
            throw new FormatException($"Missing '{key}' probability.");
        }

        // Accept a bare number or { "probability": x } / { "true": x }, which models produce regularly.
        if (elem.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "probability", "probability_true", "true", "p" })
            {
                if (elem.TryGetProperty(name, out var inner) && TryReadNumber(inner, out var nested))
                {
                    return Math.Clamp(nested, 0m, 1m);
                }
            }
        }

        if (TryReadNumber(elem, out var value))
        {
            return Math.Clamp(value, 0m, 1m);
        }

        throw new FormatException($"'{key}' was not a probability.");
    }

    private static bool TryReadNumber(JsonElement elem, out decimal value)
    {
        value = 0m;
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(elem.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string StripCodeFence(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline
            ? text[(firstNewline + 1)..lastFence].Trim()
            : text.Trim('`').Trim();
    }
}
