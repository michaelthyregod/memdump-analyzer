using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MemDumpAnalyzer.Cli;

/// <summary>
/// Single-exe builds (<c>-p:SingleExe=true</c>) embed QuestPDF's native libraries (qpdf, QuestPdfSkia)
/// as resources, because a Native AOT exe can't link them in. On first use they are extracted once to
/// <c>%LOCALAPPDATA%\memdump-analyzer\native\&lt;content hash&gt;</c>, loaded, and QuestPDF's P/Invokes
/// are pointed at them. In a normal build there are no such resources and nothing happens.
/// </summary>
internal static class NativeDependencies
{
    private const string ResourcePrefix = "native/";
    private static readonly Dictionary<string, IntPtr> Loaded = new(StringComparer.OrdinalIgnoreCase);

    public static void Prepare()
    {
        var assembly = typeof(NativeDependencies).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(n => n.Contains("qpdf", StringComparison.OrdinalIgnoreCase) ? 0 : 1)   // qpdf before Skia
            .ToList();
        if (resources.Count == 0)
            return;

        // One folder per build: the hash of the embedded files, so different versions never mix
        var blobs = resources.Select(r => (Name: r[ResourcePrefix.Length..], Data: Read(assembly, r))).ToList();
        var hash = Convert.ToHexString(SHA256.HashData(blobs.SelectMany(b => b.Data).ToArray()))[..16];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "memdump-analyzer", "native", hash);
        Directory.CreateDirectory(folder);

        foreach (var (name, data) in blobs)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path) || new FileInfo(path).Length != data.Length)
                WriteAtomically(path, data);
            Loaded[Path.GetFileNameWithoutExtension(name)] = NativeLibrary.Load(path);
        }

        // QuestPDF's [DllImport("qpdf")] / [DllImport("QuestPdfSkia")] resolve to the extracted copies
        NativeLibrary.SetDllImportResolver(typeof(QuestPDF.Settings).Assembly, (libraryName, _, _) =>
            Loaded.TryGetValue(Path.GetFileNameWithoutExtension(libraryName), out var handle) ? handle : IntPtr.Zero);
    }

    // Another process may be extracting the same file: write to a unique temp name, then move into place;
    // if the target appeared meanwhile, theirs is identical (same content hash folder) and ours is dropped.
    private static void WriteAtomically(string path, byte[] data)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, data);
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temp);
        }
    }

    private static byte[] Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
