using CS2ChatTranslator.Common;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace CS2ChatTranslator.Overlay;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int DRAG_HOTKEY_ID = 9000;
    private const int CLOSE_HOTKEY_ID = 9001;
    private const int REPLY_HOTKEY_ID = 9002;
    private const int CANCEL_HOTKEY_ID = 9003;
    private const int MOVE_LEFT_HOTKEY_ID = 9010;
    private const int MOVE_UP_HOTKEY_ID = 9011;
    private const int MOVE_RIGHT_HOTKEY_ID = 9012;
    private const int MOVE_DOWN_HOTKEY_ID = 9013;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_D = 0x44;
    private const int VK_Q = 0x51;
    private const int VK_R = 0x52;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private const string PositionFile = "overlay_position.json";
    private const int PositionFileVersion = 2;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly AppSettings _settings;
    private readonly GoogleTranslator _translator;
    private readonly ChatMessageParser _parser;
    private readonly List<MessageBubble> _messages = [];
    private ChatLogWatcher? _watcher;
    private HwndSource? _hwndSource;
    private MessageBubble? _selectedMessage;
    private IntPtr _previousForegroundWindow;
    private double? _normalTopBeforeReply;
    private bool _dragMode;
    private bool _replyMode;
    private bool _isSending;

    private double OverlayWidth => Clamp(_settings.Overlay.Width, 340.0, 680.0);
    private double BubbleMaxWidth => Math.Max(250.0, OverlayWidth * 0.78);
    private double BodyFontSize => Clamp(_settings.Overlay.FontSize, 12.0, 24.0);
    private double HeaderFontSize => Clamp(_settings.Overlay.HeaderFontSize, 10.0, 18.0);
    private int MaxVisibleMessages => Clamp(_settings.Overlay.MaxMessages, 1, 12);
    private int ReplyHistoryMessages => Clamp(_settings.Overlay.ReplyHistoryMessages, MaxVisibleMessages, 100);
    private double MessageLifeSec => Clamp(_settings.Overlay.MessageLifeSeconds, 3.0, 30.0);
    private double FadeOutSec => Clamp(_settings.Overlay.FadeOutSeconds, 0.2, 5.0);
    private byte BubbleAlpha => (byte)Math.Round(Clamp(_settings.Overlay.BackgroundOpacity, 0.55, 1.0) * 255.0);
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    public OverlayWindow()
    {
        InitializeComponent();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        _settings = configuration.Get<AppSettings>() ?? new AppSettings();
        _translator = new GoogleTranslator(
            string.IsNullOrWhiteSpace(_settings.Translator.GoogleApiKey)
                ? null
                : _settings.Translator.GoogleApiKey);
        _parser = new ChatMessageParser();

        ApplyOverlaySettings();
        LoadPosition();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void ApplyOverlaySettings()
    {
        Width = OverlayWidth;
        OverlayRoot.Width = OverlayWidth;
        OverlayRoot.MaxWidth = OverlayWidth;
        ReplyPreviewText.MaxWidth = Math.Max(220.0, OverlayWidth - 90.0);
        UpdateHistoryCount();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnableClickThrough();
        RegisterHotkeys();

        _watcher = new ChatLogWatcher(_settings.CS2.LogPath, _parser, OnNewMessage);
        _watcher.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, DRAG_HOTKEY_ID);
            UnregisterHotKey(_hwndSource.Handle, CLOSE_HOTKEY_ID);
            UnregisterHotKey(_hwndSource.Handle, REPLY_HOTKEY_ID);
            UnregisterInteractionHotkeys();
            _hwndSource.Dispose();
        }

        foreach (var message in _messages)
            message.LifetimeTimer.Stop();

        _watcher?.Dispose();
        _translator.Dispose();
    }

    private void RegisterHotkeys()
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource.AddHook(HwndHook);
        RegisterHotKey(_hwndSource.Handle, DRAG_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_D);
        RegisterHotKey(_hwndSource.Handle, CLOSE_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_Q);
        RegisterHotKey(_hwndSource.Handle, REPLY_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_R);
    }

    private void RegisterInteractionHotkeys(bool includeMovement)
    {
        if (_hwndSource == null)
            return;

        UnregisterInteractionHotkeys();
        RegisterHotKey(_hwndSource.Handle, CANCEL_HOTKEY_ID, 0, VK_ESCAPE);

        if (!includeMovement)
            return;

        RegisterHotKey(_hwndSource.Handle, MOVE_LEFT_HOTKEY_ID, 0, VK_LEFT);
        RegisterHotKey(_hwndSource.Handle, MOVE_UP_HOTKEY_ID, 0, VK_UP);
        RegisterHotKey(_hwndSource.Handle, MOVE_RIGHT_HOTKEY_ID, 0, VK_RIGHT);
        RegisterHotKey(_hwndSource.Handle, MOVE_DOWN_HOTKEY_ID, 0, VK_DOWN);
    }

    private void UnregisterInteractionHotkeys()
    {
        if (_hwndSource == null)
            return;

        UnregisterHotKey(_hwndSource.Handle, CANCEL_HOTKEY_ID);
        UnregisterHotKey(_hwndSource.Handle, MOVE_LEFT_HOTKEY_ID);
        UnregisterHotKey(_hwndSource.Handle, MOVE_UP_HOTKEY_ID);
        UnregisterHotKey(_hwndSource.Handle, MOVE_RIGHT_HOTKEY_ID);
        UnregisterHotKey(_hwndSource.Handle, MOVE_DOWN_HOTKEY_ID);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg != WM_HOTKEY)
            return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case DRAG_HOTKEY_ID:
                ToggleDragMode();
                handled = true;
                break;
            case CLOSE_HOTKEY_ID:
                Close();
                handled = true;
                break;
            case REPLY_HOTKEY_ID:
                ToggleReplyMode();
                handled = true;
                break;
            case CANCEL_HOTKEY_ID:
                ExitInteractiveMode();
                handled = true;
                break;
            case MOVE_LEFT_HOTKEY_ID:
                MoveOverlay(-12, 0);
                handled = true;
                break;
            case MOVE_UP_HOTKEY_ID:
                MoveOverlay(0, -12);
                handled = true;
                break;
            case MOVE_RIGHT_HOTKEY_ID:
                MoveOverlay(12, 0);
                handled = true;
                break;
            case MOVE_DOWN_HOTKEY_ID:
                MoveOverlay(0, 12);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private async Task OnNewMessage(ChatMessage msg)
    {
        if (!string.IsNullOrEmpty(_settings.CS2.PlayerName)
            && msg.Player.Equals(_settings.CS2.PlayerName, StringComparison.OrdinalIgnoreCase))
            return;

        var result = await _translator.TranslateAsync(msg.Message, _settings.Translator.TargetLanguage);
        if (!ChatMessageParser.NeedsTranslation(result.SourceLanguage, _settings.Translator.TargetLanguage)
            || string.IsNullOrWhiteSpace(result.TranslatedText))
            return;

        await Dispatcher.InvokeAsync(() =>
            ShowTranslation(msg, result.TranslatedText, result.SourceLanguage));
    }

    private void ShowTranslation(ChatMessage msg, string translation, string sourceLanguage)
    {
        while (_messages.Count >= ReplyHistoryMessages)
        {
            var oldest = _messages.FirstOrDefault(message => message != _selectedMessage) ?? _messages[0];
            RemoveHistoryMessage(oldest);
        }

        var (container, bubble) = BuildMessageVisual(msg, translation);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MessageLifeSec) };
        var item = new MessageBubble(msg, translation, sourceLanguage, container, bubble, timer);

        bubble.MouseLeftButtonDown += (_, e) =>
        {
            if (!_replyMode)
                return;

            SelectMessage(item);
            e.Handled = true;
        };

        timer.Tick += (_, _) =>
        {
            if (_replyMode || !item.IsDisplayed || item.IsRemoving)
                return;

            timer.Stop();
            FadeAndRemoveMessage(item);
        };

        _messages.Add(item);
        UpdateHistoryCount();
        if (_replyMode)
        {
            ShowMessageInPanel(item);
            if (_selectedMessage == null)
                SelectMessage(item);
            MessageScrollViewer.ScrollToEnd();
        }
        else
        {
            while (_messages.Count(message => message.IsDisplayed) >= MaxVisibleMessages)
            {
                var oldestVisible = _messages.First(message => message.IsDisplayed);
                HideMessage(oldestVisible);
            }

            ShowMessageInPanel(item, animate: true);
        }
    }

    private (FrameworkElement Container, Border Bubble) BuildMessageVisual(ChatMessage msg, string translation)
    {
        var bubbleColor = Color.FromArgb(BubbleAlpha, 58, 58, 60);
        var bubbleBrush = new SolidColorBrush(bubbleColor);

        var text = new TextBlock
        {
            Text = translation,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 245, 247)),
            FontFamily = UiFont,
            FontSize = BodyFontSize,
            FontWeight = FontWeights.Normal,
            LineHeight = BodyFontSize * 1.28,
            MaxWidth = BubbleMaxWidth - 36.0,
            MaxHeight = 130.0,
            TextWrapping = TextWrapping.Wrap
        };

        var bubble = new Border
        {
            Background = bubbleBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12, 7, 12, 8),
            Margin = new Thickness(7, 0, 0, 0),
            MaxWidth = BubbleMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = text,
            Effect = CreateBubbleShadow()
        };

        var tail = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 12,0 C 11,7 7,11 0,12 C 7,13 14,10 18,5 Z"),
            Fill = bubbleBrush,
            Width = 18,
            Height = 13,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            IsHitTestVisible = false
        };

        var bubbleLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = BubbleMaxWidth + 7.0
        };
        bubbleLayer.Children.Add(tail);
        bubbleLayer.Children.Add(bubble);

        var player = new TextBlock
        {
            Text = msg.Player,
            Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 235, 235, 245)),
            FontFamily = UiFont,
            FontSize = HeaderFontSize,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = BubbleMaxWidth - 70.0,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var time = new TextBlock
        {
            Text = msg.Timestamp.ToString("HH:mm"),
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 235, 235, 245)),
            FontFamily = UiFont,
            FontSize = Math.Max(10.0, HeaderFontSize - 1.0),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            Margin = new Thickness(18, 0, 0, 4),
            MaxWidth = BubbleMaxWidth - 12.0
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(time, 1);
        header.Children.Add(player);
        header.Children.Add(time);

        var container = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 7),
            Opacity = 0,
            RenderTransform = new TranslateTransform(-12, 0)
        };
        container.Children.Add(header);
        container.Children.Add(bubbleLayer);

        return (container, bubble);
    }

    private void ApplyReplySurfaceStyle()
    {
        ConversationSurface.Background = new SolidColorBrush(Color.FromArgb(0xF2, 28, 28, 30));
        ConversationSurface.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 90, 90, 95));
        ConversationSurface.BorderThickness = new Thickness(1);
        ConversationSurface.Padding = new Thickness(12);
        ConversationSurface.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 32,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = 0.52
        };
    }

    private void ClearReplySurfaceStyle()
    {
        ConversationSurface.Background = Brushes.Transparent;
        ConversationSurface.BorderBrush = Brushes.Transparent;
        ConversationSurface.BorderThickness = new Thickness(0);
        ConversationSurface.Padding = new Thickness(0);
        ConversationSurface.Effect = null;
    }

    private void UpdateHistoryCount()
    {
        if (HistoryCountText == null)
            return;

        HistoryCountText.Text = _messages.Count switch
        {
            0 => "No recent translations",
            1 => "1 recent translation",
            _ => $"{_messages.Count} recent translations"
        };
    }

    private void ToggleReplyMode()
    {
        if (_replyMode)
        {
            ExitReplyMode();
            return;
        }

        EnterReplyMode();
    }

    private void EnterReplyMode()
    {
        if (_dragMode)
            LeaveDragMode(restoreFocus: false);

        CaptureForegroundWindow();
        _normalTopBeforeReply = Top;
        _replyMode = true;
        DisableClickThrough();
        RegisterInteractionHotkeys(includeMovement: false);
        ApplyReplySurfaceStyle();
        ReplyHeader.Visibility = Visibility.Visible;
        HeaderDivider.Visibility = Visibility.Visible;
        ReplyPanel.Visibility = Visibility.Visible;
        MessageScrollViewer.MaxHeight = 330;
        MessagePanel.Children.Clear();

        foreach (var message in _messages)
        {
            message.LifetimeTimer.Stop();
            message.Bubble.Cursor = Cursors.Hand;
            ShowMessageInPanel(message);
        }

        if (_messages.Count > 0)
        {
            SelectMessage(_messages[^1]);
        }
        else
        {
            _selectedMessage = null;
            ReplyingToText.Text = "No message selected";
            ReplyPreviewText.Text = "Wait for a translated message";
            ReplyTextBox.IsEnabled = false;
        }

        ActivateInteractiveWindow();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            UpdateLayout();
            KeepInteractiveWindowOnScreen();
            MessageScrollViewer.ScrollToEnd();
            ReplyTextBox.Focus();
            Keyboard.Focus(ReplyTextBox);
        });
        UpdateSendButton();
    }

    private void ExitReplyMode(bool restoreFocus = true)
    {
        if (!_replyMode)
            return;

        SetSelectedVisual(_selectedMessage, selected: false);
        _selectedMessage = null;
        _replyMode = false;
        _isSending = false;
        UnregisterInteractionHotkeys();
        ClearReplySurfaceStyle();
        ReplyHeader.Visibility = Visibility.Collapsed;
        HeaderDivider.Visibility = Visibility.Collapsed;
        ReplyPanel.Visibility = Visibility.Collapsed;
        MessageScrollViewer.MaxHeight = 420;
        ReplyStatusText.Visibility = Visibility.Collapsed;
        ReplyTextBox.Clear();
        ReplyTextBox.IsEnabled = true;
        MessagePanel.Children.Clear();

        foreach (var message in _messages)
        {
            message.Bubble.Cursor = Cursors.Arrow;
            message.IsDisplayed = false;
            message.IsRemoving = false;
        }

        RestoreRecentMessages();

        if (_normalTopBeforeReply.HasValue)
        {
            Top = _normalTopBeforeReply.Value;
            _normalTopBeforeReply = null;
        }

        EnableClickThrough();
        if (restoreFocus)
            RestoreForegroundWindow();
    }

    private void SelectMessage(MessageBubble message)
    {
        if (!_replyMode || message.IsRemoving)
            return;

        SetSelectedVisual(_selectedMessage, selected: false);
        _selectedMessage = message;
        SetSelectedVisual(message, selected: true);

        ReplyingToText.Text = $"Reply to {message.Message.Player}";
        ReplyPreviewText.Text = message.Translation;
        ReplyTextBox.IsEnabled = true;
        ReplyStatusText.Visibility = Visibility.Collapsed;
        UpdateSendButton();
        ReplyTextBox.Focus();
        message.Container.BringIntoView();
    }

    private static void SetSelectedVisual(MessageBubble? message, bool selected)
    {
        if (message == null)
            return;

        message.Bubble.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(10, 132, 255))
            : Brushes.Transparent;
        message.Bubble.Effect = selected
            ? new DropShadowEffect
            {
                Color = Color.FromRgb(10, 132, 255),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55
            }
            : CreateBubbleShadow();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await TranslateAndCopyReplyAsync();

    private async Task TranslateAndCopyReplyAsync()
    {
        var selected = _selectedMessage;
        var reply = ReplyTextBox.Text.Trim();
        if (_isSending || selected == null || string.IsNullOrWhiteSpace(reply))
            return;

        _isSending = true;
        ReplyTextBox.IsEnabled = false;
        ReplyStatusText.Text = "Translating...";
        ReplyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(100, 210, 255));
        ReplyStatusText.Visibility = Visibility.Visible;
        UpdateSendButton();

        var result = await _translator.TranslateAsync(reply, selected.SourceLanguage);
        if (result.SourceLanguage == "error" || string.IsNullOrWhiteSpace(result.TranslatedText))
        {
            _isSending = false;
            ReplyTextBox.IsEnabled = true;
            ReplyStatusText.Text = "Could not translate. Try again.";
            ReplyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
            UpdateSendButton();
            ReplyTextBox.Focus();
            return;
        }

        try
        {
            Clipboard.SetText(result.TranslatedText);
        }
        catch
        {
            _isSending = false;
            ReplyTextBox.IsEnabled = true;
            ReplyStatusText.Text = "Could not access the clipboard.";
            ReplyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
            UpdateSendButton();
            return;
        }

        ReplyStatusText.Text = "Copied to clipboard";
        ReplyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88));
        await Task.Delay(650);

        if (IsVisible)
            ExitReplyMode();
    }

    private void ReplyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReplyPlaceholder.Visibility = string.IsNullOrEmpty(ReplyTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSendButton();
    }

    private async void ReplyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ExitReplyMode();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await TranslateAndCopyReplyAsync();
        }
    }

    private void UpdateSendButton()
    {
        if (SendButton == null || ReplyTextBox == null)
            return;

        SendButton.IsEnabled = !_isSending
            && _selectedMessage != null
            && !string.IsNullOrWhiteSpace(ReplyTextBox.Text);
    }

    private void CancelReply_Click(object sender, RoutedEventArgs e)
        => ExitReplyMode();

    private void ToggleDragMode()
    {
        if (_dragMode)
        {
            LeaveDragMode();
            return;
        }

        if (_replyMode)
            ExitReplyMode(restoreFocus: false);

        CaptureForegroundWindow();
        _dragMode = true;
        DisableClickThrough();
        RegisterInteractionHotkeys(includeMovement: true);
        DragHandle.Visibility = Visibility.Visible;
        ActivateInteractiveWindow();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            UpdateLayout();
            KeepInteractiveWindowOnScreen();
            Focus();
        });
    }

    private void LeaveDragMode(bool restoreFocus = true)
    {
        if (!_dragMode)
            return;

        _dragMode = false;
        UnregisterInteractionHotkeys();
        DragHandle.Visibility = Visibility.Collapsed;
        SavePosition();
        EnableClickThrough();

        if (restoreFocus)
            RestoreForegroundWindow();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_dragMode
            || e.ButtonState != MouseButtonState.Pressed
            || FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;

        try
        {
            DragMove();
            KeepInteractiveWindowOnScreen();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_replyMode || _dragMode))
        {
            ExitInteractiveMode();
            e.Handled = true;
            return;
        }

        if (_replyMode && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Up)
            {
                SelectRelativeMessage(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                SelectRelativeMessage(1);
                e.Handled = true;
                return;
            }
        }

        if (!_dragMode)
            return;

        var movement = e.Key switch
        {
            Key.Left => (-12.0, 0.0),
            Key.Up => (0.0, -12.0),
            Key.Right => (12.0, 0.0),
            Key.Down => (0.0, 12.0),
            _ => (0.0, 0.0)
        };

        if (movement == (0.0, 0.0))
            return;

        MoveOverlay(movement.Item1, movement.Item2);
        e.Handled = true;
    }

    private void SelectRelativeMessage(int offset)
    {
        if (!_replyMode || _messages.Count == 0)
            return;

        var currentIndex = _selectedMessage == null
            ? _messages.Count - 1
            : _messages.IndexOf(_selectedMessage);
        var targetIndex = Clamp(currentIndex + offset, 0, _messages.Count - 1);
        SelectMessage(_messages[targetIndex]);
    }

    private void ExitInteractiveMode()
    {
        if (_replyMode)
            ExitReplyMode();
        else if (_dragMode)
            LeaveDragMode();
    }

    private void MoveOverlay(double deltaX, double deltaY)
    {
        if (!_dragMode)
            return;

        Left += deltaX;
        Top += deltaY;
        KeepInteractiveWindowOnScreen();
    }

    private void KeepInteractiveWindowOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : OverlayWidth;
        var height = ActualHeight > 0 ? ActualHeight : 80.0;
        Left = Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
    }

    private void ActivateInteractiveWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetForegroundWindow(hwnd);
        Activate();
        Focus();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match)
                return match;
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void DoneMoving_Click(object sender, RoutedEventArgs e)
        => LeaveDragMode();

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        SetDefaultPosition();
        KeepInteractiveWindowOnScreen();
        SavePosition();
    }

    private void CaptureForegroundWindow()
    {
        var current = GetForegroundWindow();
        var ownWindow = new WindowInteropHelper(this).Handle;
        if (current != IntPtr.Zero && current != ownWindow)
            _previousForegroundWindow = current;
    }

    private void RestoreForegroundWindow()
    {
        if (_previousForegroundWindow == IntPtr.Zero)
            return;

        var target = _previousForegroundWindow;
        _previousForegroundWindow = IntPtr.Zero;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => SetForegroundWindow(target));
    }

    private void EnableClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    private void DisableClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (style | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT);
    }

    private void LoadPosition()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, PositionFile);
            if (File.Exists(path))
            {
                var position = JsonSerializer.Deserialize<OverlayPosition>(File.ReadAllText(path));
                if (position is { Version: PositionFileVersion })
                {
                    Left = position.Left;
                    Top = position.Top;
                    return;
                }
            }
        }
        catch
        {
        }

        SetDefaultPosition();
    }

    private void SetDefaultPosition()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Left + 24;
        Top = screen.Top + (screen.Height * 0.58);
    }

    private void SavePosition()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, PositionFile);
            File.WriteAllText(path, JsonSerializer.Serialize(new OverlayPosition(Left, Top, PositionFileVersion)));
        }
        catch
        {
        }
    }

    private void FadeAndRemoveMessage(MessageBubble message)
    {
        if (message.IsRemoving)
            return;

        message.IsRemoving = true;
        AnimateFade(message.Container, 1, 0, FadeOutSec, () => HideMessage(message));
    }

    private void ShowMessageInPanel(MessageBubble message, bool animate = false, TimeSpan? lifetime = null)
    {
        message.LifetimeTimer.Stop();
        message.Container.BeginAnimation(OpacityProperty, null);
        message.Container.Opacity = animate ? 0 : 1;
        message.IsRemoving = false;
        message.IsDisplayed = true;

        if (message.Container.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = animate ? -12 : 0;
        }

        if (!MessagePanel.Children.Contains(message.Container))
            MessagePanel.Children.Add(message.Container);

        if (lifetime.HasValue)
            message.LifetimeTimer.Interval = lifetime.Value;
        else
            message.LifetimeTimer.Interval = TimeSpan.FromSeconds(MessageLifeSec);

        if (animate)
            AnimateMessageIn(message.Container, message.LifetimeTimer.Start);
        else if (!_replyMode)
            message.LifetimeTimer.Start();
    }

    private void RestoreRecentMessages()
    {
        var now = DateTime.UtcNow;
        var recent = _messages
            .Where(message => (now - message.ReceivedAtUtc).TotalSeconds < MessageLifeSec)
            .TakeLast(MaxVisibleMessages)
            .ToList();

        foreach (var message in recent)
        {
            var remainingSeconds = Math.Max(0.1, MessageLifeSec - (now - message.ReceivedAtUtc).TotalSeconds);
            ShowMessageInPanel(message, lifetime: TimeSpan.FromSeconds(remainingSeconds));
        }
    }

    private void HideMessage(MessageBubble message)
    {
        message.LifetimeTimer.Stop();
        message.Container.BeginAnimation(OpacityProperty, null);
        MessagePanel.Children.Remove(message.Container);
        message.IsDisplayed = false;
        message.IsRemoving = false;
    }

    private void RemoveHistoryMessage(MessageBubble message)
    {
        HideMessage(message);
        if (_selectedMessage == message)
        {
            SetSelectedVisual(message, selected: false);
            _selectedMessage = null;
        }

        _messages.Remove(message);
        UpdateHistoryCount();
    }

    private static DropShadowEffect CreateBubbleShadow()
        => new()
        {
            Color = Colors.Black,
            BlurRadius = 18,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = 0.34
        };

    private static void AnimateMessageIn(FrameworkElement element, Action onComplete)
    {
        var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        opacity.Completed += (_, _) => onComplete();
        element.BeginAnimation(OpacityProperty, opacity);

        if (element.RenderTransform is TranslateTransform transform)
        {
            var slide = new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }

    private static void AnimateFade(UIElement element, double from, double to, double seconds, Action? onComplete = null)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds));
        if (onComplete != null)
            animation.Completed += (_, _) => onComplete();
        element.BeginAnimation(OpacityProperty, animation);
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));

    private static int Clamp(int value, int min, int max)
        => Math.Min(max, Math.Max(min, value));

    private sealed class MessageBubble(
        ChatMessage message,
        string translation,
        string sourceLanguage,
        FrameworkElement container,
        Border bubble,
        DispatcherTimer lifetimeTimer)
    {
        public ChatMessage Message { get; } = message;
        public string Translation { get; } = translation;
        public string SourceLanguage { get; } = sourceLanguage;
        public FrameworkElement Container { get; } = container;
        public Border Bubble { get; } = bubble;
        public DispatcherTimer LifetimeTimer { get; } = lifetimeTimer;
        public DateTime ReceivedAtUtc { get; } = DateTime.UtcNow;
        public bool IsDisplayed { get; set; }
        public bool IsRemoving { get; set; }
    }
}

public record OverlayPosition(double Left, double Top, int Version = 1);
