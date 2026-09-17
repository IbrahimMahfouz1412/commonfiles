using System.Globalization;
using System.Text.RegularExpressions;

namespace FileSplitter;

public static class SplitEngine
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Splits <paramref name="inputPath"/> into parts named "name.ext.001", "name.ext.002", ...</summary>
    public static async Task<IReadOnlyList<string>> SplitAsync(
        string inputPath, long maxPartBytes, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (maxPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPartBytes), "Max part size must be greater than 0.");

        Directory.CreateDirectory(outputDir);
        var created = new List<string>();
        var buffer = new byte[BufferSize];

        try
        {
            await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            long total = input.Length;
            long partCount = Math.Max(1, (total + maxPartBytes - 1) / maxPartBytes);
            int digits = Math.Max(3, partCount.ToString(CultureInfo.InvariantCulture).Length);
            string baseName = Path.Combine(outputDir, Path.GetFileName(inputPath));
            long done = 0;

            for (long part = 1; part <= partCount; part++)
            {
                string partPath = $"{baseName}.{part.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')}";
                created.Add(partPath);

                await using var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                long remaining = Math.Min(maxPartBytes, total - done);
                while (remaining > 0)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (read == 0) throw new IOException("Unexpected end of input file.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    remaining -= read;
                    done += read;
                    progress?.Report(total == 0 ? 1 : (double)done / total);
                }
            }

            progress?.Report(1);
            return created;
        }
        catch
        {
            DeleteQuietly(created);
            throw;
        }
    }

    /// <summary>Finds the full sequence of parts starting from any one of them (e.g. "movie.mp4.001").</summary>
    public static IReadOnlyList<string> FindParts(string anyPartPath)
    {
        var match = Regex.Match(anyPartPath, @"^(?<base>.+)\.(?<num>\d+)$");
        if (!match.Success)
            throw new ArgumentException("Selected file is not a part file (expected a name ending in .001, .002, ...).");

        string basePath = match.Groups["base"].Value;
        int digits = match.Groups["num"].Value.Length;
        var parts = new List<string>();
        for (long i = 1; ; i++)
        {
            string p = $"{basePath}.{i.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')}";
            if (!File.Exists(p)) break;
            parts.Add(p);
        }

        if (parts.Count == 0)
            throw new FileNotFoundException($"First part not found: {basePath}.{"1".PadLeft(digits, '0')}");
        return parts;
    }

    public static string DefaultJoinOutput(string anyPartPath) =>
        Regex.Replace(anyPartPath, @"\.\d+$", "");

    public static async Task JoinAsync(
        IReadOnlyList<string> parts, string outputPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        long total = parts.Sum(p => new FileInfo(p).Length);
        long done = 0;
        var buffer = new byte[BufferSize];

        try
        {
            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            foreach (string part in parts)
            {
                await using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(total == 0 ? 1 : (double)done / total);
                }
            }
            progress?.Report(1);
        }
        catch
        {
            DeleteQuietly([outputPath]);
            throw;
        }
    }

    /// <summary>Parses sizes like "500", "250KB", "1.5 GB", "700m".</summary>
    public static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        var m = Regex.Match(text.Trim(), @"^(?<n>\d+(\.\d+)?)\s*(?<u>b|k|kb|m|mb|g|gb|t|tb)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        double n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        long mult = m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "k" or "kb" => 1L << 10,
            "m" or "mb" => 1L << 20,
            "g" or "gb" => 1L << 30,
            "t" or "tb" => 1L << 40,
            _ => 1,
        };
        double result = Math.Floor(n * mult);
        if (result < 1 || result > long.MaxValue) return false;
        bytes = (long)result;
        return true;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.##} {units[i]}";
    }

    private static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
    }
}
