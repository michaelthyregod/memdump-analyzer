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
        /// <summary>When the dump was written, from the minidump header (null for non-minidump formats).</summary>
        public DateTime? CaptureTimeUtc { get; }

        internal LoadedDump(DataTarget dataTarget, ClrRuntime runtime, string clrVersion, string appDomainName, DateTime? captureTimeUtc)
        {
            DataTarget = dataTarget;
            Runtime = runtime;
            ClrVersion = clrVersion;
            AppDomainName = appDomainName;
            CaptureTimeUtc = captureTimeUtc;
        }

        public void Dispose()
        {
            Runtime.Dispose();
            DataTarget.Dispose();
        }
    }

    public static LoadedDump Load(string dumpPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        // Checked outside the try below so cancellation is not rewrapped as a runtime-creation failure
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            dataTarget.Dispose();
            throw;
        }

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

        return new LoadedDump(dataTarget, runtime, clrVersion, appDomainName, ReadMinidumpTimestamp(dumpPath));
    }

    /// <summary>
    /// Reads TimeDateStamp from the MINIDUMP_HEADER (offset 20, seconds since 1970 UTC) — the time the
    /// dump was written. The file's own timestamp changes when the dump is copied, so it's only a fallback.
    /// </summary>
    public static DateTime? ReadMinidumpTimestamp(string dumpPath)
    {
        try
        {
            Span<byte> header = stackalloc byte[24];
            using var fs = File.OpenRead(dumpPath);
            if (fs.Read(header) < header.Length) return null;
            if (header[0] != (byte)'M' || header[1] != (byte)'D' || header[2] != (byte)'M' || header[3] != (byte)'P')
                return null;
            uint seconds = BitConverter.ToUInt32(header[20..]);
            return seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
