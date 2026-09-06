using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Narula.Image.Convertor.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;

        SourceText.Text = "Drop a folder or image here";
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Source))
            {
                SourceText.Text = string.IsNullOrWhiteSpace(_model.Source)
                    ? "Drop a folder or image here"
                    : _model.Source;
            }
        };

        BrowseFolder.Click += BrowseFolderAsync;
        BrowseFile.Click += BrowseFileAsync;
        BrowseDestination.Click += BrowseDestinationAsync;
        ClearDestination.Click += (_, _) => _model.Destination = string.Empty;
        ClearIconSize.Click += (_, _) => _model.IconSize = null;
        ConvertButton.Click += ConvertAsync;

        // Dropping a folder is the reason this window exists: it is the one thing a browser
        // cannot do, and the thing that makes typing quoted paths unnecessary.
        DragDrop.SetAllowDrop(DropZone, true);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

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
