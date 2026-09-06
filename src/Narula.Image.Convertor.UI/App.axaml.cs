using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

// 'Narula.Image.Convertor.Application' shadows Avalonia's, exactly as the engine's namespace
// shadows SixLabors' and ImageMagick's image types elsewhere.
using AvaloniaApplication = Avalonia.Application;

namespace Narula.Image.Convertor.UI;

public partial class App : AvaloniaApplication
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
