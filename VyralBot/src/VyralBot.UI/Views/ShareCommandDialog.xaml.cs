using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.Services;

namespace VyralBot.UI.Views;

/// <summary>Result of a confirmed share (dialog inputs).</summary>
public sealed record ShareCommandResult(string Name, string? Description, string Category, string? Author);

/// <summary>
/// Share form for publishing the open flow to the command marketplace.
/// Modeled on ConfirmDialog (borderless window, theme brushes).
/// </summary>
public partial class ShareCommandDialog : Window
{
    /// <summary>The confirmed share inputs, or null when cancelled.</summary>
    public ShareCommandResult? Result { get; private set; }

    public System.Collections.Generic.IReadOnlyList<CategoryOption> Categories { get; } =
        MarketplaceCategories.All.Select(c => new CategoryOption(c, char.ToUpperInvariant(c[0]) + c[1..])).ToList();

    public ShareCommandDialog(string defaultName, string defaultDescription, string defaultAuthor)
    {
        InitializeComponent();
        DataContext = this;

        NameBox.Text = defaultName ?? string.Empty;
        DescriptionBox.Text = defaultDescription ?? string.Empty;
        AuthorBox.Text = defaultAuthor ?? string.Empty;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.CaretIndex = NameBox.Text.Length; };
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            return;
        }

        Result = new ShareCommandResult(
            NameBox.Text.Trim(),
            string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            (CategoryBox.SelectedItem as CategoryOption)?.Key ?? MarketplaceCategories.Other,
            string.IsNullOrWhiteSpace(AuthorBox.Text) ? null : AuthorBox.Text.Trim());
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>Combo entry for the marketplace category dropdown.</summary>
public sealed record CategoryOption(string Key, string Label);
