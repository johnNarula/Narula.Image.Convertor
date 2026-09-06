using System.Diagnostics;

using ImageMagick;

namespace Narula.Image.Convertor;

internal enum RunStatus
{
    Completed,
    HelpRequested,
    FormatsRequested,
    InvalidArguments,
    SourceMissing,
    DestinationUnavailable,
    SourceUnreadable,
    NothingToDo,
}

/// <summary>Everything that happened, with nothing yet printed.</summary>
internal sealed record RunOutcome(
    RunStatus Status,
    string? Message,
    CliOptions? Options,
    IReadOnlyList<ConversionResult> Results,
    int TotalPlanned,
    TimeSpan Elapsed)
{
    public bool AnyFailed => Results.Any(r => r.Outcome == Outcome.Failed);

    public bool WriteRefused => Results.Any(r => r.Outcome == Outcome.Failed && r.Reason == ConversionResult.PermissionDenied);

    public static RunOutcome Stopped(RunStatus status, string? message = null, CliOptions? options = null) =>
        new(status, message, options, [], 0, TimeSpan.Zero);
}

/// <summary>
/// One conversion from arguments to results, with no opinion about how any of it is displayed.
/// The console front end and the window both go through here, so neither can develop behaviour
/// the other lacks.
/// </summary>
internal static class ConversionRun
{
    public static async Task<RunOutcome> ExecuteAsync(string[] args, IProgressSink progress, CancellationToken cancellationToken)
    {
        ParseOutcome parsed = CliOptions.Parse(args);

        if (parsed.HelpRequested)
        {
            return RunOutcome.Stopped(RunStatus.HelpRequested);
        }

        if (parsed.FormatsRequested)
        {
            return RunOutcome.Stopped(RunStatus.FormatsRequested);
        }

        if (parsed.Options is null)
        {
            return RunOutcome.Stopped(RunStatus.InvalidArguments, parsed.Error);
        }

        CliOptions options = parsed.Options;

        if (options.SourceIsSingleFile)
        {
            if (!File.Exists(Path.Combine(options.SourceRoot, options.SourcePattern)))
            {
                return RunOutcome.Stopped(RunStatus.SourceMissing, $"source file not found: {options.SourceInput}", options);
            }
        }
        else if (!Directory.Exists(options.SourceRoot))
        {
            return RunOutcome.Stopped(RunStatus.SourceMissing, $"source folder not found: {options.SourceRoot}", options);
        }

        ScanResult scan;

        try
        {
            scan = FileScanner.Scan(options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RunOutcome.Stopped(RunStatus.SourceUnreadable, $"could not read {options.SourceRoot}: {exception.Message}", options);
        }

        if (scan.Total == 0)
        {
            string message = options.SourcePattern == "*"
                ? $"No image files found in {options.SourceRoot}."
                : $"No files matching {options.SourcePattern} found in {options.SourceRoot}.";

            return RunOutcome.Stopped(RunStatus.NothingToDo, message, options);
        }

        try
        {
            Directories.EnsureExists(options.DestinationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RunOutcome.Stopped(RunStatus.DestinationUnavailable, exception.Message, options);
        }

        // We run our own workers, so ImageMagick must not also fan out per image.
        ResourceLimits.Thread = 1;

        MagickConverter converter = new(options);

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ConversionResult> results =
            await ConversionEngine.RunAsync(scan.Items, options, converter, progress, cancellationToken);
        stopwatch.Stop();

        progress.Finish();

        return new RunOutcome(
            RunStatus.Completed,
            null,
            options,
            [.. results, .. scan.Rejected],
            scan.Total,
            stopwatch.Elapsed);
    }
}
