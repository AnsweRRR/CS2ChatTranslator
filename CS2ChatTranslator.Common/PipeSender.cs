using System.IO.Pipes;
using System.Text.Json;

namespace CS2ChatTranslator.Common;

public class PipeSender : IDisposable
{
    private const string PipeName = "cs2chattranslator";
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;

    public async Task<bool> TryConnectAsync()
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(500); // 500ms timeout
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            return true;
        }
        catch
        {
            _pipe?.Dispose();
            _pipe = null;
            _writer = null;
            return false;
        }
    }

    public async Task SendAsync(string player, string original, string? translated, string channel, string? sourceLanguage = null)
    {
        if (_writer == null || !(_pipe?.IsConnected ?? false))
            return;

        try
        {
            var msg = new { Player = player, Original = original, Translated = translated, Channel = channel, SourceLanguage = sourceLanguage };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(msg));
        }
        catch
        {
            _writer = null;
            _pipe = null;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
