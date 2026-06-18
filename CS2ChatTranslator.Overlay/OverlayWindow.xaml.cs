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
using System.Windows.Threading;

namespace CS2ChatTranslator.Overlay;

public partial class OverlayWindow : Window
{
    // ─── Win32 ──────────────────────────────────────────────────────────────
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    // ─── Globális hotkey Win32 ───────────────────────────────────────────────
    private const int HOTKEY_ID = 9000;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_D = 0x44;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ─── Konstansok ─────────────────────────────────────────────────────────
    private const int MaxMessages = 6;
    private const double MessageLifeSec = 8.0;
    private const double FadeOutSec = 1.0;
    private const string PositionFile = "overlay_position.json";

    // ─── Mezők ──────────────────────────────────────────────────────────────
    private readonly AppSettings _settings;
    private readonly GoogleTranslator _translator;
    private readonly ChatMessageParser _parser;
    private ChatLogWatcher? _watcher;
    private HwndSource? _hwndSource;
    private bool _dragMode;

    // ─── Init ────────────────────────────────────────────────────────────────
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

        Loaded += OnLoaded;
        Closed += OnClosed;

        LoadPosition();
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
        UnregisterHotKey(_hwndSource!.Handle, HOTKEY_ID);
        _hwndSource?.Dispose();
        _watcher?.Dispose();
        _translator.Dispose();
    }

    // ─── Globális hotkey regisztráció ────────────────────────────────────────
    private void RegisterHotkey()
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource.AddHook(HwndHook);
        RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_D);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleDragMode();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ─── Drag mód toggle ─────────────────────────────────────────────────────
    private void ToggleDragMode()
    {
        _dragMode = !_dragMode;

        if (_dragMode)
        {
            DisableClickThrough();
            DragHandle.Visibility = Visibility.Visible;
            HintPanel.Visibility = Visibility.Collapsed;
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x80, 0xFF)); // kék tint = drag mód
        }
        else
        {
            EnableClickThrough();
            DragHandle.Visibility = Visibility.Collapsed;
            HintPanel.Visibility = Visibility.Collapsed;
            Background = Brushes.Transparent;
            SavePosition();
        }
    }

    // ─── Drag ────────────────────────────────────────────────────────────────
    private void DragBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    // ─── Click-through ───────────────────────────────────────────────────────
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

    // ─── Pozíció mentés/betöltés ─────────────────────────────────────────────
    private void LoadPosition()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, PositionFile);
            if (File.Exists(path))
            {
                var pos = JsonSerializer.Deserialize<OverlayPosition>(File.ReadAllText(path));
                if (pos != null) { Left = pos.Left; Top = pos.Top; return; }
            }
        }
        catch { }

        var screen = SystemParameters.WorkArea;
        Left = screen.Left + 10;
        Top = screen.Bottom - 200;
    }

    private void SavePosition()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, PositionFile);
            File.WriteAllText(path, JsonSerializer.Serialize(new OverlayPosition(Left, Top)));
        }
        catch { }
    }

    // ─── Új üzenet ───────────────────────────────────────────────────────────
    private async Task OnNewMessage(ChatMessage msg)
    {
        if (!string.IsNullOrEmpty(_settings.CS2.PlayerName)
            && msg.Player.Equals(_settings.CS2.PlayerName, StringComparison.OrdinalIgnoreCase))
            return;

        var result = await _translator.TranslateAsync(msg.Message, _settings.Translator.TargetLanguage);
        bool needsTranslation = ChatMessageParser.NeedsTranslation(result.SourceLanguage, _settings.Translator.TargetLanguage);
        string? translated = needsTranslation ? result.TranslatedText : null;

        Dispatcher.Invoke(() => ShowMessage(msg, translated));
    }

    // ─── Üzenet megjelenítés ─────────────────────────────────────────────────
    private void ShowMessage(ChatMessage msg, string? translated)
    {
        while (MessagePanel.Children.Count >= MaxMessages)
            MessagePanel.Children.RemoveAt(0);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0A, 0x0A, 0x0A)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 0, 2),
            Opacity = 0,
        };

        var stack = new StackPanel();

        string channelHex = msg.Channel switch
        {
            var c when c.StartsWith("T") => "#FFD700",
            var c when c.StartsWith("CT") => "#00BFFF",
            _ => "#AAAAAA"
        };

        var header = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        header.Inlines.Add(new System.Windows.Documents.Run($"[{msg.Channel}] ")
        { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(channelHex)) });
        header.Inlines.Add(new System.Windows.Documents.Run(msg.Player)
        { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = msg.Message,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrEmpty(translated))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"  → {translated}",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 220, 100)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        container.Child = stack;
        MessagePanel.Children.Add(container);

        AnimateFade(container, 0, 1, 0.3, () =>
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

    private static void AnimateFade(UIElement el, double from, double to, double sec, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(sec));
        if (onComplete != null)
            anim.Completed += (_, _) => onComplete();
        el.BeginAnimation(OpacityProperty, anim);
    }
}

public record OverlayPosition(double Left, double Top);
