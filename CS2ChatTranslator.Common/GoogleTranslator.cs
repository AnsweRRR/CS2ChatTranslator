using System.Text;
using Newtonsoft.Json.Linq;

namespace CS2ChatTranslator.Common;

public class GoogleTranslator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    // Google Translate API ingyenes alternatíva (API kulcs nélkül, limit ~100 req/h)
    // Ha van API kulcsod, azt is támogatja
    private const string FreeApiUrl = "https://translate.googleapis.com/translate_a/single";
    private const string PaidApiUrl = "https://translation.googleapis.com/language/translate/v2";

    public GoogleTranslator(string? apiKey = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        _apiKey = apiKey ?? string.Empty;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage = "en")
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(text, "unknown", targetLanguage);

        try
        {
            string translated;
            string detectedLang;

            if (!string.IsNullOrEmpty(_apiKey))
            {
                (translated, detectedLang) = await TranslateWithApiKeyAsync(text, targetLanguage);
            }
            else
            {
                (translated, detectedLang) = await TranslateFreeAsync(text, targetLanguage);
            }

            return new TranslationResult(translated, detectedLang, targetLanguage);
        }
        catch (Exception ex)
        {
            return new TranslationResult($"[Fordítási hiba: {ex.Message}]", "error", targetLanguage);
        }
    }

    private async Task<(string translated, string detectedLang)> TranslateFreeAsync(string text, string targetLang)
    {
        // Ingyenes Google Translate endpoint (nem hivatalos, de működik kis forgalomnál)
        var url = $"{FreeApiUrl}?client=gtx&sl=auto&tl={targetLang}&dt=t&dt=ld&q={Uri.EscapeDataString(text)}";

        var response = await _httpClient.GetStringAsync(url);
        var json = JArray.Parse(response);

        // Lefordított szöveg összerakása (lehet több részből áll)
        var sb = new StringBuilder();
        foreach (var item in json[0])
        {
            if (item[0]?.Type == JTokenType.String)
                sb.Append(item[0]);
        }

        // Felismert nyelv
        string detectedLang = "unknown";
        try { detectedLang = json[2]?.ToString() ?? "unknown"; } catch { }

        return (sb.ToString(), detectedLang);
    }

    private async Task<(string translated, string detectedLang)> TranslateWithApiKeyAsync(string text, string targetLang)
    {
        var url = $"{PaidApiUrl}?key={_apiKey}";
        var body = new
        {
            q = text,
            target = targetLang,
            format = "text"
        };

        var content = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(body),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = JObject.Parse(await response.Content.ReadAsStringAsync());
        var translated = responseJson["data"]?["translations"]?[0]?["translatedText"]?.ToString() ?? text;
        var detectedLang = responseJson["data"]?["translations"]?[0]?["detectedSourceLanguage"]?.ToString() ?? "unknown";

        return (translated, detectedLang);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public record TranslationResult(string TranslatedText, string SourceLanguage, string TargetLanguage);
