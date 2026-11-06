// Rewrites the ZIP container of an Office Open XML or OpenDocument file with plain structure, for LibreOffice.
// LibreOffice's package reader (26.2) refuses archives that other readers open, such as Tencent Docs exports (issue
// #141): it fails on entries stored uncompressed with a data descriptor, bytes between entries and a name that appears
// twice, saying only "source file could not be loaded". The parts are fine, so a copy with the same parts written by
// System.IO.Compression loads.

using System.IO.Compression;

namespace Filee.Engines.Office;

/// <summary>Copies an Office Open XML or OpenDocument package into a new, plainly structured ZIP.</summary>
internal static class PackageRepacker
{
    private const string ContentTypes = "[Content_Types].xml";
    private const string Mimetype = "mimetype";
    private const long MaxPackageTotalBytes = 1024L * 1024 * 1024; // 1 GB max uncompressed package content

    /// <summary>The file is a ZIP that holds <c>[Content_Types].xml</c> (Office Open XML) or <c>mimetype</c> (OpenDocument).</summary>
    public static bool IsPackage(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry(ContentTypes) is not null || zip.GetEntry(Mimetype) is not null;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void CopyBounded(Stream from, Stream to, string name)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxPackageTotalBytes)
                throw new InvalidDataException($"Office package part '{name}' exceeds safe decompression size limits.");
            to.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Writes every part of <paramref name="input"/> to <paramref name="output"/> with the same name and content:
    /// <c>[Content_Types].xml</c> first, an OpenDocument <c>mimetype</c> first and uncompressed (its signature),
    /// everything else deflated, without directory entries, data descriptors or extra fields. A name that appears
    /// twice keeps its first part.
    /// </summary>
    public static void Repack(string input, string output)
    {
        using var source = ZipFile.OpenRead(input);
        var parts = source.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
            .OrderBy(e => e.FullName is ContentTypes or Mimetype ? 0 : 1)
            .ToList();

        // A seekable stream: the sizes go into the local headers instead of data descriptors.
        using var file = File.Create(output);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            if (!written.Add(part.FullName))
                continue;
            var level = part.FullName == Mimetype ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var copy = zip.CreateEntry(part.FullName, level);
            if (part.LastWriteTime.Year is >= 1980 and <= 2107)
                copy.LastWriteTime = part.LastWriteTime;
            using var from = part.Open();
            using var to = copy.Open();
            CopyBounded(from, to, part.FullName);
        }
    }
}
