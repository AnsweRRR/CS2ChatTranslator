//using CS2ChatTranslator;
//using CS2ChatTranslator.Common;
//using Microsoft.Extensions.Configuration;

//// ─── Konfiguráció ───────────────────────────────────────────────────────────

//var configuration = new ConfigurationBuilder()
//    .SetBasePath(AppContext.BaseDirectory)
//    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
//    .Build();

//var settings = configuration.Get<AppSettings>() ?? new AppSettings();

//string logFilePath = settings.CS2.LogPath;
//string? apiKey = string.IsNullOrWhiteSpace(settings.Translator.GoogleApiKey)
//    ? null
//    : settings.Translator.GoogleApiKey;
//string targetLanguage = settings.Translator.TargetLanguage;
//string? playerName    = settings.CS2.PlayerName;

//// ─── Inicializálás ──────────────────────────────────────────────────────────

//Console.OutputEncoding = System.Text.Encoding.UTF8;
//Console.Title = "CS2 Chat Translator";

//PrintBanner();

//Console.WriteLine($"[i] Log fájl:   {logFilePath}");
//Console.WriteLine($"[i] Cél nyelv:  {targetLanguage}");
//Console.WriteLine($"[i] Saját név:  {(string.IsNullOrEmpty(playerName) ? "(nincs beállítva)" : playerName)}");
//Console.WriteLine($"[i] API kulcs:  {(string.IsNullOrEmpty(apiKey) ? "nincs (ingyenes endpoint)" : "beállítva")}");
//Console.WriteLine();

//var translator  = new GoogleTranslator(apiKey);
//var parser      = new ChatMessageParser();
//var pipeSender  = new PipeSender();

//// Megpróbál csatlakozni az overlay-hez (nem kötelező, konzol fallback van)
//bool overlayConnected = await pipeSender.TryConnectAsync();
//Console.WriteLine(overlayConnected
//    ? "[✓] Overlay csatlakozva – üzenetek oda is mennek."
//    : "[i] Overlay nem fut – csak konzol kimenet.");
//Console.WriteLine();

//using var watcher = new ChatLogWatcher(logFilePath, parser, async (msg) =>
//{
//    await HandleNewMessage(msg, translator, targetLanguage, playerName, pipeSender);
//});

//watcher.Start();

//Console.WriteLine("[i] Nyomj Ctrl+C-t a kilépéshez...");
//Console.WriteLine(new string('─', 60));

//var tcs = new TaskCompletionSource();
//Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.SetResult(); };
//await tcs.Task;

//Console.WriteLine("\n[i] Kilépés...");
//pipeSender.Dispose();

//// ─── Üzenet kezelő ──────────────────────────────────────────────────────────

//static async Task HandleNewMessage(
//    ChatMessage msg,
//    GoogleTranslator translator,
//    string targetLang,
//    string? playerName,
//    PipeSender pipeSender
//)
//{
//    // Saját üzenetek kiszűrése
//    if (!string.IsNullOrEmpty(playerName)
//        && msg.Player.Equals(playerName, StringComparison.OrdinalIgnoreCase))
//        return;

//    // Fordítás
//    var result = await translator.TranslateAsync(msg.Message, targetLang);
//    bool needsTranslation = ChatMessageParser.NeedsTranslation(result.SourceLanguage, targetLang);
//    string? translated = needsTranslation ? result.TranslatedText : null;

//    // Konzol kimenet
//    var channelColor = msg.Channel switch
//    {
//        "ALL" or "ALL DEAD" => ConsoleColor.Gray,
//        "T"   or "T DEAD"   => ConsoleColor.Yellow,
//        "CT"  or "CT DEAD"  => ConsoleColor.Cyan,
//        _                   => ConsoleColor.White
//    };

//    Console.ForegroundColor = ConsoleColor.DarkGray;
//    Console.Write($"[{msg.Timestamp:HH:mm:ss}] ");
//    Console.ForegroundColor = channelColor;
//    Console.Write($"[{msg.Channel}] ");
//    Console.ForegroundColor = ConsoleColor.White;
//    Console.Write($"{msg.Player}: ");
//    Console.ForegroundColor = ConsoleColor.Gray;
//    Console.WriteLine(msg.Message);

//    if (needsTranslation)
//    {
//        Console.ForegroundColor = ConsoleColor.DarkGray;
//        Console.Write("           └─ ");
//        Console.ForegroundColor = ConsoleColor.Green;
//        Console.Write($"[{result.SourceLanguage.ToUpper()}→{result.TargetLanguage.ToUpper()}] ");
//        Console.ForegroundColor = ConsoleColor.White;
//        Console.WriteLine(result.TranslatedText);
//    }

//    Console.ResetColor();

//    // Overlay küldés
//    await pipeSender.SendAsync(msg.Player, msg.Message, translated, msg.Channel);
//}

//static void PrintBanner()
//{
//    Console.ForegroundColor = ConsoleColor.Yellow;
//    Console.WriteLine(@"
//  ██████╗███████╗██████╗      ██████╗██╗  ██╗ █████╗ ████████╗
// ██╔════╝██╔════╝╚════██╗    ██╔════╝██║  ██║██╔══██╗╚══██╔══╝
// ██║     ███████╗ █████╔╝    ██║     ███████║███████║   ██║   
// ██║     ╚════██║ ╚═══██╗    ██║     ██╔══██║██╔══██║   ██║   
// ╚██████╗███████║██████╔╝    ╚██████╗██║  ██║██║  ██║   ██║   
//  ╚═════╝╚══════╝╚═════╝      ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝  
//              TRANSLATOR  –  fordítás a konzolban
//");
//    Console.ResetColor();
//}
