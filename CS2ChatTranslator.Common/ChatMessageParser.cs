using System.Text.RegularExpressions;

namespace CS2ChatTranslator.Common;

public class ChatMessageParser
{
    // Valódi CS2 log formátum:
    // 06/17 21:31:37  [ALL] Cigogre Berk‎: Good morning!
    // 06/17 21:31:37  [ALL] Cigogre Berk‎ [DEAD]: kurva
    private static readonly Regex ChatPattern = new(
        @"^\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}\s+\[(?<channel>ALL|T|CT)\]\s+(?<player>.+?)\u200e?\s*(?<dead>\[DEAD\])?\s*:\s(?<message>.+)$",
        RegexOptions.Compiled);

    public ChatMessage? TryParse(string logLine)
    {
        var match = ChatPattern.Match(logLine);
        if (!match.Success)
            return null;

        var channel = match.Groups["channel"].Value;
        if (match.Groups["dead"].Success)
            channel += " DEAD";

        return new ChatMessage(
            Player: match.Groups["player"].Value.Trim().TrimEnd('\u200e'),
            Message: match.Groups["message"].Value.Trim(),
            Channel: channel,
            Timestamp: DateTime.Now
        );
    }

    public static bool NeedsTranslation(string detectedLang, string targetLang)
    {
        return !string.IsNullOrEmpty(detectedLang)
            && detectedLang != "error"
            && !detectedLang.StartsWith(targetLang, StringComparison.OrdinalIgnoreCase);
    }
}

public record ChatMessage(string Player, string Message, string Channel, DateTime Timestamp);
