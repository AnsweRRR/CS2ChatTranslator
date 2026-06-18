namespace CS2ChatTranslator.Common;

public class ChatLogWatcher : IDisposable
{
    private readonly string _logFilePath;
    private readonly ChatMessageParser _parser;
    private readonly Func<ChatMessage, Task> _onNewMessage;
    private readonly CancellationTokenSource _cts = new();
    private long _lastPosition;

    private const int PollIntervalMs = 500;

    public ChatLogWatcher(string logFilePath, ChatMessageParser parser, Func<ChatMessage, Task> onNewMessage)
    {
        _logFilePath = logFilePath;
        _parser = parser;
        _onNewMessage = onNewMessage;
    }

    public void Start()
    {
        if (!File.Exists(_logFilePath))
        {
            Console.WriteLine($"[!] Log fájl nem található: {_logFilePath}");
            Console.WriteLine("[!] Győződj meg róla, hogy a CS2 -condebug launch option-nel fut!");
        }
        else
        {
            // Csak az ezután érkező üzeneteket olvassuk
            _lastPosition = new FileInfo(_logFilePath).Length;
            Console.WriteLine($"[i] Log fájl megtalálva, figyelés indítása...");
        }

        Console.WriteLine($"[✓] Figyelés aktív: {_logFilePath}");

        Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct);

                if (!File.Exists(_logFilePath))
                    continue;

                // FileShare.ReadWrite: akkor is megnyitja, ha a CS2 írja
                using var fs = new FileStream(
                    _logFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (fs.Length <= _lastPosition)
                    continue;

                fs.Seek(_lastPosition, SeekOrigin.Begin);

                using var reader = new StreamReader(fs, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    var message = _parser.TryParse(line);
                    if (message != null)
                        await _onNewMessage(message);
                }

                _lastPosition = fs.Position;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Átmeneti lock – következő körben újra próbálja
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Hiba: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}