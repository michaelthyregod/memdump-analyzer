using Microsoft.Diagnostics.Runtime;

namespace MemDumpAnalyzer.Core;

/// <summary>
/// Opens a .dmp file via ClrMD and validates that a CLR runtime is present.
/// Callers must dispose the returned <see cref="LoadedDump"/> when finished.
/// </summary>
public static class DumpLoader
{
    public sealed class LoadedDump : IDisposable
    {
        public DataTarget DataTarget { get; }
        public ClrRuntime Runtime { get; }
        public string ClrVersion { get; }
        public string AppDomainName { get; }

        internal LoadedDump(DataTarget dataTarget, ClrRuntime runtime, string clrVersion, string appDomainName)
        {
            DataTarget = dataTarget;
            Runtime = runtime;
            ClrVersion = clrVersion;
            AppDomainName = appDomainName;
        }

        public void Dispose()
        {
            Runtime.Dispose();
            DataTarget.Dispose();
        }
    }

    public static LoadedDump Load(string dumpPath)
    {
        if (!File.Exists(dumpPath))
            throw new FileNotFoundException($"Dump file not found: {dumpPath}");

        var dataTarget = DataTarget.LoadDump(dumpPath);

        if (dataTarget.ClrVersions.Length == 0)
        {
            dataTarget.Dispose();
            throw new InvalidOperationException($"No CLR runtime found in dump: {dumpPath}");
        }

        var clrInfo = dataTarget.ClrVersions[0];
        ClrRuntime runtime;
        try
        {
            runtime = clrInfo.CreateRuntime();
        }
        catch (Exception ex)
        {
            dataTarget.Dispose();
            throw new InvalidOperationException($"Failed to create CLR runtime: {ex.Message}", ex);
        }

        var clrVersion = clrInfo.Version.ToString();
        var appDomainName = runtime.AppDomains.FirstOrDefault()?.Name ?? "Unknown";

        return new LoadedDump(dataTarget, runtime, clrVersion, appDomainName);
    }
}
