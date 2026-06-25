# CS2 Chat Translator

Real-time Counter-Strike 2 chat translation for Windows. The project includes:

- **Console app**: prints translated chat messages in a terminal.
- **WPF overlay app**: shows translated chat messages in a small always-on-top, click-through overlay.

Both apps read CS2's `console.log` and translate incoming chat messages into English by default, or any language you configure.

## Download

Download the latest Windows builds from the repository's **GitHub Releases** page:

- `CS2ChatTranslator.Console-win-x64.exe`
- `CS2ChatTranslator.Overlay-win-x64.exe`

Download the `.exe` you want and run it. The release builds are self-contained, so .NET does not need to be installed on the target machine.

## Prerequisites

- Windows
- CS2 installed via Steam
- For source builds: [.NET 10 SDK](https://dotnet.microsoft.com/download)

## CS2 Setup Required

CS2 must be launched with the `-condebug` launch option so it writes console output to a log file.

1. Steam -> Library -> Counter-Strike 2 -> right-click -> **Properties**
2. **General** tab -> **Launch Options**
3. Add:

```text
-condebug
```

The default log file path is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log
```

## Configuration

Each app can run without a config file by using the defaults below. To override them, place an `appsettings.json` file next to the executable.

```json
{
  "Translator": {
    "TargetLanguage": "en",
    "GoogleApiKey": ""
  },
  "CS2": {
    "LogPath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Counter-Strike Global Offensive\\game\\csgo\\console.log",
    "PlayerName": ""
  },
  "Overlay": {
    "Width": 640,
    "FontSize": 15.5,
    "HeaderFontSize": 13,
    "MaxMessages": 6,
    "MessageLifeSeconds": 12,
    "FadeOutSeconds": 1,
    "BackgroundOpacity": 0.84
  }
}
```

| Setting | Default | Description |
| --- | --- | --- |
| `Translator.TargetLanguage` | `en` | Target translation language, for example `en`, `de`, `fr`, `hu` |
| `Translator.GoogleApiKey` | Empty | Optional Google Translate API key |
| `CS2.LogPath` | Steam default path | Path to CS2's `console.log` |
| `CS2.PlayerName` | Empty | Optional player name to ignore your own messages |
| `Overlay.Width` | `640` | Overlay width in pixels |
| `Overlay.FontSize` | `15.5` | Main chat text size |
| `Overlay.HeaderFontSize` | `13` | Player/channel header text size |
| `Overlay.MaxMessages` | `6` | Maximum visible chat messages |
| `Overlay.MessageLifeSeconds` | `12` | Time before a message starts fading |
| `Overlay.FadeOutSeconds` | `1` | Fade-out animation duration |
| `Overlay.BackgroundOpacity` | `0.84` | Message background opacity, from `0.45` to `1.0` |

## Running The Console App

From a release:

```text
CS2ChatTranslator.Console-win-x64.exe
```

From source:

```bash
dotnet run --project CS2ChatTranslator.Console
```

The console app prints the original chat message and, when needed, the translated text below it.

## Running The WPF Overlay App

From a release:

```text
CS2ChatTranslator.Overlay-win-x64.exe
```

From source:

```bash
dotnet run --project CS2ChatTranslator.Overlay
```

The overlay is always on top and click-through during normal use, so it should not block CS2 mouse input.

Useful overlay controls:

- Press `Ctrl+Shift+D` to enter drag mode.
- In drag mode, drag the visible top bar to move the overlay.
- Press `Ctrl+Shift+D` again to leave drag mode.
- Press `Ctrl+Shift+Q` to close the overlay.
- In drag mode, click the `×` button on the top bar to close the overlay.
- The overlay position is saved to `overlay_position.json` next to the executable.

Overlay display behavior:

- Shows up to 6 recent chat messages.
- Messages fade out after about 12 seconds by default.
- Team/chat channel colors are shown in the message header.
- Messages already detected as the target language are shown without a translated line.

## Building From Source

```bash
dotnet restore CS2ChatTranslator.slnx
dotnet build CS2ChatTranslator.slnx --configuration Release
```

Publish self-contained Windows builds:

```bash
dotnet publish CS2ChatTranslator.Console/CS2ChatTranslator.Console.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish CS2ChatTranslator.Overlay/CS2ChatTranslator.Overlay.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true
```

## Notes

- Without an API key, the app uses the free Google Translate endpoint and may be rate-limited.
- With an API key, it uses the official Google Cloud Translation API.
- The app only reads CS2's log file. It does not modify CS2 files or interact with the game process.
- Only newly incoming chat messages are translated; old messages already present in the log are ignored.
- If nothing appears, check that CS2 was started with `-condebug` and that `CS2.LogPath` points to the correct `console.log`.

## Example Console Output

```text
[14:23:01] [ALL] PlayerOne: hola a todos
           -> [ES->EN] hello everyone

[14:23:15] [CT] PlayerTwo: nice shot!
           (not translated, already in the target language)

[14:23:30] [ALL] PlayerThree: gg wp
```
