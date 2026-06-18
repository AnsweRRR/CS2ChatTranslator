namespace CS2ChatTranslator.Common;

public class AppSettings
{
    public TranslatorSettings Translator { get; set; } = new();
    public CS2Settings CS2 { get; set; } = new();
}

public class TranslatorSettings
{
    public string TargetLanguage { get; set; } = "hu";
    public string GoogleApiKey { get; set; } = string.Empty;
}

public class CS2Settings
{
    public string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        @"Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log");
    public string PlayerName { get; set; } = string.Empty;
}