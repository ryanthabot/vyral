using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VyralBot.UI.Views;

public partial class CustomDialog : Window
{
    public bool Result { get; private set; }

    public CustomDialog(string title, string message, CustomDialogButtons buttons = CustomDialogButtons.OK, CustomDialogIcon icon = CustomDialogIcon.None)
    {
        InitializeComponent();
        
        // Set title with icon
        TitleText.Text = GetIconText(icon) + title;
        MessageText.Text = message;
        
        // Create buttons based on type
        CreateButtons(buttons);
    }

    private string GetIconText(CustomDialogIcon icon)
    {
        return icon switch
        {
            CustomDialogIcon.Warning => "⚠️ ",
            CustomDialogIcon.Error => "❌ ",
            CustomDialogIcon.Success => "✅ ",
            CustomDialogIcon.Info => "ℹ️ ",
            CustomDialogIcon.Question => "❓ ",
            _ => ""
        };
    }

    private void CreateButtons(CustomDialogButtons buttons)
    {
        ButtonPanel.Children.Clear();

        switch (buttons)
        {
            case CustomDialogButtons.OK:
                AddButton("OK", true, false);
                break;

            case CustomDialogButtons.YesNo:
                AddButton("NO", false, false, 12);
                AddButton("YES", true, true);
                break;

            case CustomDialogButtons.YesNoCancel:
                AddButton("CANCEL", false, false, 12);
                AddButton("NO", false, false, 12);
                AddButton("YES", true, true);
                break;
        }
    }

    private void AddButton(string content, bool isDefault, bool isDangerous, int rightMargin = 0)
    {
        var button = new Button
        {
            Content = content,
            Width = 100,
            Height = 40,
            Margin = new Thickness(0, 0, rightMargin, 0)
        };

            if (isDefault)
            {
                button.Style = (Style)FindResource("MaterialDesignRaisedButton");
                if (isDangerous)
                {
                    button.Background = (Brush)FindResource("ErrorBrush");
                    button.Foreground = (Brush)(TryFindResource("TextOnPrimaryBrush") ?? Brushes.White);
                }
                else
                {
                    button.Background = (Brush)FindResource("PrimaryBrush");
                    button.Foreground = (Brush)(TryFindResource("TextOnPrimaryBrush") ?? Brushes.White);
                }
            }
        else
        {
            button.Style = (Style)FindResource("MaterialDesignFlatButton");
            button.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }

        button.Click += (sender, e) =>
        {
            if (content == "YES" || content == "OK")
            {
                Result = true;
                DialogResult = true;
            }
            else if (content == "NO")
            {
                Result = false;
                DialogResult = false;
            }
            else // CANCEL
            {
                Result = false;
                DialogResult = false;
            }
            Close();
        };

        ButtonPanel.Children.Add(button);
    }
}

public enum CustomDialogButtons
{
    OK,
    YesNo,
    YesNoCancel
}

public enum CustomDialogIcon
{
    None,
    Info,
    Warning,
    Error,
    Success,
    Question
}
