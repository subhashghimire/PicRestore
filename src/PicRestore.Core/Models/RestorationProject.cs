namespace PicRestore.Core.Models;

/// <summary>
/// Non-destructive project state for one photo: the mask, the settings used, and an append-only
/// history of what each pipeline run did. The original file on disk is never modified by any of this.
/// </summary>
public sealed class RestorationProject
{
    public required string OriginalFilePath { get; init; }

    public DamageMask? Mask { get; set; }

    public ProcessingSettings Settings { get; set; } = ProcessingSettings.CreateDefault();

    public List<PipelineStageResult> History { get; } = new();

    public void RecordStage(PipelineStageResult result) => History.Add(result);
}
