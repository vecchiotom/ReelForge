namespace ReelForge.WorkflowEngine.Execution;

public sealed class WorkflowHardeningOptions
{
    public const string SectionName = "WorkflowHardening";

    public bool EnableStructuredRetryDiagnostics { get; set; } = true;
    public int MaxStepRetries { get; set; } = 3;
    public int MaxAuthorStepRetries { get; set; } = 3;
    public int MaxTranslatorStepRetries { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 2;
}
