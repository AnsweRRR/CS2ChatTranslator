using System.Text.Json;
using System.IO;

namespace CS2ChatTranslator.Overlay;

public sealed class HistoryPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public HistoryPreferencesStore()
    {
        var dataDirectory = AppDataPaths.GetDataDirectory();
        _filePath = Path.Combine(dataDirectory, "preferences.json");
    }

    public HistoryPreferences Load(bool defaultEnabled, int defaultRetentionDays)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var saved = JsonSerializer.Deserialize<HistoryPreferences>(File.ReadAllText(_filePath));
                if (saved != null)
                    return saved.Normalize();
            }
        }
        catch
        {
        }

        return new HistoryPreferences(defaultEnabled, defaultRetentionDays).Normalize();
    }

    public void Save(HistoryPreferences preferences)
    {
        var normalized = preferences.Normalize();
        File.WriteAllText(_filePath, JsonSerializer.Serialize(normalized, JsonOptions));
    }
}

public sealed record HistoryPreferences(bool Enabled, int RetentionDays)
{
    public HistoryPreferences Normalize()
        => this with
        {
            RetentionDays = RetentionDays <= 0 ? 0 : Math.Clamp(RetentionDays, 1, 3650)
        };
}
