namespace JevHrTriage.Api.Options;

public sealed class TriageDemoOptions
{
    public const string SectionName = "TriageDemo";

    public string Mode { get; set; } = "Mock";
    public string Model { get; set; } = "typesafe/jev-1.13";
    public int LiveTimeoutSeconds { get; set; } = 20;
    public decimal AutoRouteMinConfidence { get; set; } = 0.75m;
    public decimal ReviewIfCategoryGapBelow { get; set; } = 0.15m;
    public decimal ReviewIfMultipleTopicsAtLeast { get; set; } = 0.55m;
    public decimal ReviewIfMissingInfoNoulAtLeast { get; set; } = 0.60m;
    public decimal UrgentScoreThreshold { get; set; } = 1.6m;
    public decimal UrgentHighConfidenceThreshold { get; set; } = 0.80m;
}
