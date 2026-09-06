using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Narula.Image.Convertor;

namespace Narula.Image.Convertor.UI;

/// <summary>
/// Everything the window does, with no reference to Avalonia, so the behaviour can be tested
/// without a display. The view binds to this and does nothing else of consequence.
/// </summary>
internal sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Func<string[], IProgressSink, CancellationToken, Task<RunOutcome>> _run;

    private CancellationTokenSource? _cancellation;

    private string _source = string.Empty;
    private string _destination = string.Empty;
    private string _target = "jpg";
    private bool _recursive;
    private int _quality = Defaults.Current.Quality;
    private bool _overwrite = Defaults.Current.Overwrite;
    private bool _preserveTransparency = Defaults.Current.PreserveTransparency;
    private bool _preserveMetadata = Defaults.Current.PreserveMetadata;
    private string _background = Defaults.Current.Background;
    private string _sourceFilter = AllTypes;
    private int? _iconSize;
    private bool _running;
    private double _progress;
    private string _status = "Choose a source to begin.";
    private string _summary = string.Empty;

    public MainViewModel()
        : this(ConversionRun.ExecuteAsync)
    {
    }

    /// <summary>Test seam: lets a test supply the run instead of converting real files.</summary>
    public MainViewModel(Func<string[], IProgressSink, CancellationToken, Task<RunOutcome>> run)
    {
        _run = run;

        // Everything that can actually be written, with the everyday ones first so the list is
        // useful before a single character is typed.
        string[] common = ["jpg", "png", "webp", "avif", "tiff", "bmp", "gif", "ico", "jxl", "pdf"];
        List<(string Name, string Description)> writable = [.. ImageFormats.WritableFormats()];

        Targets =
        [
            .. common.Where(c => writable.Any(w => w.Name == c)),
            .. writable.Select(w => w.Name).Where(n => !common.Contains(n)),
        ];

        SourceFilters = [AllTypes, .. FileScanner.KnownImageExtensions];
        IconSizes = [.. CliOptions.IconSizes];
    }

    /// <summary>The filter entry meaning "do not narrow by extension".</summary>
    public const string AllTypes = "All Supported Image Types";

    public ObservableCollection<string> Targets { get; }

    public ObservableCollection<int> IconSizes { get; }

    public ObservableCollection<string> SourceFilters { get; }

    /// <summary>Narrows a folder scan to one extension, by turning the source into a glob.</summary>
    public string SourceFilter
    {
        get => _sourceFilter;
        set => Set(ref _sourceFilter, string.IsNullOrWhiteSpace(value) ? AllTypes : value);
    }

    public ObservableCollection<string> Failures { get; } = [];

    public string Source
    {
        get => _source;
        set
        {
            if (Set(ref _source, value))
            {
                Notify(nameof(CanConvert));
                Notify(nameof(DestinationHint));
                Notify(nameof(DestinationNote));
                Notify(nameof(IconSizeApplies));
            }
        }
    }

    /// <summary>Empty means the engine's default: a folder beside the source.</summary>
    public string Destination
    {
        get => _destination;
        set
        {
            if (Set(ref _destination, value))
            {
                Notify(nameof(DestinationHint));
                Notify(nameof(DestinationNote));
            }
        }
    }

    public string Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value ?? string.Empty))
            {
                Notify(nameof(CanConvert));
                Notify(nameof(TargetDescription));
                Notify(nameof(DestinationHint));
                Notify(nameof(DestinationNote));
                Notify(nameof(IconSizeApplies));
                Notify(nameof(QualityApplies));
            }
        }
    }

    public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }
    public int Quality { get => _quality; set => Set(ref _quality, value); }
    public bool Overwrite { get => _overwrite; set => Set(ref _overwrite, value); }
    public bool PreserveTransparency { get => _preserveTransparency; set => Set(ref _preserveTransparency, value); }
    public bool PreserveMetadata { get => _preserveMetadata; set => Set(ref _preserveMetadata, value); }
    public string Background { get => _background; set => Set(ref _background, value); }

    /// <summary>Null means "largest" when reading an icon, and "every size" when writing one.</summary>
    public int? IconSize { get => _iconSize; set => Set(ref _iconSize, value); }

    public bool Running
    {
        get => _running;
        private set
        {
            if (Set(ref _running, value))
            {
                Notify(nameof(CanConvert));
                Notify(nameof(CanCancel));
                Notify(nameof(IsIdle));
            }
        }
    }

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public bool CanConvert => !Running && !string.IsNullOrWhiteSpace(Source) && TargetIsWritable;

    /// <summary>Only while there is something to stop.</summary>
    public bool CanCancel => Running;

    /// <summary>Everything that sets up a run is locked while one is in flight.</summary>
    public bool IsIdle => !Running;

    /// <summary>Icon sizes only mean something when an icon is on one end of the conversion.</summary>
    public bool IconSizeApplies =>
        Target.Equals("ico", StringComparison.OrdinalIgnoreCase) ||
        Source.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);

    public bool QualityApplies => !Target.Equals("png", StringComparison.OrdinalIgnoreCase);

    /// <summary>ImageMagick's own words for the chosen format, shown under the picker.</summary>
    public string TargetDescription => ImageFormats.Describe(Target) ?? string.Empty;

    /// <summary>False while the typed target is not a format anything can write.</summary>
    public bool TargetIsWritable => ImageFormats.ResolveTarget(Target) is not null;

    /// <summary>Shows where output will land, so nobody has to guess what the default does.</summary>
    public string DestinationHint
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Destination))
            {
                return Destination;
            }

            if (string.IsNullOrWhiteSpace(Source))
            {
                return "Choose a source, and this shows exactly where output will go.";
            }

            string root = Directory.Exists(Source) ? Source : Path.GetDirectoryName(Source) ?? Source;
            return Path.Combine(root, string.Format(Defaults.Current.DestinationFolderFormat, Target));
        }
    }

    /// <summary>
    /// Says out loud that the default folder does not have to exist yet, since the path shown for
    /// it looks identical to one that does.
    /// </summary>
    public string DestinationNote
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Source))
            {
                return string.Empty;
            }

            bool usingDefault = string.IsNullOrWhiteSpace(Destination);
            string target = usingDefault ? DestinationHint : Destination;

            string which = usingDefault ? "Default, beside the source." : "Chosen folder.";
            string state = Directory.Exists(target) ? "It already exists." : "Created when you convert.";

            return $"{which}  {state}";
        }
    }

    /// <summary>
    /// The source as the engine should see it. A filter on a folder becomes a glob, which the
    /// engine already understands as "only these names, and honour that extension explicitly".
    /// </summary>
    public string EffectiveSource =>
        SourceFilter != AllTypes && Directory.Exists(Source)
            ? Path.Combine(Source, "*" + SourceFilter)
            : Source;

    public ConversionRequest BuildRequest() => new()
    {
        Source = EffectiveSource,
        Destination = string.IsNullOrWhiteSpace(Destination) ? null : Destination,
        Target = Target,
        Recursive = Recursive,
        Quality = Quality,
        Overwrite = Overwrite,
        PreserveTransparency = PreserveTransparency,
        PreserveMetadata = PreserveMetadata,
        Background = Background,
        IconSize = IconSizeApplies ? IconSize : null,
    };

    public async Task ConvertAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConvert)
        {
            return;
        }

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;

        Running = true;
        Progress = 0;
        Summary = string.Empty;
        Failures.Clear();
        Status = "Working...";

        try
        {
            RunOutcome outcome = await _run(BuildRequest().ToArguments(), new ViewModelProgress(this), cancellation.Token);

            // The engine stops handing out work on cancellation and returns what finished, so a
            // cancelled run still has a real result worth showing rather than a bare message.
            Present(outcome, cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception exception)
        {
            Status = "Failed to run.";
            Summary = exception.Message;
        }
        finally
        {
            _cancellation = null;
            Running = false;
        }
    }

    /// <summary>Stops the run. What has already been written stays written.</summary>
    public void Cancel()
    {
        if (Running)
        {
            Status = "Stopping...";
            _cancellation?.Cancel();
        }
    }

    private void Present(RunOutcome outcome, bool cancelled = false)
    {
        if (outcome.Status != RunStatus.Completed)
        {
            Status = outcome.Status switch
            {
                RunStatus.NothingToDo => outcome.Message ?? "Nothing to convert.",
                RunStatus.InvalidArguments => $"Check the settings: {outcome.Message}",
                RunStatus.DestinationUnavailable => "Could not create the destination folder.",
                _ => outcome.Message ?? "Nothing to do.",
            };

            if (outcome.Status == RunStatus.DestinationUnavailable)
            {
                // The commonest cause by far, and invisible without saying so.
                Summary = $"{outcome.Message}\n\nIf the destination is under Documents, Pictures, Desktop or "
                    + "OneDrive, Windows Defender Controlled Folder Access is the likely cause. Allow "
                    + "img2imgUI.exe through it in Windows Security, under Ransomware protection.";
            }

            return;
        }

        int converted = outcome.Results.Count(r => r.Outcome == Outcome.Converted);
        int copied = outcome.Results.Count(r => r.Outcome == Outcome.Copied);
        int skipped = outcome.Results.Count(r => r.Outcome == Outcome.Skipped);
        var failed = outcome.Results.Where(r => r.Outcome == Outcome.Failed).ToList();

        long bytesIn = outcome.Results.Where(Produced).Sum(r => r.BytesIn);
        long bytesOut = outcome.Results.Where(Produced).Sum(r => r.BytesOut);

        List<string> parts = [$"{converted} converted"];
        if (copied > 0) parts.Add($"{copied} copied");
        if (skipped > 0) parts.Add($"{skipped} skipped");
        if (failed.Count > 0) parts.Add($"{failed.Count} failed");

        string size = bytesIn > 0
            ? $"   {Bytes(bytesIn)} to {Bytes(bytesOut)}"
            : string.Empty;

        Summary = $"{string.Join(", ", parts)} in {outcome.Elapsed.TotalSeconds:0.0}s{size}";

        Status = cancelled
            ? "Cancelled. Files already converted have been kept."
            : failed.Count > 0 ? "Finished with failures." : "Finished.";

        if (!cancelled)
        {
            Progress = 100;
        }

        foreach (ConversionResult failure in failed)
        {
            Failures.Add($"{failure.Item.RelativePath} — {failure.Reason}");
        }

        if (outcome.WriteRefused)
        {
            Failures.Add("Windows refused these writes. Allow img2imgUI.exe through Controlled Folder Access.");
        }
    }

    private static bool Produced(ConversionResult result) =>
        result.Outcome is Outcome.Converted or Outcome.Copied;

    private static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} B" : $"{value:0.0} {units[unit]}";
    }

    private void OnProgress(int completed, int total, string fileName)
    {
        Progress = total == 0 ? 0 : completed * 100.0 / total;
        Status = $"{fileName}  ({completed} of {total})";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Bridges engine progress onto the view model.</summary>
    private sealed class ViewModelProgress(MainViewModel owner) : IProgressSink
    {
        public void Report(int completed, int total, string fileName) => owner.OnProgress(completed, total, fileName);

        public void Finish()
        {
        }
    }
}
