using Narula.Image.Convertor;
using Narula.Image.Convertor.UI;

namespace ImageConvertor.Tests;

public class AboutTests
{
    [Fact]
    public void The_version_is_major_minor_year_and_day()
    {
        // 1.0.YY.MMDD, with the month-day padded. The numeric assembly version cannot hold the
        // leading zero, which is exactly why the informational version is the one on show.
        string[] parts = AppInfo.Version.Split('.');

        Assert.Equal(4, parts.Length);
        Assert.Equal("1", parts[0]);
        Assert.Equal(2, parts[2].Length);
        Assert.Equal(4, parts[3].Length);

        int monthDay = int.Parse(parts[3]);
        Assert.InRange(monthDay / 100, 1, 12);
        Assert.InRange(monthDay % 100, 1, 31);
    }

    [Fact]
    public void The_version_carries_no_build_metadata()
    {
        // Source control stamps "+commit" onto the informational version; nobody wants to read it.
        Assert.DoesNotContain('+', AppInfo.Version);
    }

    [Fact]
    public void The_about_box_states_what_this_build_can_do()
    {
        (int readable, int writable) = AppInfo.FormatCounts;

        Assert.True(readable > 200, $"only {readable} readable formats");
        Assert.True(writable > 150, $"only {writable} writable formats");

        IReadOnlyList<AboutRow> rows = AboutDetails.Rows();

        Assert.Contains(rows, r => r.Label == "Formats" && r.Value.Contains($"{readable} readable"));
        Assert.Contains(rows, r => r.Label == "Engine" && r.Value.Contains("Magick.NET"));
        Assert.Contains(rows, r => r.Label == "Runtime");
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Value), $"{r.Label} was empty"));
    }

    [Fact]
    public void The_copyable_text_holds_everything_the_box_shows()
    {
        string text = AboutDetails.AsText();

        Assert.Contains(AboutDetails.Product, text);
        Assert.Contains(AppInfo.Version, text);
        Assert.All(AboutDetails.Rows(), r => Assert.Contains(r.Label, text));
        Assert.Contains(AboutDetails.AuthorLink, text);
    }

    [Fact]
    public void Settings_are_looked_for_in_your_profile_before_the_install_folder()
    {
        string[] searched = [.. Defaults.SearchPaths];

        Assert.Equal(2, searched.Length);
        Assert.Equal(Defaults.UserSettingsPath, searched[0]);
        Assert.Equal(Defaults.InstalledSettingsPath, searched[1]);
        Assert.Contains("img2img", Defaults.UserSettingsPath);
    }

    [Fact]
    public void The_about_box_says_where_the_licences_went()
    {
        // The executables are single-file, so nothing beside them reveals what is inside unless
        // the window says so.
        string licence = AboutDetails.Licence;

        Assert.Contains("MIT", licence);
        Assert.Contains("LICENSE.txt", licence);
        Assert.Contains("THIRD-PARTY-NOTICES.md", licence);
        Assert.Contains("Apache-2.0", licence);
        Assert.Contains(AboutDetails.InstallFolder, licence);

        Assert.Contains(licence, AboutDetails.AsText());
    }
}
