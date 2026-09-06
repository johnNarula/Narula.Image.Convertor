using System.Diagnostics;
using ImageMagick;

namespace Narula.Image.Convertor;

/// <summary>
/// The whole run, from arguments to exit code. Separate from Program.cs so the exit-code
/// behaviour can be tested without launching a process.
/// </summary>
internal static class Application
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ParseOutcome parsed = CliOptions.Parse(args);

        if (parsed.HelpRequested)
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        if (parsed.FormatsRequested)
        {
            ImageFormats.WriteListing(Console.Out);
            return 0;
        }

        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"nImgConvertor: {parsed.Error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Run 'nImgConvertor -h' for usage.");
            return 2;
        }

        CliOptions options = parsed.Options;

        if (options.SourceIsSingleFile)
        {
            if (!File.Exists(Path.Combine(options.SourceRoot, options.SourcePattern)))
            {
                Console.Error.WriteLine($"nImgConvertor: source file not found: {options.SourceInput}");
                return 2;
            }
        }
        else if (!Directory.Exists(options.SourceRoot))
        {
            Console.Error.WriteLine($"nImgConvertor: source folder not found: {options.SourceRoot}");
            return 2;
        }

        ScanResult scan;

        try
        {
            scan = FileScanner.Scan(options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"nImgConvertor: could not read {options.SourceRoot}: {exception.Message}");
            return 2;
        }

        try
        {
            Directories.EnsureExists(options.DestinationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"nImgConvertor: could not create destination folder {options.DestinationPath}");
            Console.Error.WriteLine($"  {exception.Message}");

            if (OperatingSystem.IsWindows())
            {
                // Defender's Controlled Folder Access protects Documents, Pictures, Desktop and
                // their OneDrive equivalents, and refuses writes from apps not on its allow list.
                // It reports the refusal as "could not find file <the folder>", which reads like a
                // bug here rather than a policy decision somewhere else.
                Console.Error.WriteLine();
                Console.Error.WriteLine("  If that folder is under Documents, Pictures, Desktop or OneDrive, this is most");
                Console.Error.WriteLine("  likely Windows Defender Controlled Folder Access. To allow this tool:");
                Console.Error.WriteLine("    Windows Security > Virus & threat protection > Ransomware protection >");
                Console.Error.WriteLine("    Manage ransomware protection > Allow an app through Controlled folder access");
                Console.Error.WriteLine($"    Add: {Environment.ProcessPath}");
            }

            return 2;
        }

        if (scan.Total == 0)
        {
            Console.WriteLine(options.SourcePattern == "*"
                ? $"No image files found in {options.SourceRoot}."
                : $"No files matching {options.SourcePattern} found in {options.SourceRoot}.");
            return 0;
        }

        // We run our own workers, so ImageMagick must not also fan out per image.
        ResourceLimits.Thread = 1;

        ProgressReporter progress = new(scan.Items.Count);
        MagickConverter converter = new(options);

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ConversionResult> results =
            await ConversionEngine.RunAsync(scan.Items, options, converter, progress, cancellationToken);
        stopwatch.Stop();

        progress.Finish();

        List<ConversionResult> all = [.. results, .. scan.Rejected];
        Report.Render(all, scan.Total, stopwatch.Elapsed, options);

        return all.Any(r => r.Outcome == Outcome.Failed) ? 1 : 0;
    }
}
