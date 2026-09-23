using JevHrTriage.Api.Models;

namespace JevHrTriage.Api.Services;

public sealed class SampleDataService : ISampleDataService
{
    private static readonly IReadOnlyCollection<DemoSample> Samples = new List<DemoSample>
    {
        new()
        {
            Id = "routine-payroll",
            Title = "Straightforward payroll adjustment",
            CoverageTag = "straightforward-routing",
            Request = new TriageRequest
            {
                Subject = "Overtime missing on latest payslip",
                Message = "My overtime from last week is missing on my latest paycheck. Please review and adjust if needed.",
                EmployeeId = "EMP-1042",
                PayPeriod = "2026-09-01 to 2026-09-15",
                Country = "US",
                EmploymentType = "FullTime"
            }
        },
        new()
        {
            Id = "urgent-access",
            Title = "Urgent HR system access blocking work",
            CoverageTag = "urgency",
            Request = new TriageRequest
            {
                Subject = "Cannot access HR portal before payroll cutoff",
                Message = "I am blocked and cannot submit required timesheet corrections today because I cannot log in to the HR portal.",
                EmployeeId = "EMP-4421",
                HrSystemName = "Workday",
                Country = "US",
                EmploymentType = "PartTime"
            }
        },
        new()
        {
            Id = "missing-benefits-info",
            Title = "Benefits question with missing structured details",
            CoverageTag = "missing-information",
            Request = new TriageRequest
            {
                Subject = "Need help with coverage",
                Message = "I think my coverage is wrong and I need help understanding what changed.",
                EmployeeId = "EMP-7782",
                Country = "US"
            }
        },
        new()
        {
            Id = "ambiguous-request",
            Title = "Ambiguous question likely to route for review",
            CoverageTag = "ambiguity",
            Request = new TriageRequest
            {
                Subject = "Question about my account and options",
                Message = "I need help with my account and maybe my plan, not sure where to start.",
                EmployeeId = "EMP-9191",
                Country = "CA"
            }
        },
        new()
        {
            Id = "multi-topic",
            Title = "Multiple topics in one message",
            CoverageTag = "multiple-topics",
            Request = new TriageRequest
            {
                Subject = "Overtime missing and portal locked",
                Message = "My missing overtime is still not fixed and I am also locked out of the HR portal so I cannot submit corrections.",
                EmployeeId = "EMP-1120",
                PayPeriod = "2026-09-01 to 2026-09-15",
                HrSystemName = "Workday"
            }
        },
        new()
        {
            Id = "review-onboarding",
            Title = "Onboarding case missing required fields",
            CoverageTag = "requires-review",
            Request = new TriageRequest
            {
                Subject = "New hire setup help",
                Message = "Please help with onboarding setup for a new engineer joining next week.",
                EmployeeId = "EMP-3001"
            }
        }
    };

    public IReadOnlyCollection<DemoSample> GetSamples() => Samples;
}
