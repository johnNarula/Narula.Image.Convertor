using System.Collections.Concurrent;

namespace Narula.Image.Convertor;

internal static class ConversionEngine
{
    /// <summary>
    /// Runs the work list. Workers share nothing but a counter and the cancellation token, so the
    /// only ordering guarantee is that every item returns exactly one result.
    /// </summary>
    public static async Task<IReadOnlyList<ConversionResult>> RunAsync(
        IReadOnlyList<WorkItem> items,
        CliOptions options,
        IImageConverter converter,
        ProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ConcurrentBag<ConversionResult> results = [];

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        ParallelOptions parallel = new()
        {
            MaxDegreeOfParallelism = options.Parallelism,
            CancellationToken = cancellation.Token,
        };

        try
        {
            await Parallel.ForEachAsync(items, parallel, async (item, token) =>
            {
                ConversionResult result = await converter.ConvertAsync(item, options, token).ConfigureAwait(false);

                results.Add(result);
                progress.Report(Path.GetFileName(item.SourcePath));

                if (result.Outcome == Outcome.Failed && options.StopOnError)
                {
                    // Let in-flight work finish; just stop handing out new files.
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Either -e tripped or the user pressed Ctrl+C. Report on what actually completed.
        }

        return [.. results];
    }
}
