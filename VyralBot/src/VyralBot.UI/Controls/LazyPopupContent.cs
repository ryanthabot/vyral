using System;
using System.Windows;
using System.Windows.Controls;

namespace VyralBot.UI.Controls;

/// <summary>
/// A ContentControl that only loads its content when it becomes visible.
/// Used for popup content to avoid allocating memory until the popup is opened.
/// </summary>
public class LazyPopupContent : ContentControl
{
    private bool _isLoaded = false;
    private Type? _contentType;
    private object? _cachedContent;

    public static readonly DependencyProperty ContentTypeProperty =
        DependencyProperty.Register(
            nameof(ContentType),
            typeof(Type),
            typeof(LazyPopupContent),
            new PropertyMetadata(null, OnContentTypeChanged));

    /// <summary>
    /// The type of the UserControl to create when the popup becomes visible
    /// </summary>
    public Type? ContentType
    {
        get => (Type?)GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    private static void OnContentTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LazyPopupContent control)
        {
            control._contentType = e.NewValue as Type;
            control._isLoaded = false;
            control._cachedContent = null;
            control.Content = null;
        }
    }

    public LazyPopupContent()
    {
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && !_isLoaded && _contentType != null)
        {
            LoadContent();
        }
    }

    private void LoadContent()
    {
        if (_contentType == null || _isLoaded) return;

        try
        {
            // Check if we can get from DI container first
            var serviceProvider = App.ServiceProvider;
            if (serviceProvider != null)
            {
                _cachedContent = serviceProvider.GetService(_contentType);
            }
            
            // Fall back to Activator if DI didn't return anything
            if (_cachedContent == null)
            {
                _cachedContent = Activator.CreateInstance(_contentType);
            }

            Content = _cachedContent;
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LazyPopupContent: Failed to create {_contentType?.Name}: {ex.Message}");
        }
    }
}
