namespace PicRestore.Core.Models;

/// <summary>
/// Reported by <c>RestorationPipeline.Run</c> as each stage starts, so the UI can show a real, labelled
/// progress indicator instead of a spinner with no information - see the design plan's "nothing is a
/// black box" requirement. <see cref="StageIndex"/> is 1-based (the stage currently running).
/// </summary>
public sealed record PipelineProgress(string StageName, int StageIndex, int StageCount);
