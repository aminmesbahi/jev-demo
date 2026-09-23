# Jev HR Service-Request Triage Demo (ASP.NET Core)

Practical demo API for HR service-request triage using TypeSafe Jev via OpenRouter Decisions API.

This demo is intentionally limited to recommendation-only triage:
- It returns a suggested queue and review status.
- It does not contact employees.
- It does not create tickets in external systems.

## Chosen Stack

- .NET target framework: net10.0
- Project type: ASP.NET Core Web API + Swagger UI
- JSON: System.Text.Json
- HTTP: IHttpClientFactory + typed HttpClient
- Tests: xUnit

## What Jev Decides vs What C# Decides

Jev judgments (model output):
- Category (Choice)
- Urgency (Score, fractional)
- Multiple topics detection (Noul)
- Message-level missing-info signal (Noul)

C# deterministic logic (application output):
- Category-to-queue mapping (explicit dictionary)
- Required structured fields by category
- Clarification prompts from fixed templates
- Human-review thresholds (confidence, probability gap, multiple topics, urgency confidence, missing fields)
- Final review status and suggested queue

## Endpoints

- POST /api/hr/triage
	- Analyze one request and return:
		- mode (Live or Mock)
		- Jev judgments (separate section)
		- Application recommendation (separate section)
		- latency and usage/cost when available
- GET /api/demo/samples
	- Six realistic synthetic requests for demo coverage:
		- straightforward
		- urgency
		- missing information
		- ambiguity
		- multiple topics
		- requires review
- GET /health
	- Health status only, no paid inference call

## OpenRouter Decisions API Contract Used

- Endpoint: POST https://openrouter.ai/api/alpha/decisions
- Model default: typesafe/jev-1.13
- Auth: Bearer token (OPENROUTER_API_KEY)

The implementation uses state + questions and typed answers. It does not use chat completions.

## Prerequisites

- .NET SDK that supports net10.0
- Optional for Live mode: OpenRouter API key with credits

## Setup and Run

1. Restore and build:

```powershell
dotnet restore JevHrTriageDemo.slnx
dotnet build JevHrTriageDemo.slnx
```

2. Run tests:

```powershell
dotnet test JevHrTriageDemo.slnx --no-build
```

3. Run API:

```powershell
dotnet run --project JevHrTriage.Api --launch-profile http
```

4. Open Swagger UI:

- http://localhost:5218/swagger

## Mode Switching (Mock vs Live)

Default is Mock mode in appsettings.

### Mock mode

- No credentials required
- Deterministic synthetic fixtures
- Response explicitly labels synthetic judgments:
	- top-level isSynthetic
	- per-judgment isSynthetic

### Live mode

Set mode and key before run:

```powershell
$env:OPENROUTER_API_KEY = "your_key_here"
dotnet user-secrets init --project JevHrTriage.Api
dotnet user-secrets set "TriageDemo:Mode" "Live" --project JevHrTriage.Api
# optional if you prefer user secrets over env var:
dotnet user-secrets set "OpenRouter:ApiKey" "your_key_here" --project JevHrTriage.Api
```

Alternative without user secrets:

```powershell
$env:OPENROUTER_API_KEY = "your_key_here"
dotnet run --project JevHrTriage.Api --launch-profile http -- TriageDemo:Mode=Live
```

Important behavior:
- If Live mode fails, the API returns explicit ProblemDetails errors.
- It does not silently switch to Mock mode.

## Demo Requests

Use the ready-to-run file:

- JevHrTriage.Api/JevHrTriage.Api.http

Includes:
- GET /health
- GET /api/demo/samples
- POST /api/hr/triage

## Example Triage Response Shape

- mode
- model
- upstreamLatencyMs
- usage (cost/tokens/provider when available)
- jevJudgments
	- category (choice + probabilities + confidence)
	- urgency (score + probabilities + confidence)
	- multipleTopics (noul)
	- missingInfoInMessage (noul)
- applicationRecommendation
	- suggestedQueue
	- reviewStatus (AutoRoute or HumanReview)
	- reviewReasons
	- missingRequiredFields
	- clarificationPrompts
	- appliedRules

## Architecture (Small, Understandable)

- Controllers:
	- api/hr/triage
	- api/demo/samples
- Services:
	- JevDecisionProvider (Live + Mock)
	- OpenRouterDecisionsClient
	- TriageWorkflowService (business logic)
	- SampleDataService
- Models:
	- request/response DTOs
	- OpenRouter request/response contracts
- Options:
	- TriageDemo thresholds/mode/model/timeout
	- OpenRouter base URL/path/key

## Error Handling

ProblemDetails responses are returned for:
- invalid input
- missing configuration
- authentication failures
- insufficient credits
- rate limits
- timeouts
- malformed upstream responses

## Five-Minute Team Walkthrough

1. Business pain (45s)
- HR inbox triage is noisy and inconsistent.
- Misrouting slows SLA and employee satisfaction.

2. What Jev does (60s)
- Jev classifies category, scores urgency, detects multi-topic and missing-info signals.
- It returns typed probabilities/confidence instead of text explanations.

3. What C# does (75s)
- Maps category to queue explicitly.
- Applies risk-aware thresholds and review gates.
- Validates required fields deterministically.
- Generates clarification prompts from fixed templates (no invented details).

4. Show in Swagger (90s)
- GET /api/demo/samples
- POST /api/hr/triage using multi-topic sample
- Highlight separate sections:
	- jevJudgments
	- applicationRecommendation

5. Reliability and governance (60s)
- Mock mode is deterministic for demos/tests.
- Live mode uses OpenRouter with explicit errors.
- Valid structured output does not guarantee correct judgment.
- Thresholds are demo defaults that require evaluation on real data.

## Troubleshooting

- 401 Authentication Failed
	- Check OPENROUTER_API_KEY or OpenRouter:ApiKey
- 402 Insufficient Credits
	- Add OpenRouter credits
- 429 Rate Limited
	- Retry with backoff; lower request frequency
- 504 Upstream Timeout
	- Increase TriageDemo:LiveTimeoutSeconds
- Review rates too high/low
	- Tune confidence and gap thresholds in TriageDemo settings

## Verification Completed

Executed locally:
- Build succeeded
- Tests succeeded (6/6)
- API launched on http://localhost:5218
- Verified endpoints:
	- GET /health
	- GET /api/demo/samples
	- POST /api/hr/triage
	- GET /swagger/index.html

Live mode note:
- Live OpenRouter call was not executed in this run because no API key was provided in-session.
- Mock mode verification is complete and runnable.
