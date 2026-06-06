using Spectre.Console;

namespace MemDumpAnalyzer.Cli;

/// <summary>
/// Renders dump-loading/analysis failures as actionable messages instead of raw stack traces.
/// </summary>
internal static class ErrorReporting
{
    public static void PrintAnalysisError(Exception ex, string dumpName)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red bold]✗ Could not analyze[/] [yellow]{Markup.Escape(dumpName)}[/]");
        AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(ex.Message)}[/]");
        AnsiConsole.WriteLine();

        if (ex is InvalidDataException || ex.Message.Contains("ClrHeap", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[bold]The dump does not contain a usable managed heap. Common causes:[/]");
            AnsiConsole.MarkupLine("  • Captured [yellow]without full memory[/] (a minidump). Re-capture with:");
            AnsiConsole.MarkupLine("      [aqua]procdump -ma <pid>[/]  or  [aqua]dotnet-dump collect --type full -p <pid>[/]");
            AnsiConsole.MarkupLine("      or Task Manager → right-click the process → [aqua]Create dump file[/]");
            AnsiConsole.MarkupLine("  • Captured [yellow]while the CLR was still starting up[/], before the heap was initialized.");
            AnsiConsole.MarkupLine("  • The file is [yellow]corrupt or truncated[/] — e.g. an incomplete copy or transfer.");
        }
        else if (ex.Message.Contains("No CLR runtime", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[bold]No .NET runtime was found in the dump. Common causes:[/]");
            AnsiConsole.MarkupLine("  • The dumped process is [yellow]not a .NET process[/] (native code only).");
            AnsiConsole.MarkupLine("  • The dump is a [yellow]minidump[/] that omits the module list — re-capture with full memory.");
            AnsiConsole.MarkupLine("  • A [yellow]32-bit process was dumped by a 64-bit tool[/] (or vice versa) — use a matching-bitness capture tool.");
        }
        else if (ex.Message.Contains("Failed to create CLR runtime", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[bold]The .NET runtime in the dump could not be loaded. Common causes:[/]");
            AnsiConsole.MarkupLine("  • [yellow]Architecture mismatch[/] — a 32-bit (x86) dump must be analyzed by an x86 build of this tool, and x64 by x64.");
            AnsiConsole.MarkupLine("  • The matching [yellow]DAC (mscordaccore/mscordacwks)[/] could not be downloaded — check connectivity to the Microsoft symbol server.");
        }
        else
        {
            // Unexpected failure — keep a compact stack trace for diagnosis.
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        }

        AnsiConsole.WriteLine();
    }
}
