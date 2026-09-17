using System.Runtime.InteropServices;

namespace FileSplitter;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        // Command-line mode: FileSplitter split <file> <size> [outDir] | FileSplitter join <anyPart> [outFile]
        if (args.Length > 0 && args[0].ToLowerInvariant() is "split" or "join" or "-h" or "--help" or "/?")
        {
            AttachConsole(-1);
            Console.WriteLine();
            return RunCli(args);
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length == 1 && File.Exists(args[0])) form.PreloadSplitFile(args[0]);
        Application.Run(form);
        return 0;
    }

    private static int RunCli(string[] args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "split" when args.Length is 3 or 4:
                {
                    if (!SplitEngine.TryParseSize(args[2], out long max))
                        throw new ArgumentException($"Invalid size \"{args[2]}\". Examples: 500KB, 100MB, 1.5GB");
                    string outDir = args.Length == 4 ? args[3] : Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
                    var parts = SplitEngine.SplitAsync(args[1], max, outDir, CliProgress()).GetAwaiter().GetResult();
                    Console.WriteLine($"\nCreated {parts.Count} part(s) in {outDir}");
                    return 0;
                }
                case "join" when args.Length is 2 or 3:
                {
                    var parts = SplitEngine.FindParts(Path.GetFullPath(args[1]));
                    string output = args.Length == 3 ? args[2] : SplitEngine.DefaultJoinOutput(parts[0]);
                    SplitEngine.JoinAsync(parts, output, CliProgress()).GetAwaiter().GetResult();
                    Console.WriteLine($"\nJoined {parts.Count} part(s) into {output}");
                    return 0;
                }
                default:
                    Console.WriteLine("""
                        Usage:
                          FileSplitter                                 Open the GUI
                          FileSplitter split <file> <maxSize> [outDir] Split a file (maxSize: 500KB, 100MB, 1.5GB)
                          FileSplitter join <anyPart> [outFile]        Join parts (e.g. video.mp4.001)
                        """);
                    return args[0].StartsWith('-') || args[0] == "/?" ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("\nError: " + ex.Message);
            return 1;
        }
    }

    private static IProgress<double> CliProgress()
    {
        int last = -1;
        return new SyncProgress(p =>
        {
            int pct = (int)(p * 100);
            if (pct == last) return;
            last = pct;
            Console.Write($"\r{pct,3}%");
        });
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
