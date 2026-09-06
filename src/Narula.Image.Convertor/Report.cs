using System.Globalization;

namespace Narula.Image.Convertor;

internal static class Report
{
    private const int MaxFailuresListed = 10;
    private const string Rule = "──────────────────────────────────────────";

    public static void Render(IReadOnlyList<ConversionResult> results, int totalPlanned, TimeSpan elapsed, CliOptions options)
    {
        int converted = results.Count(r => r.Outcome == Outcome.Converted);
        int copied = results.Count(r => r.Outcome == Outcome.Copied);
        int skipped = results.Count(r => r.Outcome == Outcome.Skipped);
        var failures = results.Where(r => r.Outcome == Outcome.Failed).ToList();

        long bytesIn = results.Where(Produced).Sum(r => r.BytesIn);
        long bytesOut = results.Where(Produced).Sum(r => r.BytesOut);

        double perSecond = elapsed.TotalSeconds > 0 ? results.Count / elapsed.TotalSeconds : results.Count;

        Console.WriteLine();
        Console.WriteLine(Rule);

        string noun = totalPlanned == 1 ? "file" : "files";
        string counted = results.Count == totalPlanned
            ? $"{results.Count} {noun}"
            : $"{results.Count} of {totalPlanned} {noun}";

        Console.WriteLine($"  {counted}    {elapsed:hh\\:mm\\:ss\\.f}    {perSecond.ToString("0.#", CultureInfo.InvariantCulture)}/sec");
        Console.WriteLine();

        int resized = results.Count(r => r.Outcome == Outcome.Converted && r.Reason is not null);

        WriteCount("Converted", converted, null);
        if (resized > 0) WriteCount("Resized", resized, $"(to fit {options.TargetType})");
        if (copied > 0) WriteCount("Copied", copied, $"(already {options.TargetType})");
        if (skipped > 0) WriteCount("Skipped", skipped, "(exists, -o false)");
        if (failures.Count > 0) WriteCount("Failed", failures.Count, null, ConsoleColor.Red);

        if (bytesIn > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {FormatBytes(bytesIn)} → {FormatBytes(bytesOut)}   ({DescribeChange(bytesIn, bytesOut)})");
        }

        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Failures");

            int pathWidth = failures.Take(MaxFailuresListed).Max(f => f.Item.RelativePath.Length);

            foreach (ConversionResult failure in failures.Take(MaxFailuresListed))
            {
                Console.WriteLine($"    {failure.Item.RelativePath.PadRight(pathWidth)}   {failure.Reason}");
            }

            if (failures.Count > MaxFailuresListed)
            {
                Console.WriteLine($"    ... {failures.Count - MaxFailuresListed} more");
            }
        }

        Console.WriteLine(Rule);
    }

    /// <summary>Only files that actually landed in the destination count toward the size comparison.</summary>
    private static bool Produced(ConversionResult result) =>
        result.Outcome is Outcome.Converted or Outcome.Copied;

    private static void WriteCount(string label, int count, string? note, ConsoleColor? colour = null)
    {
        string line = $"  {label.PadRight(11)}{count,5}";

        if (note is not null)
        {
            line += $"   {note}";
        }

        if (colour is null || Console.IsOutputRedirected)
        {
            Console.WriteLine(line);
            return;
        }

        ConsoleColor original = Console.ForegroundColor;
        Console.ForegroundColor = colour.Value;
        Console.WriteLine(line);
        Console.ForegroundColor = original;
    }

    private static string DescribeChange(long bytesIn, long bytesOut)
    {
        if (bytesIn == bytesOut)
        {
            return "same size";
        }

        double ratio = (double)bytesOut / bytesIn;

        return ratio < 1
            ? $"{Math.Round((1 - ratio) * 100)}% smaller"
            : $"{Math.Round((ratio - 1) * 100)}% larger";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string number = unit == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString(value < 10 ? "0.00" : value < 100 ? "0.0" : "0", CultureInfo.InvariantCulture);

        return $"{number} {units[unit]}";
    }
}
