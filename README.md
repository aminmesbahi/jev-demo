# Jev HR Triage Demo

A small ASP.NET Core API that shows how to triage HR service requests with [Jev](https://openrouter.ai/typesafe/jev-1.13), TypeSafe's decision model, served through OpenRouter's Decisions API.

You send it an employee's request, for example "my overtime is missing and I'm locked out of Workday", and it tells you two things:

1. **What Jev thinks:** the category, how urgent it is, whether it mixes several topics, and whether the message looks underspecified. Each answer comes with probabilities.
2. **What the application decides:** which queue it would go to, whether it can be auto-routed or needs a human, which required details are missing, and which rules led there.

The demo doesn't act on anything. It never contacts employees or creates tickets. It only recommends.

## Why Jev and not a chat model?

Triage is a decision problem, not a writing problem. With a chat model you write a prompt, ask for JSON, parse it, and hope the label is one you recognise. You also get no reliable sense of how sure the model was.

Jev works differently. You give it a `state` (the request) and a set of typed questions, and it answers with one of the options you defined plus a probability for each. There is nothing to parse and no free text to hallucinate. The probabilities are what you threshold on. Each call costs fractions of a cent, because input is billed at about $0.042 per million tokens and output is free.

The rule this demo follows: **Jev judges, C# decides.** Jev answers narrow questions about the language. Everything with business consequences lives in plain, testable C#: queue mapping, required fields, review thresholds and clarification wording.

## Getting started

You need a .NET SDK that supports `net10.0`.

```powershell
dotnet build JevHrTriageDemo.slnx
dotnet test JevHrTriageDemo.slnx
dotnet run --project JevHrTriage.Api --launch-profile http
```

Then open http://localhost:5218/swagger. The easiest tour:

1. `GET /api/demo/samples` returns six synthetic requests: a routine one, an urgent one, one with missing info, an ambiguous one, a multi-topic one, and one that needs review.
2. Paste one of them into `POST /api/hr/triage`.

[JevHrTriage.Api.http](JevHrTriage.Api/JevHrTriage.Api.http) has the same calls if you prefer running them from your editor.

### Mock mode vs Live mode

By default the app runs in **Mock** mode. It needs no API key, costs nothing, and gives the same answer every time, which makes it right for tests and offline demos. The mock is crude keyword matching, though. It shows the shape of the flow, not how good Jev is. To make that clear, every mock response is marked `isSynthetic: true`, at the top level and on each judgment.

To use the real model, switch to **Live** mode and give it an OpenRouter key:

```powershell
$env:OPENROUTER_API_KEY = "sk-or-..."
dotnet run --project JevHrTriage.Api --launch-profile http -- TriageDemo:Mode=Live
```

If you'd rather not use environment variables, user secrets work too:

```powershell
dotnet user-secrets set "TriageDemo:Mode" "Live" --project JevHrTriage.Api
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-..." --project JevHrTriage.Api
```

Live mode never quietly falls back to Mock. If OpenRouter fails, you get an error, not a fake answer that looks real.

## How a request flows

```
TriageRequest ──► JevDecisionProvider ──► OpenRouter /api/alpha/decisions (one call, 4 questions)
                         │
                         ▼
                  JevJudgments ──► TriageWorkflowService (thresholds, mapping, required fields)
                                          │
                                          ▼
                                   TriageResponse
```

**One call, four questions.** Jev answers every question in the same request, in parallel, so adding a question barely changes latency. The questions are:

| Question            | Type     | What it asks                                                     |
|---------------------|----------|------------------------------------------------------------------|
| `category`          | choice   | payroll, leave, benefits, onboarding, HR system access, or other/unclear |
| `urgency`           | score    | routine → time-sensitive → urgent (pay, legal timing or work blocked) |
| `multiple_topics`   | noul     | Does this mix two or more separate HR issues?                   |
| `missing_information` | noul   | Judging the text alone, is key detail missing?                  |

("Noul" is Jev's yes/no question type. It returns the probability of "yes".)

**The application then decides.** [TriageWorkflowService](JevHrTriage.Api/Services/TriageWorkflowService.cs) maps the category to a queue using an explicit dictionary, and checks which structured fields that category needs. Payroll, for example, needs an employee ID and a pay period. It then runs the review rules. If any rule fires, the request goes to `HumanReview`, otherwise to `AutoRoute`. Each rule writes a line into `appliedRules` with the actual value and threshold, so you can always see why a request ended up where it did.

Clarification prompts come from fixed templates. Nothing in a response is invented by a model.

## Tuning the thresholds

Everything lives under `TriageDemo` in [appsettings.json](JevHrTriage.Api/appsettings.json):

| Setting                          | Default | Sends to review when…                                    |
|----------------------------------|---------|----------------------------------------------------------|
| `AutoRouteMinConfidence`         | 0.75    | category confidence is below this                        |
| `ReviewIfCategoryGapBelow`       | 0.15    | the top two categories are closer than this              |
| `ReviewIfMultipleTopicsAtLeast`  | 0.55    | the multi-topic probability is at or above this          |
| `ReviewIfMissingInfoNoulAtLeast` | 0.60    | the missing-info probability is at or above this         |
| `UrgentScoreThreshold`           | 1.6     | (marks the request urgent)                               |
| `UrgentHighConfidenceThreshold`  | 0.80    | the request is urgent but urgency confidence is below this |

To be honest about these numbers: **they are reasonable starting points, not tuned values.** TypeSafe recommends choosing thresholds from a few hundred labeled examples and from what each kind of mistake costs. Misrouting a payroll problem is worse than sending one extra ticket to a human. A few things worth knowing before you tune:

- **Confidence is not accuracy.** It measures how concentrated Jev's probabilities are. A confident answer can still be wrong if the question was badly phrased.
- **Answers vary slightly between calls.** Don't set a threshold right at the edge of your borderline cases.
- **Pin the model version.** The default is `typesafe/jev-1.13`, not `~typesafe/jev-latest`, so your thresholds don't move under you when a new release ships.

## When things go wrong

Errors come back as standard `ProblemDetails`:

| You see | Meaning | What to do |
|---------|---------|------------|
| 400 | The request body is invalid | Fix the input |
| 401 | Bad or missing API key | Check `OPENROUTER_API_KEY` / `OpenRouter:ApiKey` |
| 402 | Out of OpenRouter credits | Top up |
| 429 | Still rate limited after retries | Slow down |
| 500 "Missing Configuration" | Live mode without a key | Set the key |
| 502 "Upstream Unreachable" | Couldn't connect to OpenRouter | Check network / proxy |
| 502 "Invalid Decisions Request" | OpenRouter rejected our payload (422) | This is a bug in the demo's question definitions, not your input |
| 502 "Malformed Upstream Response" | OpenRouter answered, but not in the expected shape | The alpha API may have changed |
| 503 | OpenRouter or the provider is overloaded | Try again shortly |
| 504 | OpenRouter didn't answer within `LiveTimeoutSeconds` | Raise the timeout, or retry |

**Retries.** Rate limits (429) and provider overload (529) are retried automatically. It uses exponential backoff with a little jitter, and respects `Retry-After` when OpenRouter sends one. Other errors fail immediately, because retrying a bad key or an empty balance only wastes time. You can tune this with `OpenRouter:MaxRetries` (default 2) and `OpenRouter:RetryBaseDelayMs` (default 500). Note that `LiveTimeoutSeconds` applies to each attempt, not to the whole sequence.

**Cancellation.** If the caller hangs up, the upstream call is cancelled and nothing is reported as a timeout. Only a real HttpClient timeout becomes a 504.

## Logging and audit

Every triage writes one structured log line with:

- the mode
- the model snapshot and OpenRouter's request id
- the latency
- the category probabilities, urgency, and the two noul values
- the chosen queue, the review status, and the full list of reasons and applied rules

The request id is also returned to the caller as `upstreamRequestId`, so you can match a response to OpenRouter's records.

**The request text and employee fields are never logged on purpose.** The log has everything you need to replay and re-tune the thresholds, and nothing that identifies a person.

## Project layout

```
JevHrTriage.Api/
  Controllers/      HrController (triage), DemoController (samples)
  Services/
    JevDecisionProvider.cs        builds the questions, calls Jev (or the mock), parses answers
    OpenRouterDecisionsClient.cs  HTTP, auth, retries, error mapping
    TriageWorkflowService.cs      all the business rules
    SampleDataService.cs          the six demo requests
  Models/           request/response DTOs and OpenRouter contracts
  Options/          TriageDemo and OpenRouter settings
JevHrTriage.Tests/  xUnit tests for the client, the provider and the workflow rules
```

## A five-minute walkthrough for the team

1. **The problem (under a minute).** The HR inbox is noisy. Misrouted requests bounce between teams and miss their SLA.
2. **What Jev does (a minute).** Show the four questions. Point out that they are typed and that every answer has probabilities. There is no prompt-and-parse.
3. **What C# does (a minute).** Open `TriageWorkflowService`: the queue map, the required fields, the thresholds. All of it is ordinary code that can be reviewed and tested.
4. **Live in Swagger (90 seconds).** Run the multi-topic sample. Show `jevJudgments` and `applicationRecommendation` side by side, then read `appliedRules` aloud. It explains itself.
5. **Honesty slide (a minute).** Structured output doesn't mean correct output. The thresholds still need real labeled data, and the mock is only a stand-in.

## Known limitations

- **Text and language:** Jev handles text only, and it is most accurate in English.
- **Literal reading:** it answers the question exactly as written, so vague question wording turns into vague probabilities.
- **Dates and arithmetic:** it is not reliable at these. Keep anything you can compute in code.
- **Alpha API:** the Decisions API is still alpha (the path says so), and the request or response shape may change.
- **Mock mode:** it is keyword matching and should not be used to judge quality.

## Further reading

- [Jev on OpenRouter](https://openrouter.ai/docs/guides/community/jev)
- [TypeSafe docs](https://docs.typesafe.ai). The pages on confidence, patterns, and jev-1.13's known weaknesses are especially useful.
