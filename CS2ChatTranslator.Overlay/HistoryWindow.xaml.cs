using CS2ChatTranslator.Common;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Window = System.Windows.Window;
using WindowState = System.Windows.WindowState;

namespace CS2ChatTranslator.Overlay;

public partial class HistoryWindow : Window
{
    private readonly ChatHistoryStore _historyStore;
    private readonly GoogleTranslator _translator;
    private readonly Action _openSettings;
    private readonly ObservableCollection<SessionListItem> _sessions = [];
    private readonly ObservableCollection<HistoryMessageItem> _messages = [];
    private readonly DispatcherTimer _searchTimer;
    private HistoryMessageItem? _selectedMessage;
    private bool _isLoading;
    private bool _isSending;

    public HistoryWindow(ChatHistoryStore historyStore, GoogleTranslator translator, Action openSettings)
    {
        InitializeComponent();
        _historyStore = historyStore;
        _translator = translator;
        _openSettings = openSettings;

        SessionList.ItemsSource = _sessions;
        MessageList.ItemsSource = _messages;
        ChannelFilter.ItemsSource = new[] { "All channels", "ALL", "T", "CT" };
        ChannelFilter.SelectedIndex = 0;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RefreshMessagesAsync();
        };

        Loaded += async (_, _) => await RefreshAllAsync();
    }

    public async Task RefreshAllAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        try
        {
            var selectedSessionId = (SessionList.SelectedItem as SessionListItem)?.SessionId;
            var sessions = await _historyStore.GetSessionsAsync();
            var languages = await _historyStore.GetLanguagesAsync();

            _sessions.Clear();
            _sessions.Add(new SessionListItem(
                null,
                "All Messages",
                "Complete local history",
                sessions.Sum(session => session.MessageCount)));
            foreach (var session in sessions)
            {
                _sessions.Add(new SessionListItem(
                    session.SessionId,
                    session.DisplayDate,
                    session.DisplayTime,
                    session.MessageCount));
            }

            var selectedSession = _sessions.FirstOrDefault(item => item.SessionId == selectedSessionId)
                ?? _sessions.FirstOrDefault();
            SessionList.SelectedItem = selectedSession;

            var selectedLanguage = LanguageFilter.SelectedItem as string;
            LanguageFilter.ItemsSource = new[] { "All languages" }
                .Concat(languages.Select(language => language.ToUpperInvariant()))
                .ToList();
            LanguageFilter.SelectedItem = selectedLanguage != null
                && LanguageFilter.Items.Contains(selectedLanguage)
                    ? selectedLanguage
                    : "All languages";
        }
        finally
        {
            _isLoading = false;
        }

        await RefreshMessagesAsync();
    }

    private async Task RefreshMessagesAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        try
        {
            var selectedId = _selectedMessage?.Id;
            var selectedSession = SessionList.SelectedItem as SessionListItem;
            var channel = ChannelFilter.SelectedIndex <= 0
                ? string.Empty
                : ChannelFilter.SelectedItem?.ToString() ?? string.Empty;
            var language = LanguageFilter.SelectedIndex <= 0
                ? string.Empty
                : LanguageFilter.SelectedItem?.ToString() ?? string.Empty;

            var records = await _historyStore.QueryMessagesAsync(new ChatHistoryFilter(
                selectedSession?.SessionId,
                SearchBox.Text,
                channel,
                language,
                FavoritesFilter.IsChecked == true));

            _messages.Clear();
            foreach (var record in records)
                _messages.Add(new HistoryMessageItem(record));

            EmptyState.Visibility = _messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (selectedId.HasValue)
            {
                var selected = _messages.FirstOrDefault(message => message.Id == selectedId.Value);
                if (selected != null)
                    MessageList.SelectedItem = selected;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void Filter_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender == FavoritesFilter)
            FavoritesFilter.Content = FavoritesFilter.IsChecked == true ? "\u2605" : "\u2606";

        if (IsLoaded)
            await RefreshMessagesAsync();
    }

    private async void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await RefreshMessagesAsync();
    }

    private void MessageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMessage = MessageList.SelectedItem as HistoryMessageItem;
        var hasSelection = _selectedMessage != null;
        HistoryReplyBox.IsEnabled = hasSelection;
        ReplyContextText.Text = hasSelection
            ? $"Reply to {_selectedMessage!.Player} in {_selectedMessage.SourceLanguage.ToUpperInvariant()}"
            : "Select a message to reply";
        UpdateSendButton();
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryMessageItem item })
            return;

        item.SetFavorite(!item.IsFavorite);
        await _historyStore.SetFavoriteAsync(item.Id, item.IsFavorite);
        if (FavoritesFilter.IsChecked == true)
            await RefreshMessagesAsync();
        e.Handled = true;
    }

    private async void DeleteMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryMessageItem item })
            return;

        var result = MessageBox.Show(
            "Delete this message from local history?",
            "CS2 Chat Translator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        await _historyStore.DeleteMessageAsync(item.Id);
        _messages.Remove(item);
        EmptyState.Visibility = _messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete all locally stored chat history? This action cannot be undone.",
            "CS2 Chat Translator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        await _historyStore.ClearAsync();
        _selectedMessage = null;
        await RefreshAllAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export chat history",
            FileName = $"cs2-chat-history-{DateTime.Now:yyyy-MM-dd}",
            DefaultExt = ".json",
            Filter = "JSON file (*.json)|*.json|CSV file (*.csv)|*.csv|Text file (*.txt)|*.txt"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var records = _messages.Select(message => message.Record).ToList();
        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var content = extension switch
        {
            ".csv" => BuildCsv(records),
            ".txt" => BuildText(records),
            _ => JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true })
        };

        await File.WriteAllTextAsync(dialog.FileName, content, Encoding.UTF8);
    }

    private async void HistorySendButton_Click(object sender, RoutedEventArgs e)
        => await TranslateAndCopyReplyAsync();

    private async Task TranslateAndCopyReplyAsync()
    {
        var selected = _selectedMessage;
        var reply = HistoryReplyBox.Text.Trim();
        if (_isSending || selected == null || string.IsNullOrWhiteSpace(reply))
            return;

        _isSending = true;
        HistoryReplyBox.IsEnabled = false;
        ReplyContextText.Text = "Translating reply...";
        UpdateSendButton();

        var translation = await _translator.TranslateAsync(reply, selected.SourceLanguage);
        if (translation.SourceLanguage == "error" || string.IsNullOrWhiteSpace(translation.TranslatedText))
        {
            _isSending = false;
            HistoryReplyBox.IsEnabled = true;
            ReplyContextText.Text = "Reply translation failed. Try again.";
            UpdateSendButton();
            return;
        }

        try
        {
            Clipboard.SetText(translation.TranslatedText);
        }
        catch
        {
            _isSending = false;
            HistoryReplyBox.IsEnabled = true;
            ReplyContextText.Text = "The clipboard is not available.";
            UpdateSendButton();
            return;
        }

        await _historyStore.SaveReplyAsync(selected.Id, reply, translation.TranslatedText);
        selected.SetReply(reply, translation.TranslatedText);
        HistoryReplyBox.Clear();
        ReplyContextText.Text = "Translated reply copied to clipboard";
        _isSending = false;
        HistoryReplyBox.IsEnabled = true;
        UpdateSendButton();
        HistoryReplyBox.Focus();
    }

    private void HistoryReplyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HistoryReplyPlaceholder.Visibility = string.IsNullOrEmpty(HistoryReplyBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSendButton();
    }

    private async void HistoryReplyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await TranslateAndCopyReplyAsync();
    }

    private void UpdateSendButton()
    {
        if (HistorySendButton == null || HistoryReplyBox == null)
            return;

        HistorySendButton.IsEnabled = !_isSending
            && _selectedMessage != null
            && !string.IsNullOrWhiteSpace(HistoryReplyBox.Text);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _openSettings();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private static string BuildCsv(IReadOnlyList<ChatHistoryMessage> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Timestamp,Session,Channel,Player,SourceLanguage,Original,Translated,Reply,TranslatedReply,Favorite");
        foreach (var record in records)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(record.Timestamp.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                EscapeCsv(record.SessionId),
                EscapeCsv(record.Channel),
                EscapeCsv(record.Player),
                EscapeCsv(record.SourceLanguage),
                EscapeCsv(record.OriginalText),
                EscapeCsv(record.TranslatedText),
                EscapeCsv(record.ReplyText),
                EscapeCsv(record.ReplyTranslatedText),
                record.IsFavorite ? "true" : "false"
            }));
        }

        return builder.ToString();
    }

    private static string BuildText(IReadOnlyList<ChatHistoryMessage> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records)
        {
            builder.Append('[')
                .Append(record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("] [")
                .Append(record.Channel)
                .Append("] ")
                .Append(record.Player)
                .Append(": ")
                .AppendLine(record.OriginalText);

            if (!string.IsNullOrWhiteSpace(record.TranslatedText))
                builder.Append("  -> ").AppendLine(record.TranslatedText);
            if (!string.IsNullOrWhiteSpace(record.ReplyTranslatedText))
                builder.Append("  <- ").AppendLine(record.ReplyTranslatedText);
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

public sealed record SessionListItem(
    string? SessionId,
    string PrimaryText,
    string SecondaryText,
    int MessageCount)
{
    public string CountText => MessageCount.ToString(CultureInfo.InvariantCulture);
}

public sealed class HistoryMessageItem : INotifyPropertyChanged
{
    private ChatHistoryMessage _record;

    public HistoryMessageItem(ChatHistoryMessage record)
        => _record = record;

    public long Id => _record.Id;
    public ChatHistoryMessage Record => _record;
    public string Player => _record.Player;
    public string Channel => _record.Channel;
    public string OriginalText => _record.OriginalText;
    public string? TranslatedText => _record.TranslatedText;
    public string SourceLanguage => _record.SourceLanguage;
    public string? ReplyText => _record.ReplyText;
    public string? ReplyTranslatedText => _record.ReplyTranslatedText;
    public bool IsFavorite => _record.IsFavorite;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(_record.TranslatedText);
    public bool HasReply => !string.IsNullOrWhiteSpace(_record.ReplyTranslatedText);
    public string DisplayTime => _record.Timestamp.ToLocalTime().ToString("MMM d, HH:mm");
    public string LanguageLabel => $"{_record.SourceLanguage.ToUpperInvariant()} -> {_record.TargetLanguage.ToUpperInvariant()}";
    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetFavorite(bool value)
    {
        _record = _record with { IsFavorite = value };
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteGlyph));
    }

    public void SetReply(string reply, string translatedReply)
    {
        _record = _record with
        {
            ReplyText = reply,
            ReplyTranslatedText = translatedReply
        };
        OnPropertyChanged(nameof(ReplyText));
        OnPropertyChanged(nameof(ReplyTranslatedText));
        OnPropertyChanged(nameof(HasReply));
        OnPropertyChanged(nameof(Record));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
