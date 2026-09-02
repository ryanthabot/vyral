using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using VyralBot.UI.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Behaviors;

/// <summary>
/// Attached behavior that enables variable picker functionality for TextBox context menus.
/// </summary>
public static class TextBoxVariableBehavior
{
    public static readonly DependencyProperty EnableVariablePickerProperty =
        DependencyProperty.RegisterAttached(
            "EnableVariablePicker",
            typeof(bool),
            typeof(TextBoxVariableBehavior),
            new PropertyMetadata(false, OnEnableVariablePickerChanged));

    public static readonly DependencyProperty IsVariablesMenuItemProperty =
        DependencyProperty.RegisterAttached(
            "IsVariablesMenuItem",
            typeof(bool),
            typeof(TextBoxVariableBehavior),
            new PropertyMetadata(false, OnIsVariablesMenuItemChanged));

    public static bool GetEnableVariablePicker(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableVariablePickerProperty);
    }

    public static void SetEnableVariablePicker(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableVariablePickerProperty, value);
    }

    public static bool GetIsVariablesMenuItem(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsVariablesMenuItemProperty);
    }

    public static void SetIsVariablesMenuItem(DependencyObject obj, bool value)
    {
        obj.SetValue(IsVariablesMenuItemProperty, value);
    }

    private static void OnEnableVariablePickerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Property exists to mark TextBoxes that have variable picker enabled
    }

    private static void OnIsVariablesMenuItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem menuItem || e.NewValue is not true) return;

        // Wire up the click handler for the Variables menu item
        menuItem.Click += OnVariablesMenuItemClick;
        
        // Also handle when the menu item loads to ensure we have proper context
        menuItem.Loaded += (s, args) =>
        {
            // Find parent TextBox
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is TextBox textBox)
            {
                // Store the textbox reference for later use
                menuItem.Tag = textBox;
            }
        };
    }

    private static void OnVariablesMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        
        // Find the TextBox - try Tag first, then traverse visual tree
        TextBox? textBox = menuItem.Tag as TextBox;
        if (textBox == null)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            textBox = contextMenu?.PlacementTarget as TextBox;
        }
        
        if (textBox == null) return;

        // Close the context menu
        if (menuItem.Parent is ContextMenu cm)
        {
            cm.IsOpen = false;
        }

        // Create popup for variable picker, centered in main window
        var mainWindow = Application.Current?.MainWindow;
        var popup = new Popup
        {
            Placement = PlacementMode.Center,
            PlacementTarget = mainWindow,
            AllowsTransparency = true,
            StaysOpen = false,
            IsOpen = false
        };


        var variablePicker = new VariablePicker
        {
            TargetTextBox = textBox,
            IsCompactMode = true,
            IsPopupMode = true
        };

        // Set DataContext using DI so variables load
        if (variablePicker.DataContext == null)
        {
            if (Application.Current is App app && App.ServiceProvider != null)
            {
                variablePicker.DataContext = App.ServiceProvider.GetService(typeof(VyralBot.UI.ViewModels.VariablePickerViewModel));
            }
        }

        // Wire up close logic for X button and variable selection
        void ClosePopupHandler(object? s, EventArgs e)
        {
            popup.IsOpen = false;
        }
        variablePicker.CloseRequested += (s, e) => popup.IsOpen = false;
        if (variablePicker.DataContext is VariablePickerViewModel vm)
        {
            vm.VariableSelected += ClosePopupHandler;
        }

        // Try to get platform from DataContext
        if (textBox.DataContext != null)
        {
            var dataContextType = textBox.DataContext.GetType();
            var platformProperty = dataContextType.GetProperty("SelectedPlatform");
            if (platformProperty != null)
            {
                var platform = platformProperty.GetValue(textBox.DataContext);
                if (platform != null)
                {
                    var nameProperty = platform.GetType().GetProperty("Name");
                    if (nameProperty != null)
                    {
                        variablePicker.Platform = nameProperty.GetValue(platform)?.ToString();
                    }
                }
            }
        }

        popup.Child = variablePicker;
        
        // Load variables and show popup
        var viewModel = variablePicker.DataContext as VariablePickerViewModel;
        if (viewModel != null)
        {
            // Use Dispatcher to ensure popup opens after menu closes
            textBox.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await viewModel.LoadVariablesAsync(null); // Show all variables, ignore platform
                popup.IsOpen = true;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            // Fallback: just open the popup
            textBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                popup.IsOpen = true;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
