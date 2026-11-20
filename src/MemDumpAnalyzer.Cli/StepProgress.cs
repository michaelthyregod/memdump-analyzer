using MemDumpAnalyzer.Core;
using Spectre.Console;

namespace MemDumpAnalyzer.Cli;

/// <summary>
/// Shows the analysis step on a Spectre progress task ("Walking the heap (3/5)"), so a slow step is
/// visible instead of looking like a hang. Updates the task directly (no SynchronizationContext needed).
/// </summary>
internal sealed class StepProgress(ProgressTask task, string? prefix = null) : IProgress<AnalysisStep>
{
    public void Report(AnalysisStep step)
    {
        task.MaxValue = step.Total;
        task.Value = step.Number - 1;
        task.Description = $"[green]{Markup.Escape((prefix != null ? prefix + ": " : "") + step.Description)}[/] [grey]({step.Number}/{step.Total})[/]";
    }

    public void Done()
    {
        task.Value = task.MaxValue;
        task.Description = $"[green]{Markup.Escape(prefix != null ? prefix + ": done" : "Done")}[/]";
    }
}
