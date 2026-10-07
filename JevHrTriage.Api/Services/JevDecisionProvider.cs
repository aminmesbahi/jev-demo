using System.Diagnostics;
using System.Text.Json;
using JevHrTriage.Api.Models;
using JevHrTriage.Api.Options;
using Microsoft.Extensions.Options;

namespace JevHrTriage.Api.Services;

public sealed class JevDecisionProvider : IJevDecisionProvider
{
    private readonly IOpenRouterDecisionsClient _client;
    private readonly TriageDemoOptions _options;

    public JevDecisionProvider(IOpenRouterDecisionsClient client, IOptions<TriageDemoOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string Mode => string.Equals(_options.Mode, "Live", StringComparison.OrdinalIgnoreCase) ? "Live" : "Mock";

    public bool IsSynthetic => !string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);

    public async Task<JevDecisionSet> EvaluateAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMockDecision(request);
        }

        var stopwatch = Stopwatch.StartNew();
        var decisionsRequest = new DecisionsRequest
        {
            Model = _options.Model,
            State = new
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
            },
            Questions = BuildQuestions()
        };

        DecisionsResponse response;
        try
        {
            response = await _client.SubmitAsync(decisionsRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only HttpClient's own timeout lands here. If the caller cancelled, the exception
            // propagates untouched so it isn't misreported as an upstream timeout.
            throw new TriageException(504, "Upstream Timeout", "The OpenRouter request timed out.");
        }
        finally
        {
            stopwatch.Stop();
        }

        return new JevDecisionSet
        {
            Model = response.Model ?? _options.Model,
            RequestId = response.Id,
            Provider = response.Provider,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            Usage = new UsageSnapshot
            {
                CostUsd = response.Usage?.Cost,
                InputTokens = response.Usage?.InputTokens,
                OutputTokens = response.Usage?.OutputTokens,
                Provider = response.Provider
            },
            Judgments = new JevJudgments
            {
                Category = ParseChoice(response.Answers, "category"),
                Urgency = ParseScore(response.Answers, "urgency"),
                MultipleTopics = ParseNoul(response.Answers, "multiple_topics"),
                MissingInfoInMessage = ParseNoul(response.Answers, "missing_information")
            }
        };
    }

    private static Dictionary<string, object> BuildQuestions()
    {
        return new Dictionary<string, object>
        {
            ["category"] = new
            {
                type = "choice",
                instructions = "Which single primary HR category best matches this employee request? If unclear, choose other_unclear.",
                criteria = new Dictionary<string, string>
                {
                    ["payroll"] = "Paycheck, overtime, deductions, tax withholding, compensation payment timing.",
                    ["leave"] = "Vacation, sick leave, parental leave, unpaid leave, PTO balances or scheduling.",
                    ["benefits"] = "Insurance, enrollment, dependents, claims, retirement, or benefits eligibility questions.",
                    ["onboarding"] = "New-hire setup, orientation, preboarding paperwork, first-week logistics.",
                    ["hr_system_access"] = "Problems logging into HR tools, account lockouts, MFA, or permissions.",
                    ["other_unclear"] = "Ambiguous or not clearly in one category."
                }
            },
            ["urgency"] = new
            {
                type = "score",
                instructions = "How urgent is handling this request for employee impact and business continuity?",
                criteria = new[]
                {
                    "Routine question; can follow normal queue time.",
                    "Time-sensitive; should be addressed within one business day.",
                    "Urgent; employee pay, legal timing, or critical work access is currently blocked."
                }
            },
            ["multiple_topics"] = new
            {
                type = "noul",
                instructions = "Does the request contain two or more distinct HR issues that could belong to different categories?",
                criteria = new
                {
                    @true = "The message combines separate concerns, such as payroll and HR-system access in one request.",
                    @false = "The message is mostly about one HR issue."
                }
            },
            ["missing_information"] = new
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

    private JevDecisionSet BuildMockDecision(TriageRequest request)
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

        return new JevDecisionSet
        {
            Model = _options.Model,
            Provider = "MockProvider",
            LatencyMs = 7,
            Usage = null,
            Judgments = new JevJudgments
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
            }
        };
    }
}
