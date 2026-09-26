namespace PicRestore.Core.Models;

/// <summary>One entry in a restoration's audit trail, shown in the UI's confidence/history view.</summary>
public sealed record PipelineStageResult(string StageName, bool Applied, string Message)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
