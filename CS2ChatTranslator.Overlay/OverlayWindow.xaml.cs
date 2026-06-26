using CS2ChatTranslator.Common;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CS2ChatTranslator.Overlay;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private const int DRAG_HOTKEY_ID = 9000;
    private const int CLOSE_HOTKEY_ID = 9001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_D = 0x44;
    private const int VK_Q = 0x51;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const string PositionFile = "overlay_position.json";

    private readonly AppSettings _settings;
    private readonly GoogleTranslator _translator;
    private readonly ChatMessageParser _parser;
    private ChatLogWatcher? _watcher;
    private HwndSource? _hwndSource;
    private bool _dragMode;

    private double OverlayWidth => Clamp(_settings.Overlay.Width, 360.0, 820.0);
    private double TextMaxWidth => Math.Max(220.0, OverlayWidth - 34.0);
    private double BodyFontSize => Clamp(_settings.Overlay.FontSize, 11.0, 24.0);
    private double HeaderFontSize => Clamp(_settings.Overlay.HeaderFontSize, 10.0, 18.0);
    private int MaxVisibleMessages => Clamp(_settings.Overlay.MaxMessages, 1, 12);
    private double MessageLifeSec => Clamp(_settings.Overlay.MessageLifeSeconds, 3.0, 30.0);
    private double FadeOutSec => Clamp(_settings.Overlay.FadeOutSeconds, 0.2, 5.0);
    private byte PanelAlpha => (byte)Math.Round(Clamp(_settings.Overlay.BackgroundOpacity, 0.45, 1.0) * 255.0);
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily MonoFont = new("SF Mono, Cascadia Mono, Consolas");

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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnableClickThrough();
        RegisterHotkey();

        _watcher = new ChatLogWatcher(_settings.CS2.LogPath, _parser, OnNewMessage);
        _watcher.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, DRAG_HOTKEY_ID);
            UnregisterHotKey(_hwndSource.Handle, CLOSE_HOTKEY_ID);
            _hwndSource.Dispose();
        }

        _watcher?.Dispose();
        _translator.Dispose();
    }

    private void RegisterHotkey()
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource.AddHook(HwndHook);
        RegisterHotKey(_hwndSource.Handle, DRAG_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_D);
        RegisterHotKey(_hwndSource.Handle, CLOSE_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_Q);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg != WM_HOTKEY)
            return IntPtr.Zero;

        var hotkeyId = wParam.ToInt32();
        if (hotkeyId == DRAG_HOTKEY_ID)
        {
            ToggleDragMode();
            handled = true;
        }
        else if (hotkeyId == CLOSE_HOTKEY_ID)
        {
            Close();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleDragMode()
    {
        _dragMode = !_dragMode;

        if (_dragMode)
        {
            DisableClickThrough();
            DragHandle.Visibility = Visibility.Visible;
            Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        }
        else
        {
            EnableClickThrough();
            DragHandle.Visibility = Visibility.Collapsed;
            Background = Brushes.Transparent;
            SavePosition();
        }
    }

    private void DragBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

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
                var pos = JsonSerializer.Deserialize<OverlayPosition>(File.ReadAllText(path));
                if (pos != null)
                {
                    Left = pos.Left;
                    Top = pos.Top;
                    return;
                }
            }
        }
        catch
        {
        }

        var screen = SystemParameters.WorkArea;
        Left = screen.Left + 18;
        Top = screen.Bottom - 260;
    }

    private void SavePosition()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, PositionFile);
            File.WriteAllText(path, JsonSerializer.Serialize(new OverlayPosition(Left, Top)));
        }
        catch
        {
        }
    }

    private async Task OnNewMessage(ChatMessage msg)
    {
        if (!string.IsNullOrEmpty(_settings.CS2.PlayerName)
            && msg.Player.Equals(_settings.CS2.PlayerName, StringComparison.OrdinalIgnoreCase))
            return;

        var result = await _translator.TranslateAsync(msg.Message, _settings.Translator.TargetLanguage);
        bool needsTranslation = ChatMessageParser.NeedsTranslation(result.SourceLanguage, _settings.Translator.TargetLanguage);
        string? translated = needsTranslation ? result.TranslatedText : null;

        Dispatcher.Invoke(() => ShowMessage(msg, translated, needsTranslation ? result.SourceLanguage : null));
    }

    private void ShowMessage(ChatMessage msg, string? translated, string? sourceLanguage)
    {
        while (MessagePanel.Children.Count >= MaxVisibleMessages)
            MessagePanel.Children.RemoveAt(0);

        var channel = GetChannelStyle(msg.Channel);
        var hasTranslation = !string.IsNullOrWhiteSpace(translated);

        var container = new Border
        {
            Background = BuildPanelBrush(),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x68, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = OverlayWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = 0,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 30,
                ShadowDepth = 0,
                Opacity = 0.52
            }
        };

        var stack = new StackPanel();
        stack.Children.Add(BuildHeader(msg, channel));
        stack.Children.Add(BuildOriginalMessage(msg, channel, hasTranslation));

        if (hasTranslation)
            stack.Children.Add(BuildTranslatedMessage(translated!, sourceLanguage));

        container.Child = stack;
        MessagePanel.Children.Add(container);

        AnimateFade(container, 0, 1, 0.18, () =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MessageLifeSec) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                AnimateFade(container, 1, 0, FadeOutSec, () =>
                    MessagePanel.Children.Remove(container));
            };
            timer.Start();
        });
    }

    private UIElement BuildHeader(ChatMessage msg, ChannelStyle channel)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(channel.Accent),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x28, channel.Accent.R, channel.Accent.G, channel.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x4A, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = msg.Channel,
                Foreground = new SolidColorBrush(channel.Accent),
                FontFamily = MonoFont,
                FontSize = HeaderFontSize,
                FontWeight = FontWeights.SemiBold
            }
        };

        var player = new TextBlock
        {
            Text = msg.Player,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontFamily = UiFont,
            FontSize = HeaderFontSize + 1.0,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = Math.Max(120.0, TextMaxWidth - 150.0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var time = new TextBlock
        {
            Text = msg.Timestamp.ToString("HH:mm:ss"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 235, 239, 246)),
            FontFamily = MonoFont,
            FontSize = HeaderFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        Grid.SetColumn(dot, 0);
        Grid.SetColumn(badge, 1);
        Grid.SetColumn(player, 2);
        Grid.SetColumn(time, 3);
        header.Children.Add(dot);
        header.Children.Add(badge);
        header.Children.Add(player);
        header.Children.Add(time);

        return header;
    }

    private UIElement BuildOriginalMessage(ChatMessage msg, ChannelStyle channel, bool hasTranslation)
    {
        return new TextBlock
        {
            Text = msg.Message,
            Foreground = hasTranslation
                ? new SolidColorBrush(Color.FromArgb(0xC8, 226, 231, 239))
                : new SolidColorBrush(channel.Message),
            FontFamily = UiFont,
            FontSize = BodyFontSize,
            FontWeight = hasTranslation ? FontWeights.Normal : FontWeights.Medium,
            LineHeight = BodyFontSize * 1.32,
            MaxWidth = TextMaxWidth,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private UIElement BuildTranslatedMessage(string translated, string? sourceLanguage)
    {
        var targetLanguage = string.IsNullOrWhiteSpace(_settings.Translator.TargetLanguage)
            ? "?"
            : _settings.Translator.TargetLanguage.ToUpperInvariant();
        var source = string.IsNullOrWhiteSpace(sourceLanguage)
            ? "?"
            : sourceLanguage.ToUpperInvariant();

        var text = new TextBlock
        {
            FontFamily = UiFont,
            FontSize = BodyFontSize + 1.0,
            FontWeight = FontWeights.SemiBold,
            LineHeight = (BodyFontSize + 1.0) * 1.32,
            MaxWidth = TextMaxWidth - 18.0,
            TextWrapping = TextWrapping.Wrap
        };

        text.Inlines.Add(new Run($"[{source}->{targetLanguage}] ")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(100, 210, 255)),
            FontFamily = MonoFont,
            FontWeight = FontWeights.SemiBold
        });
        text.Inlines.Add(new Run(translated)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(249, 252, 255))
        });

        return new Border
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(0x34, 255, 255, 255),
                Color.FromArgb(0x16, 100, 210, 255),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 9, 0, 0),
            Child = text
        };
    }

    private LinearGradientBrush BuildPanelBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(PanelAlpha, 42, 45, 54), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(PanelAlpha, 18, 20, 27), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(PanelAlpha, 12, 14, 19), 1.0));
        return brush;
    }

    private static ChannelStyle GetChannelStyle(string channel)
    {
        if (channel.StartsWith("T", StringComparison.OrdinalIgnoreCase))
        {
            return new ChannelStyle(
                Accent: Color.FromRgb(255, 214, 92),
                Message: Color.FromRgb(255, 238, 190));
        }

        if (channel.StartsWith("CT", StringComparison.OrdinalIgnoreCase))
        {
            return new ChannelStyle(
                Accent: Color.FromRgb(100, 210, 255),
                Message: Color.FromRgb(207, 240, 255));
        }

        return new ChannelStyle(
            Accent: Color.FromRgb(191, 197, 208),
            Message: Color.FromRgb(246, 248, 252));
    }

    private static void AnimateFade(UIElement el, double from, double to, double sec, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(sec));
        if (onComplete != null)
            anim.Completed += (_, _) => onComplete();
        el.BeginAnimation(OpacityProperty, anim);
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));

    private static int Clamp(int value, int min, int max)
        => Math.Min(max, Math.Max(min, value));

    private readonly record struct ChannelStyle(Color Accent, Color Message);
}

public record OverlayPosition(double Left, double Top);
