using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Full OBS surface. The code-behind exists for one wiring the XAML cannot
/// express: the ObsWindowEmbedHost needs the ObsWindowHostService reference,
/// so whenever the host VM is present (or the view is reloaded into the tree)
/// the control is attached to it. The control is idempotent — it completes the
/// embed itself when OBS reaches an embeddable state and parks the window on
/// its own teardown.
/// </summary>
public partial class ObsHostView : UserControl
{
    public ObsHostView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += static (sender, _) => ((ObsHostView)sender).AttachEmbedIfInstalled();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObsHostViewModel old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.EmbedRequested -= OnEmbedRequested;
        }

        if (e.NewValue is ObsHostViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.EmbedRequested += OnEmbedRequested;
        }

        AttachEmbedIfInstalled();
    }

    private void OnEmbedRequested()
    {
        AttachEmbedIfInstalled();
        Embed.EnsureEmbedded();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The install finished while the view was open — the embed surface
        // appears and needs the service.
        if (e.PropertyName == nameof(ObsHostViewModel.IsObsInstalled))
        {
            AttachEmbedIfInstalled();
        }
    }

    private void AttachEmbedIfInstalled()
    {
        if (DataContext is ObsHostViewModel { IsObsInstalled: true } viewModel)
        {
            Embed.Attach(viewModel.HostService);
        }
    }
}
