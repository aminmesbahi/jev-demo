using System.Text.Json;
using JevHrTriage.Api.Models;

namespace JevHrTriage.Api.Services;

/// <summary>
/// The triage question set and answer parsing, in the System One wire format
/// (state + typed questions). Shared by every decision model so each one is asked
/// exactly the same thing.
/// </summary>
public static class TriageQuestions
{
    public const string Category = "category";
    public const string Urgency = "urgency";
    public const string MultipleTopics = "multiple_topics";
    public const string MissingInformation = "missing_information";

    public static object BuildState(TriageRequest request) => new
    {
        subject = request.Subject,
        message = request.Message,
        country = request.Country,
        employment_type = request.EmploymentType,
        known_fields = new
        {
            has_pay_period = !string.IsNullOrWhiteSpace(request.PayPeriod),
            has_leave_dates = request.LeaveStartDate.HasValue && request.LeaveEndDate.HasValue,
            has_hr_system_name = !string.IsNullOrWhiteSpace(request.HrSystemName),
            has_benefit_plan = !string.IsNullOrWhiteSpace(request.BenefitPlan),
            has_onboarding_start_date = request.OnboardingStartDate.HasValue
        }
    };

    public static JevJudgments ParseJudgments(Dictionary<string, JsonElement> answers) => new()
    {
        Category = ParseChoice(answers, Category),
        Urgency = ParseScore(answers, Urgency),
        MultipleTopics = ParseNoul(answers, MultipleTopics),
        MissingInfoInMessage = ParseNoul(answers, MissingInformation)
    };

    /// <summary>Category options, in the order they are sent to the model.</summary>
    public static readonly IReadOnlyDictionary<string, string> CategoryCriteria = new Dictionary<string, string>
    {
        ["payroll"] = "Paycheck, overtime, deductions, tax withholding, compensation payment timing.",
        ["leave"] = "Vacation, sick leave, parental leave, unpaid leave, PTO balances or scheduling.",
        ["benefits"] = "Insurance, enrollment, dependents, claims, retirement, or benefits eligibility questions.",
        ["onboarding"] = "New-hire setup, orientation, preboarding paperwork, first-week logistics.",
        ["hr_system_access"] = "Problems logging into HR tools, account lockouts, MFA, or permissions.",
        ["other_unclear"] = "Ambiguous or not clearly in one category."
    };

    /// <summary>Urgency levels, lowest to highest. Index = score level.</summary>
    public static readonly IReadOnlyList<string> UrgencyCriteria =
    [
        "Routine question; can follow normal queue time.",
        "Time-sensitive; should be addressed within one business day.",
        "Urgent; employee pay, legal timing, or critical work access is currently blocked."
    ];

    public static Dictionary<string, object> BuildQuestions()
    {
        return new Dictionary<string, object>
        {
            [Category] = new
            {
                type = "choice",
                instructions = "Which single primary HR category best matches this employee request? If unclear, choose other_unclear.",
                criteria = CategoryCriteria
            },
            [Urgency] = new
            {
                type = "score",
                instructions = "How urgent is handling this request for employee impact and business continuity?",
                criteria = UrgencyCriteria
            },
            [MultipleTopics] = new
            {
                type = "noul",
                instructions = "Does the request contain two or more distinct HR issues that could belong to different categories?",
                criteria = new
                {
                    @true = "The message combines separate concerns, such as payroll and HR-system access in one request.",
                    @false = "The message is mostly about one HR issue."
                }
            },
            [MissingInformation] = new
            {
                type = "noul",
                instructions = "Based on the message wording itself, is there likely missing detail needed to process the request?",
                criteria = new
                {
                    @true = "The message is vague or lacks key specifics in text.",
                    @false = "The message text appears specific enough to start processing."
                }
            }
        };
    }

    private static ChoiceJudgment ParseChoice(Dictionary<string, JsonElement> answers, string key)
    {
        if (!answers.TryGetValue(key, out var elem))
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Missing expected answer '{key}'.");
        }

        if (!elem.TryGetProperty("choice", out var choiceElem) || choiceElem.ValueKind != JsonValueKind.String)
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Answer '{key}' did not include a choice value.");
        }

        var result = new ChoiceJudgment { Choice = choiceElem.GetString() ?? "other_unclear" };

        if (elem.TryGetProperty("confidence", out var confidenceElem) && confidenceElem.ValueKind == JsonValueKind.Number)
        {
            result.Confidence = confidenceElem.GetDecimal();
        }

        if (elem.TryGetProperty("probabilities", out var probsElem) && probsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in probsElem.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                {
                    result.Probabilities[p.Name] = p.Value.GetDecimal();
                }
            }
        }

        return result;
    }

    private static ScoreJudgment ParseScore(Dictionary<string, JsonElement> answers, string key)
    {
        if (!answers.TryGetValue(key, out var elem))
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Missing expected answer '{key}'.");
        }

        if (!elem.TryGetProperty("score", out var scoreElem) || scoreElem.ValueKind != JsonValueKind.Number)
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Answer '{key}' did not include a score value.");
        }

        var result = new ScoreJudgment { Score = scoreElem.GetDecimal() };

        if (elem.TryGetProperty("confidence", out var confidenceElem) && confidenceElem.ValueKind == JsonValueKind.Number)
        {
            result.Confidence = confidenceElem.GetDecimal();
        }

        if (elem.TryGetProperty("probabilities", out var probsElem) && probsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in probsElem.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                {
                    result.Probabilities[p.Name] = p.Value.GetDecimal();
                }
            }
        }

        if (elem.TryGetProperty("legend", out var legendElem) && legendElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in legendElem.EnumerateObject())
            {
                if (item.Value.ValueKind == JsonValueKind.String)
                {
                    result.Legend[item.Name] = item.Value.GetString() ?? string.Empty;
                }
            }
        }

        return result;
    }

    private static NoulJudgment ParseNoul(Dictionary<string, JsonElement> answers, string key)
    {
        if (!answers.TryGetValue(key, out var elem))
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Missing expected answer '{key}'.");
        }

        if (!elem.TryGetProperty("noul", out var noulElem) || noulElem.ValueKind != JsonValueKind.Number)
        {
            throw new TriageException(502, "Malformed Upstream Response", $"Answer '{key}' did not include a noul value.");
        }

        return new NoulJudgment { Noul = noulElem.GetDecimal() };
    }
}
