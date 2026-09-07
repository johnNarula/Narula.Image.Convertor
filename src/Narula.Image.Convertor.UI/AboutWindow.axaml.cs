using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Narula.Image.Convertor.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        ProductText.Text = AboutDetails.Product;
        VersionText.Text = AboutDetails.Version;
        SummaryText.Text = AboutDetails.Summary;
        CopyrightText.Text = AboutDetails.Copyright;
        LicenceText.Text = AboutDetails.Licence;
        Rows.ItemsSource = AboutDetails.Rows();

        AuthorLink.Content = AboutDetails.Author;
        AuthorLink.NavigateUri = new Uri(AboutDetails.AuthorLink);

        McpHeading.Text = AboutDetails.McpHeading;
        McpSummary.Text = AboutDetails.McpSummary;
        McpCommand.Text = AboutDetails.McpClaudeCode;
        McpOther.Text = AboutDetails.McpOtherClients;
        McpTools.Text = AboutDetails.McpTools;

        // With no server installed the instructions would be a recipe for a path that is not
        // there, so only the explanation of what is missing is shown.
        McpCommand.IsVisible = AboutDetails.McpAvailable;
        McpOther.IsVisible = AboutDetails.McpAvailable;
        McpTools.IsVisible = AboutDetails.McpAvailable;
        McpCopyButton.IsVisible = AboutDetails.McpAvailable;

        McpCopyButton.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(AboutDetails.McpSnippet());
                McpCopyButton.Content = "Copied";
            }
        };

        CloseButton.Click += (_, _) => Close();
        CopyButton.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(AboutDetails.AsText());
                CopyButton.Content = "Copied";
            }
        };
    }
}
