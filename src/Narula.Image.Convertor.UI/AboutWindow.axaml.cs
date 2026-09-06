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
        Rows.ItemsSource = AboutDetails.Rows();

        AuthorLink.Content = AboutDetails.Author;
        AuthorLink.NavigateUri = new Uri(AboutDetails.AuthorLink);

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
