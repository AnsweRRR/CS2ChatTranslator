# CS2 Chat Translator

A real-time CS2 chat translation console and/or WPF application. Automatically translates all incoming chat messages into Hungarian (or any other language) using Google Translate.

## Prerequisites

* [.NET 8 SDK](https://dotnet.microsoft.com/download)
* CS2 installed via Steam

## CS2 Setup (REQUIRED)

CS2 must be launched with the `-condebug` launch option so that it writes console output to a log file.

1. Steam → Library → CS2 → Right-click → **Properties**
2. **General** tab → **Launch Options**
3. Add: `-condebug`

The log file is located at:

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log
```

## Running the Application

```bash
# Build and run the project
dotnet run

# Specify a custom log file path
set CS2_LOG_PATH=D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log
dotnet run

# Use a different target language (e.g. English)
set TARGET_LANG=en
dotnet run

# Use a Google Translate API key (higher limits)
set GOOGLE_TRANSLATE_API_KEY=AIza...
dotnet run
```

## Configuration (Environment Variables)

| Variable                   | Default               | Description                      |
| -------------------------- | --------------------- | -------------------------------- |
| `CS2_LOG_PATH`             | Steam default path    | Location of the console.log file |
| `TARGET_LANG`              | `hu`                  | Target translation language      |
| `GOOGLE_TRANSLATE_API_KEY` | Empty (free endpoint) | Google API key                   |

## Notes

* **Without an API key**, the application uses the free Google Translate endpoint (~100 requests/hour limit)
* **With an API key**, it uses the official Google Cloud Translation API (free quota available, then usage-based pricing applies)
* The application is **read-only** – it does not modify anything in CS2, so there is **no VAC ban risk**
* Only newly incoming chat messages are translated; existing messages are ignored

## Example Output

```text
[14:23:01] [ALL] RussianPlayer: привет всем
           └─ [RU→HU] hello everyone

[14:23:15] [CT] GermanPlayer: nice shot!
           (not translated, already in English / detected as target language)

[14:23:30] [ALL] Player123: gg wp
```
