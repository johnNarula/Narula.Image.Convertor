using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class UiLauncherTests
{
    [Fact]
    public void Finds_the_window_executable_beside_the_running_one()
    {
        // The test project references the UI, so img2imgUI sits beside the test host exactly as
        // it sits beside img2img.exe in a published folder.
        string? found = UiLauncher.Locate();

        Assert.NotNull(found);
        Assert.True(File.Exists(found));
        Assert.Equal("img2imgUI", Path.GetFileNameWithoutExtension(found));
    }

    [Fact]
    public async Task The_library_never_launches_anything_of_its_own_accord()
    {
        // Only the executable turns an empty command line into a window; the library prints help,
        // so tests and scripted callers cannot accidentally spawn a process.
        StringWriter captured = new();
        TextWriter original = Console.Out;
        Console.SetOut(captured);

        int exitCode;
        try
        {
            exitCode = await Application.RunAsync([]);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", captured.ToString());
    }
}
