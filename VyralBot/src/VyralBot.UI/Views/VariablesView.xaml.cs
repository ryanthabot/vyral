using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class VariablesView : UserControl
{
    private Point _lastClickPosition;

    public VariablesView()
    {
        InitializeComponent();
        
        // Subscribe to dialog open events to position at click location
        DataContextChanged += (s, e) =>
        {
            if (DataContext is VariablesViewModel viewModel)
            {
                viewModel.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(VariablesViewModel.IsAddDialogOpen) && viewModel.IsAddDialogOpen)
                    {
                        PositionDialogAtClick(AddDialogContent, AddDialogTransform);
                    }
                    else if (args.PropertyName == nameof(VariablesViewModel.IsEditDialogOpen) && viewModel.IsEditDialogOpen)
                    {
                        PositionDialogAtClick(EditDialogContent, EditDialogTransform);
                    }
                };
            }
        };
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        // Capture the position of the button that was clicked
        if (sender is Button button)
        {
            _lastClickPosition = button.TransformToAncestor(this).Transform(new Point(0, 0));
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // Capture the position of the button that was clicked
        if (sender is Button button)
        {
            _lastClickPosition = button.TransformToAncestor(this).Transform(new Point(0, 0));
        }
    }

    private void PositionDialogAtClick(FrameworkElement dialog, TranslateTransform transform)
    {
        if (dialog == null || transform == null) return;

        // Get the size of the view
        var viewWidth = ActualWidth;
        var viewHeight = ActualHeight;

        // Center the dialog on the click position
        var dialogWidth = dialog.Width;
        var dialogHeight = dialog.Height;

        // Calculate position to center dialog on click point
        var x = _lastClickPosition.X - (dialogWidth / 2);
        var y = _lastClickPosition.Y - (dialogHeight / 2);

        // Keep dialog within bounds
        x = Math.Max(20, Math.Min(x, viewWidth - dialogWidth - 20));
        y = Math.Max(20, Math.Min(y, viewHeight - dialogHeight - 20));

        transform.X = x;
        transform.Y = y;
    }
}
