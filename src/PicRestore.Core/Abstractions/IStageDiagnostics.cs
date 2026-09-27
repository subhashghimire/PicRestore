namespace PicRestore.Core.Abstractions;

/// <summary>
/// Optional extra for a pipeline stage that can report fine-grained progress while it runs and a short
/// note about how its last run went (e.g. "AI model unavailable - used basic fill"). The pipeline checks
/// for it with a type test, so stages that don't need it are unaffected.
/// </summary>
public interface IStageDiagnostics
{
    /// <summary>Set by the pipeline before each run; the stage calls it with short status strings.</summary>
    Action<string>? StatusCallback { get; set; }

    /// <summary>A one-line, user-facing summary of the most recent run, or null if nothing notable.</summary>
    string? LastRunNote { get; }
}
