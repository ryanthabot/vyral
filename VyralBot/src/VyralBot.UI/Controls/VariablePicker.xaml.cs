using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Controls;

public partial class VariablePicker : UserControl
{
    public event EventHandler? CloseRequested;
    private VariablePickerViewModel? _viewModel;
    private TextBox? _targetTextBox;

    public static readonly DependencyProperty PlatformProperty =
        DependencyProperty.Register(
            nameof(Platform),
            typeof(string),
            typeof(VariablePicker),
            new PropertyMetadata(null));

    public string? Platform
    {
        get => (string?)GetValue(PlatformProperty);
        set => SetValue(PlatformProperty, value);
    }

    public static readonly DependencyProperty TargetTextBoxProperty =
        DependencyProperty.Register(
            nameof(TargetTextBox),
            typeof(TextBox),
            typeof(VariablePicker),
            new PropertyMetadata(null, OnTargetTextBoxChanged));

    public TextBox? TargetTextBox
    {
        get => (TextBox?)GetValue(TargetTextBoxProperty);
        set => SetValue(TargetTextBoxProperty, value);
    }

    public static readonly DependencyProperty IsCompactModeProperty =
        DependencyProperty.Register(
            nameof(IsCompactMode),
            typeof(bool),
            typeof(VariablePicker),
            new PropertyMetadata(false, OnIsCompactModeChanged));

    public bool IsCompactMode
    {
        get => (bool)GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    public static readonly DependencyProperty IsPopupModeProperty =
        DependencyProperty.Register(
            nameof(IsPopupMode),
            typeof(bool),
            typeof(VariablePicker),
            new PropertyMetadata(false));

    public bool IsPopupMode
    {
        get => (bool)GetValue(IsPopupModeProperty);
        set => SetValue(IsPopupModeProperty, value);
    }

    private static void OnIsCompactModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VariablePicker picker && picker._viewModel != null)
        {
            picker._viewModel.IsCompactMode = (bool)e.NewValue;
        }
    }

    private static void OnTargetTextBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VariablePicker picker)
        {
            picker._targetTextBox = e.NewValue as TextBox;
        }
    }

    public VariablePicker()
    {
        InitializeComponent();
        Loaded += VariablePicker_Loaded;
    }

    private void VariablePicker_Loaded(object sender, RoutedEventArgs e)
    {
        // Get ViewModel from DI container
        if (Application.Current is App app && App.ServiceProvider != null)
        {
            _viewModel = App.ServiceProvider.GetService<VariablePickerViewModel>();
            if (_viewModel != null)
            {
                _viewModel.IsCompactMode = IsCompactMode;
                _viewModel.CurrentTargetTextBox = _targetTextBox;
                _viewModel.VariableSelected -= OnVariableSelected;
                _viewModel.VariableSelected += OnVariableSelected;

                // If in popup mode, load variables
                if (IsPopupMode)
                {
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await _viewModel.LoadVariablesAsync(Platform);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
    }

    private async void TriggerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            // The ViewModel is a DI singleton shared by every picker instance.
            // Re-point it at THIS picker's target before opening, otherwise with
            // multiple pickers alive the last-loaded one would receive the insert.
            _viewModel.CurrentTargetTextBox = TargetTextBox;
            await _viewModel.LoadVariablesAsync(Platform);
            VariablePopup.IsOpen = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        VariablePopup.IsOpen = false;
        _viewModel?.CloseCommand.Execute(null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is string category)
        {
            _viewModel?.FilterByCategoryCommand.Execute(category);
        }
    }

    private void VariableCard_Click(object sender, MouseButtonEventArgs e)
    {
        // Prevent multiple triggers from bubbling
        e.Handled = true;
        
        if (sender is FrameworkElement element && element.DataContext is VariableInfo variable)
        {
            _viewModel?.SelectVariableCommand.Execute(variable);
        }
    }

    private void OnVariableSelected(object? sender, EventArgs e)
    {
        VariablePopup.IsOpen = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
