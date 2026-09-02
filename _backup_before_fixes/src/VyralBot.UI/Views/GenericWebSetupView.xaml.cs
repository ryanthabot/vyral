using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class GenericWebSetupView : UserControl
{
    public GenericWebSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2 on demand when user clicks a button (exact copy of Twitch/Kick)
        if (DataContext is BaseWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (WebView2Browser == null)
            return;

        // Always ensure WebView2 is initialized (re-initialize if needed) - exact copy of Twitch/Kick
        try
        {
            if (WebView2Browser.CoreWebView2 == null)
            {
                await WebView2Browser.EnsureCoreWebView2Async();
            }

            if (DataContext is BaseWebSetupViewModel viewModel)
            {
                viewModel.WebView2 = WebView2Browser;
            }
        }
        catch
        {
            // Failed to initialize WebView2
        }
    }

    private void OnWebView2DisposeRequested(object? sender, EventArgs e)
    {
        // Exact copy of Twitch/Kick disposal logic
        try
        {
            if (WebView2Browser?.CoreWebView2 != null)
            {
                // Dispose the control
                WebView2Browser.Dispose();

                // Force garbage collection to free memory immediately
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch
        {
            // Error disposing WebView2
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BaseWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
        }
    }

    // Dependency Properties for Content Injection
    public static readonly DependencyProperty StepperHeaderContentProperty =
        DependencyProperty.Register(nameof(StepperHeaderContent), typeof(object), typeof(GenericWebSetupView),
            new PropertyMetadata(null, OnStepperHeaderContentChanged));

    public static readonly DependencyProperty StepContentProperty =
        DependencyProperty.Register(nameof(StepContent), typeof(object), typeof(GenericWebSetupView),
            new PropertyMetadata(null, OnStepContentChanged));

    public static readonly DependencyProperty ActionBarContentProperty =
        DependencyProperty.Register(nameof(ActionBarContent), typeof(object), typeof(GenericWebSetupView),
            new PropertyMetadata(null, OnActionBarContentChanged));

    public static readonly DependencyProperty ModalContentProperty =
        DependencyProperty.Register(nameof(ModalContent), typeof(object), typeof(GenericWebSetupView),
            new PropertyMetadata(null, OnModalContentChanged));

    public static readonly DependencyProperty SuccessContentProperty =
        DependencyProperty.Register(nameof(SuccessContent), typeof(object), typeof(GenericWebSetupView),
            new PropertyMetadata(null, OnSuccessContentChanged));

    public object StepperHeaderContent
    {
        get => GetValue(StepperHeaderContentProperty);
        set => SetValue(StepperHeaderContentProperty, value);
    }

    public object StepContent
    {
        get => GetValue(StepContentProperty);
        set => SetValue(StepContentProperty, value);
    }

    public object ActionBarContent
    {
        get => GetValue(ActionBarContentProperty);
        set => SetValue(ActionBarContentProperty, value);
    }

    public object ModalContent
    {
        get => GetValue(ModalContentProperty);
        set => SetValue(ModalContentProperty, value);
    }

    public object SuccessContent
    {
        get => GetValue(SuccessContentProperty);
        set => SetValue(SuccessContentProperty, value);
    }

    private static void OnStepperHeaderContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GenericWebSetupView view)
            view.StepperHeaderPresenter.Content = e.NewValue;
    }

    private static void OnStepContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GenericWebSetupView view)
            view.StepContentPresenter.Content = e.NewValue;
    }

    private static void OnActionBarContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GenericWebSetupView view)
            view.ActionBarPresenter.Content = e.NewValue;
    }

    private static void OnModalContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GenericWebSetupView view)
            view.ModalContentPresenter.Content = e.NewValue;
    }

    private static void OnSuccessContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GenericWebSetupView view)
            view.SuccessContentPresenter.Content = e.NewValue;
    }
}
