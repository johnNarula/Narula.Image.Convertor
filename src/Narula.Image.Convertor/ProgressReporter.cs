namespace Narula.Image.Convertor;

/// <summary>
/// Writes the "Processing x of y" line. On a real console it redraws one line in place and
/// throttles itself, because at a few thousand files the console becomes slower than the encoder.
/// When output is redirected it falls back to one plain line per file so logs and pipes still work.
/// </summary>
internal sealed class ProgressReporter
{
    private const int MinimumRedrawIntervalMs = 50;

    private readonly int _total;
    private readonly bool _inPlace;
    private readonly Lock _gate = new();

    private int _completed;
    private long _lastDrawTicks = -MinimumRedrawIntervalMs;
    private int _lastLineLength;

    public ProgressReporter(int total)
    {
        _total = total;
        _inPlace = !Console.IsOutputRedirected;
    }

    public void Report(string fileName)
    {
        int done = Interlocked.Increment(ref _completed);

        if (!_inPlace)
        {
            Console.WriteLine($"Processing {fileName} ({done} of {_total})...");
            return;
        }

        long now = Environment.TickCount64;

        lock (_gate)
        {
            // Always draw the final tick; throttle everything in between.
            if (done != _total && now - _lastDrawTicks < MinimumRedrawIntervalMs)
            {
                return;
            }

            _lastDrawTicks = now;
            Draw($"Processing {fileName} ({done} of {_total})...");
        }
    }

    /// <summary>Clears the progress line so the report starts on clean ground.</summary>
    public void Finish()
    {
        if (!_inPlace)
        {
            return;
        }

        lock (_gate)
        {
            if (_lastLineLength > 0)
            {
                Console.Write('\r' + new string(' ', _lastLineLength) + '\r');
                _lastLineLength = 0;
            }
        }
    }

    private void Draw(string line)
    {
        int width = ConsoleWidth();

        if (line.Length > width)
        {
            line = string.Concat(line.AsSpan(0, Math.Max(0, width - 3)), "...");
        }

        // Pad out to whatever the previous line occupied so nothing is left behind.
        string padded = line.PadRight(_lastLineLength);
        _lastLineLength = line.Length;

        Console.Write('\r');
        Console.Write(padded);
    }

    private static int ConsoleWidth()
    {
        try
        {
            int width = Console.WindowWidth - 1;
            return width > 20 ? width : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }
}
