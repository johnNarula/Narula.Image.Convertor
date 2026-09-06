using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Narula.Image.Convertor.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();

        // Anything passed on the command line — by img2img -ui, or by the Explorer entry the
        // installer adds — before the window is shown, so it opens already filled in.
        _model.Apply(Program.Prefill);

        DataContext = _model;

        // Painted from the model rather than set to the placeholder outright, because a source
        // passed on the command line is already in place by the time this runs.
        ShowSource();

        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Source))
            {
                ShowSource();
            }
        };

        BrowseFolder.Click += BrowseFolderAsync;
        BrowseFile.Click += BrowseFileAsync;
        BrowseDestination.Click += BrowseDestinationAsync;
        ClearDestination.Click += (_, _) => _model.Destination = string.Empty;
        ClearIconSize.Click += (_, _) => _model.IconSize = null;
        ConvertButton.Click += ConvertAsync;
        CancelButton.Click += (_, _) => _model.Cancel();
        AboutButton.Click += (_, _) => new AboutWindow().ShowDialog(this);

        // The chevrons make a filtering box behave like the drop-down it resembles.
        TargetDropDown.Click += (_, _) => Toggle(TargetBox);
        FilterDropDown.Click += (_, _) => Toggle(FilterBox);
        TargetBox.KeyDown += (_, _) => Filter(TargetBox);
        FilterBox.KeyDown += (_, _) => Filter(FilterBox);

        // Dropping a folder is the reason this window exists: it is the one thing a browser
        // cannot do, and the thing that makes typing quoted paths unnecessary.
        DragDrop.SetAllowDrop(DropZone, true);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// Opens the list from the chevron. Two details make it behave like a drop-down rather than
    /// like a search box: the whole list is shown, not what is left after filtering on whatever
    /// is already typed, and focus is placed in the box before the list opens, because
    /// AutoCompleteBox closes itself the moment it loses focus.
    /// </summary>
    private void ShowSource() =>
        SourceText.Text = string.IsNullOrWhiteSpace(_model.Source)
            ? "Drop a folder or image here"
            : _model.Source;

    private static void Toggle(AutoCompleteBox box)
    {
        if (box.IsDropDownOpen)
        {
            box.IsDropDownOpen = false;
            return;
        }

        box.Focus();

        // Focusing an AutoCompleteBox selects all of its text, which reads as a highlighted
        // block rather than a value. Put the caret at the end instead, as a drop-down would.
        if (box.FindDescendantOfType<TextBox>() is { } editor)
        {
            editor.SelectionStart = editor.SelectionEnd = editor.Text?.Length ?? 0;
        }

        box.FilterMode = AutoCompleteFilterMode.None;
        box.IsDropDownOpen = true;
    }

    /// <summary>Typing filters again, undoing the full listing the chevron asked for.</summary>
    private static void Filter(AutoCompleteBox box) => box.FilterMode = AutoCompleteFilterMode.Contains;

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { Length: > 0 } path)
        {
            _model.Source = path;
        }
    }

    private async void BrowseFolderAsync(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder to convert",
            AllowMultiple = false,
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
        {
            _model.Source = path;
        }
    }

    private async void BrowseFileAsync(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an image to convert",
            AllowMultiple = false,
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
        {
            _model.Source = path;
        }
    }

    private async void BrowseDestinationAsync(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where converted files go",
            AllowMultiple = false,
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
        {
            _model.Destination = path;
        }
    }

    private async void ConvertAsync(object? sender, RoutedEventArgs e) => await _model.ConvertAsync();
}
