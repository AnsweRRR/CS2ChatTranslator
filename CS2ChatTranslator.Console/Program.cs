using CS2ChatTranslator.Common;
using Microsoft.Extensions.Configuration;

// ─── Konfiguráció ───────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>() ?? new AppSettings();

string logFilePath = settings.CS2.LogPath;
string? apiKey = string.IsNullOrWhiteSpace(settings.Translator.GoogleApiKey)
    ? null
    : settings.Translator.GoogleApiKey;
string targetLanguage = settings.Translator.TargetLanguage;
string? playerName = settings.CS2.PlayerName;

// ─── Inicializálás ──────────────────────────────────────────────────────────

try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
try { Console.Title = "CS2 Chat Translator"; } catch { }

PrintBanner();

SafeConsole.WriteLine($"[i] Log fájl:   {logFilePath}");
SafeConsole.WriteLine($"[i] Cél nyelv:  {targetLanguage}");
SafeConsole.WriteLine($"[i] Saját név:  {(string.IsNullOrEmpty(playerName) ? "(nincs beállítva)" : playerName)}");
SafeConsole.WriteLine($"[i] API kulcs:  {(string.IsNullOrEmpty(apiKey) ? "nincs (ingyenes endpoint)" : "beállítva")}");
SafeConsole.WriteLine();

var translator = new GoogleTranslator(apiKey);
var parser = new ChatMessageParser();
var pipeSender = new PipeSender();

// Megpróbál csatlakozni az overlay-hez (nem kötelező, konzol fallback van)
bool overlayConnected = await pipeSender.TryConnectAsync();
SafeConsole.WriteLine(overlayConnected
    ? "[✓] Overlay csatlakozva – üzenetek oda is mennek."
    : "[i] Overlay nem fut – csak konzol kimenet.");
SafeConsole.WriteLine();

using var watcher = new ChatLogWatcher(logFilePath, parser, async (msg) =>
{
    await HandleNewMessage(msg, translator, targetLanguage, playerName, pipeSender);
});

watcher.Start();

SafeConsole.WriteLine("[i] Nyomj Ctrl+C-t a kilépéshez...");
SafeConsole.WriteLine(new string('─', 60));

var tcs = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.SetResult(); };
await tcs.Task;

SafeConsole.WriteLine("\n[i] Kilépés...");
pipeSender.Dispose();

// ─── Üzenet kezelő ──────────────────────────────────────────────────────────

static async Task HandleNewMessage(
    ChatMessage msg,
    GoogleTranslator translator,
    string targetLang,
    string? playerName,
    PipeSender pipeSender)
{
    // Saját üzenetek kiszűrése
    if (!string.IsNullOrEmpty(playerName)
        && msg.Player.Equals(playerName, StringComparison.OrdinalIgnoreCase))
        return;

    // Fordítás
    var result = await translator.TranslateAsync(msg.Message, targetLang);
    bool needsTranslation = ChatMessageParser.NeedsTranslation(result.SourceLanguage, targetLang);
    string? translated = needsTranslation ? result.TranslatedText : null;

    // Konzol kimenet
    var channelColor = msg.Channel switch
    {
        "ALL" or "ALL DEAD" => ConsoleColor.Gray,
        "T" or "T DEAD" => ConsoleColor.Yellow,
        "CT" or "CT DEAD" => ConsoleColor.Cyan,
        _ => ConsoleColor.White
    };

    SafeConsole.Write($"[{msg.Timestamp:HH:mm:ss}] ", ConsoleColor.DarkGray);
    SafeConsole.Write($"[{msg.Channel}] ", channelColor);
    SafeConsole.Write($"{msg.Player}: ", ConsoleColor.White);
    SafeConsole.WriteLine(msg.Message, ConsoleColor.Gray);

    if (needsTranslation)
    {
        SafeConsole.Write("           └─ ", ConsoleColor.DarkGray);
        SafeConsole.Write($"[{result.SourceLanguage.ToUpper()}→{result.TargetLanguage.ToUpper()}] ", ConsoleColor.Green);
        SafeConsole.WriteLine(result.TranslatedText, ConsoleColor.White);
    }

    SafeConsole.ResetColor();

    // Overlay küldés
    await pipeSender.SendAsync(msg.Player, msg.Message, translated, msg.Channel);
}

static void PrintBanner()
{
    SafeConsole.WriteLine(@"
  ██████╗███████╗██████╗      ██████╗██╗  ██╗ █████╗ ████████╗
 ██╔════╝██╔════╝╚════██╗    ██╔════╝██║  ██║██╔══██╗╚══██╔══╝
 ██║     ███████╗ █████╔╝    ██║     ███████║███████║   ██║   
 ██║     ╚════██║ ╚═══██╗    ██║     ██╔══██║██╔══██║   ██║   
 ╚██████╗███████║██████╔╝    ╚██████╗██║  ██║██║  ██║   ██║   
  ╚═════╝╚══════╝╚═════╝      ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝  
              TRANSLATOR  –  fordítás a konzolban
", ConsoleColor.Yellow);
    SafeConsole.ResetColor();
}

// ─── Safe console helper ─────────────────────────────────────────────────────

static class SafeConsole
{
    public static void Write(string text, ConsoleColor? color = null)
    {
        try
        {
            if (color.HasValue) Console.ForegroundColor = color.Value;
            Console.Write(text);
        }
        catch (IOException) { }
    }

    public static void WriteLine(string text = "", ConsoleColor? color = null)
    {
        try
        {
            if (color.HasValue) Console.ForegroundColor = color.Value;
            Console.WriteLine(text);
        }
        catch (IOException) { }
    }

    public static void ResetColor()
    {
        try { Console.ResetColor(); } catch (IOException) { }
    }
}
