using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;
    private bool _isUserAtBottom = true;
    private int _unreadCount = 0;
    private ScrollViewer? _scrollViewer;
    private Window? _hostWindow;

    public ChatView()
    {
        InitializeComponent();

        Loaded += ChatView_Loaded;
        Unloaded += ChatView_Unloaded;
        DataContextChanged += ChatView_DataContextChanged;
    }

    private void ChatView_Loaded(object? sender, RoutedEventArgs e)
    {
        TryAttachViewModel();
        HookHostWindow();
    }

    private void ChatView_Unloaded(object? sender, RoutedEventArgs e)
    {
        UnhookHostWindow();
        DetachViewModel();
    }

    /// <summary>
    /// Subscribes to the host window's Deactivated event so open popups are
    /// closed when the app loses activation (e.g. minimize) — otherwise WPF
    /// Popups float topmost above every other application.
    /// </summary>
    private void HookHostWindow()
    {
        var window = Window.GetWindow(this);
        if (_hostWindow == window) return;

        UnhookHostWindow();
        _hostWindow = window;
        if (_hostWindow != null)
        {
            _hostWindow.Deactivated += HostWindow_Deactivated;
        }
    }

    private void UnhookHostWindow()
    {
        if (_hostWindow != null)
        {
            _hostWindow.Deactivated -= HostWindow_Deactivated;
            _hostWindow = null;
        }
    }

    private void HostWindow_Deactivated(object? sender, EventArgs e)
    {
        CloseAllPopups();
    }

    /// <summary>
    /// Closes every popup owned by this view (platform quick-settings flyouts
    /// and the message-input platform selector) via their view-model flags.
    /// </summary>
    private void CloseAllPopups()
    {
        if (DataContext is not ChatViewModel vm) return;

        vm.IsPlatformSelectorOpen = false;
        vm.IsAllPlatformsSettingsOpen = false;
        vm.IsTwitchSettingsOpen = false;
        vm.IsKickSettingsOpen = false;
        vm.IsYoutubeSettingsOpen = false;
        vm.IsTiktokSettingsOpen = false;
        vm.IsDiscordSettingsOpen = false;
    }

    private void ChatView_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        TryAttachViewModel();
    }

    private void TryAttachViewModel()
    {
        // Ensure previous handlers removed before attaching to avoid double-subscribe
        DetachViewModel();
        _vm = DataContext as ChatViewModel;
        if (_vm != null)
        {
            // Hook collection changes
            _vm.Messages.CollectionChanged += Messages_CollectionChanged;

            // Find the ScrollViewer inside the ListBox for scroll tracking
            _scrollViewer = GetScrollViewer(ChatScrollViewer);
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += ChatScrollViewer_ScrollChanged;
            }

            // If there are existing messages and auto-scroll is enabled, scroll to end
            if (_vm.AutoScroll && _vm.Messages.Count > 0)
            {
                // Use dispatcher to ensure layout is ready
                Dispatcher.BeginInvoke(() => ScrollToEnd(), DispatcherPriority.Loaded);
            }
        }
    }

    private void DetachViewModel()
    {
        if (_vm != null)
        {
            try { _vm.Messages.CollectionChanged -= Messages_CollectionChanged; } catch { }
            if (_scrollViewer != null)
            {
                try { _scrollViewer.ScrollChanged -= ChatScrollViewer_ScrollChanged; } catch { }
            }
            _vm = null;
            _scrollViewer = null;
        }
    }

    /// <summary>
    /// Finds the ScrollViewer inside a ListBox using visual tree traversal
    /// </summary>
    private static ScrollViewer? GetScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer sv) return sv;
        
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Scrolls the ListBox to the last item
    /// </summary>
    private void ScrollToEnd()
    {
        if (_vm != null && _vm.Messages.Count > 0)
        {
            ChatScrollViewer.ScrollIntoView(_vm.Messages[_vm.Messages.Count - 1]);
        }
    }

    private void ChatScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Consider user at bottom if within a small threshold of the extent
        var sv = sender as ScrollViewer;
        if (sv == null) return;

        // If content height changed (new messages), and user was at bottom before, keep them at bottom
        if (e.ExtentHeightChange != 0)
        {
            if (_isUserAtBottom && _vm?.AutoScroll == true)
            {
                // New content appended - scroll to end
                ScrollToEnd();
                return;
            }
        }

        // Update _isUserAtBottom flag based on current offset
        var tolerance = 20.0; // pixels
        var wasAtBottom = _isUserAtBottom;
        _isUserAtBottom = (sv.VerticalOffset + sv.ViewportHeight) >= (sv.ExtentHeight - tolerance);

        // If user scrolled up (left bottom), disable AutoScroll
        if (!wasAtBottom && _isUserAtBottom)
        {
            // user returned to bottom
            if (_vm != null)
            {
                _vm.AutoScroll = true;
                // Clear unread count and hide banner
                _unreadCount = 0;
                NewMessagesButton.Visibility = Visibility.Collapsed;
            }
        }
        else if (wasAtBottom && !_isUserAtBottom)
        {
            // user scrolled up
            if (_vm != null)
            {
                _vm.AutoScroll = false;
            }
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only react to additions
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        // If AutoScroll is enabled, always scroll to end so new messages are visible.
        if (_vm != null && _vm.AutoScroll)
        {
            // Use dispatcher to wait for layout update before scrolling
            Dispatcher.BeginInvoke(() => ScrollToEnd(), DispatcherPriority.Background);
            return;
        }

        // If AutoScroll is disabled (user scrolled up), increment unread counter and show banner
        _unreadCount += e.NewItems?.Count ?? 1;
        Dispatcher.BeginInvoke(() =>
        {
            NewMessagesButton.Content = _unreadCount == 1 ? "1 new message" : $"{_unreadCount} new messages";
            NewMessagesButton.Visibility = Visibility.Visible;
        }, DispatcherPriority.Background);
    }

    private void NewMessagesButton_Click(object? sender, RoutedEventArgs e)
    {
        // Scroll to bottom and re-enable auto-scroll
        Dispatcher.BeginInvoke(() =>
        {
            ScrollToEnd();
            if (_vm != null) _vm.AutoScroll = true;
            _unreadCount = 0;
            NewMessagesButton.Visibility = Visibility.Collapsed;
        }, DispatcherPriority.Background);
    }
}
