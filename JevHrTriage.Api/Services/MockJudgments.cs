using JevHrTriage.Api.Models;

namespace JevHrTriage.Api.Services;

/// <summary>
/// Keyword-based stand-in for a decision model. Deterministic and free, but it only
/// demonstrates the response shape; it says nothing about real model quality.
/// </summary>
public static class MockJudgments
{
    public static JevJudgments Build(TriageRequest request)
    {
        var msg = (request.Subject + " " + request.Message).ToLowerInvariant();
        var category = "other_unclear";
        var categoryConfidence = 0.54m;
        var categoryProbabilities = new Dictionary<string, decimal>
        {
            ["payroll"] = 0.15m,
            ["leave"] = 0.15m,
            ["benefits"] = 0.15m,
            ["onboarding"] = 0.10m,
            ["hr_system_access"] = 0.15m,
            ["other_unclear"] = 0.30m
        };

        if (msg.Contains("overtime") || msg.Contains("paycheck") || msg.Contains("salary"))
        {
            category = "payroll";
            categoryConfidence = 0.86m;
            categoryProbabilities = new Dictionary<string, decimal>
            {
                ["payroll"] = 0.88m,
                ["leave"] = 0.02m,
                ["benefits"] = 0.02m,
                ["onboarding"] = 0.01m,
                ["hr_system_access"] = 0.04m,
                ["other_unclear"] = 0.03m
            };
        }
        else if (msg.Contains("pto") || msg.Contains("vacation") || msg.Contains("sick leave"))
        {
            category = "leave";
            categoryConfidence = 0.83m;
            categoryProbabilities = new Dictionary<string, decimal>
            {
                ["payroll"] = 0.03m,
                ["leave"] = 0.86m,
                ["benefits"] = 0.03m,
                ["onboarding"] = 0.02m,
                ["hr_system_access"] = 0.02m,
                ["other_unclear"] = 0.04m
            };
        }
        else if (msg.Contains("benefit") || msg.Contains("insurance") || msg.Contains("enrollment"))
        {
            category = "benefits";
            categoryConfidence = 0.82m;
            categoryProbabilities = new Dictionary<string, decimal>
            {
                ["payroll"] = 0.02m,
                ["leave"] = 0.03m,
                ["benefits"] = 0.85m,
                ["onboarding"] = 0.02m,
                ["hr_system_access"] = 0.04m,
                ["other_unclear"] = 0.04m
            };
        }
        else if (msg.Contains("onboarding") || msg.Contains("new hire") || msg.Contains("first day"))
        {
            category = "onboarding";
            categoryConfidence = 0.79m;
            categoryProbabilities = new Dictionary<string, decimal>
            {
                ["payroll"] = 0.03m,
                ["leave"] = 0.03m,
                ["benefits"] = 0.04m,
                ["onboarding"] = 0.81m,
                ["hr_system_access"] = 0.05m,
                ["other_unclear"] = 0.04m
            };
        }
        else if (msg.Contains("login") || msg.Contains("portal") || msg.Contains("mfa") || msg.Contains("access"))
        {
            category = "hr_system_access";
            categoryConfidence = 0.84m;
            categoryProbabilities = new Dictionary<string, decimal>
            {
                ["payroll"] = 0.04m,
                ["leave"] = 0.03m,
                ["benefits"] = 0.03m,
                ["onboarding"] = 0.02m,
                ["hr_system_access"] = 0.85m,
                ["other_unclear"] = 0.03m
            };
        }

        var multipleTopics = msg.Contains(" and ") &&
                             ((msg.Contains("overtime") || msg.Contains("paycheck")) &&
                              (msg.Contains("portal") || msg.Contains("login") || msg.Contains("access")));

        var urgencyScore = 0.45m;
        var urgencyConfidence = 0.72m;
        var urgencyProbabilities = new Dictionary<string, decimal> { ["0"] = 0.68m, ["1"] = 0.25m, ["2"] = 0.07m };

        if (msg.Contains("today") || msg.Contains("deadline") || msg.Contains("blocked") || msg.Contains("cannot work"))
        {
            urgencyScore = 1.78m;
            urgencyConfidence = 0.90m;
            urgencyProbabilities = new Dictionary<string, decimal> { ["0"] = 0.05m, ["1"] = 0.17m, ["2"] = 0.78m };
        }

        var missingInfo = string.IsNullOrWhiteSpace(request.Subject) && request.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 12;

        return new JevJudgments
        {
            Category = new ChoiceJudgment
            {
                Choice = category,
                Confidence = categoryConfidence,
                Probabilities = categoryProbabilities,
                IsSynthetic = true
            },
            Urgency = new ScoreJudgment
            {
                Score = urgencyScore,
                Confidence = urgencyConfidence,
                Probabilities = urgencyProbabilities,
                Legend = new Dictionary<string, string>
                {
                    ["0"] = "Routine queue",
                    ["1"] = "Time-sensitive",
                    ["2"] = "Urgent impact"
                },
                IsSynthetic = true
            },
            MultipleTopics = new NoulJudgment { Noul = multipleTopics ? 0.92m : 0.08m, IsSynthetic = true },
            MissingInfoInMessage = new NoulJudgment { Noul = missingInfo ? 0.82m : 0.18m, IsSynthetic = true }
        };
    }
}
