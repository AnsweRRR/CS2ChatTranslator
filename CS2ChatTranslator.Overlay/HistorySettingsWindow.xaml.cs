using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Window = System.Windows.Window;

namespace CS2ChatTranslator.Overlay;

public partial class HistorySettingsWindow : Window
{
    private static readonly RetentionOption[] RetentionOptions =
    [
        new(7, "7 days"),
        new(30, "30 days"),
        new(90, "90 days"),
        new(365, "1 year"),
        new(0, "Unlimited")
    ];

    public HistorySettingsWindow(HistoryPreferences preferences, string databasePath)
    {
        InitializeComponent();
        Preferences = preferences;
        DatabasePathText.Text = databasePath;
        RetentionComboBox.ItemsSource = RetentionOptions;
        RetentionComboBox.DisplayMemberPath = nameof(RetentionOption.Label);
        RetentionComboBox.SelectedItem = RetentionOptions.FirstOrDefault(
            option => option.Days == preferences.RetentionDays) ?? RetentionOptions[1];
        HistoryEnabledCheckBox.IsChecked = preferences.Enabled;
        UpdateEnabledState();
    }

    public HistoryPreferences Preferences { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var retention = RetentionComboBox.SelectedItem as RetentionOption ?? RetentionOptions[1];
        Preferences = new HistoryPreferences(
            HistoryEnabledCheckBox.IsChecked == true,
            retention.Days);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void HistoryEnabled_Changed(object sender, RoutedEventArgs e)
        => UpdateEnabledState();

    private void UpdateEnabledState()
    {
        if (RetentionComboBox != null)
            RetentionComboBox.IsEnabled = HistoryEnabledCheckBox.IsChecked == true;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(DatabasePathText.Text);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}

public sealed record RetentionOption(int Days, string Label)
{
    public override string ToString() => Label;
}
