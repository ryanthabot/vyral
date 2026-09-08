using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Embedded studio surface. The code-behind exists for one wiring the XAML
/// cannot express: the native <see cref="Controls.ObsPreviewHost"/> needs the
/// engine reference, so whenever the studio VM reports Ready (or the view is
/// reloaded into the tree) the host is attached to it.
/// </summary>
public partial class ObsStudioView : UserControl
{
    public ObsStudioView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += static (sender, _) => ((ObsStudioView)sender).AttachPreviewIfReady();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObsStudioViewModel old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is ObsStudioViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        AttachPreviewIfReady();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObsStudioViewModel.IsEngineReady))
        {
            AttachPreviewIfReady();
        }
    }

    private void AttachPreviewIfReady()
    {
        if (DataContext is ObsStudioViewModel { IsEngineReady: true } viewModel)
        {
            // Idempotent — the host no-ops when its display already exists.
            Preview.Attach(viewModel.Engine);
        }
    }
}
