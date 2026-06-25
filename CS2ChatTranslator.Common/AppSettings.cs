namespace CS2ChatTranslator.Common;

public class AppSettings
{
    public TranslatorSettings Translator { get; set; } = new();
    public CS2Settings CS2 { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
}

public class TranslatorSettings
{
    public string TargetLanguage { get; set; } = "en";
    public string GoogleApiKey { get; set; } = string.Empty;
}

public class CS2Settings
{
    public string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        @"Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log");
    public string PlayerName { get; set; } = string.Empty;
}

public class OverlaySettings
{
    public double Width { get; set; } = 640.0;
    public double FontSize { get; set; } = 15.5;
    public double HeaderFontSize { get; set; } = 13.0;
    public int MaxMessages { get; set; } = 6;
    public double MessageLifeSeconds { get; set; } = 12.0;
    public double FadeOutSeconds { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.84;
}
