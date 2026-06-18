# CS2 Chat Translator

Valós idejű CS2 chat fordító konzol alkalmazás. Az összes beérkező chat üzenetet automatikusan lefordítja magyarra (vagy bármely más nyelvre) a Google Translate segítségével.

## Előfeltételek

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- CS2 telepítve Steam-en

## CS2 beállítás (KÖTELEZŐ)

A CS2-nek `-condebug` launch option-nel kell futnia, hogy log fájlba írja a konzolt.

1. Steam → Könyvtár → CS2 → Jobb klik → **Tulajdonságok**
2. **Általános** fül → **Indítási paraméterek**
3. Add hozzá: `-condebug`

A log fájl helye:
```
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log
```

## Futtatás

```bash
# Projekt build és futtatás
dotnet run

# Egyedi log fájl útvonal megadása
set CS2_LOG_PATH=D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log
dotnet run

# Más cél nyelv (pl. angol)
set TARGET_LANG=en
dotnet run

# Google Translate API kulccsal (magasabb limit)
set GOOGLE_TRANSLATE_API_KEY=AIza...
dotnet run
```

## Konfiguráció (környezeti változók)

| Változó | Alapértelmezett | Leírás |
|---|---|---|
| `CS2_LOG_PATH` | Steam alapértelmezett útvonal | Console.log fájl helye |
| `TARGET_LANG` | `hu` | Cél fordítási nyelv |
| `GOOGLE_TRANSLATE_API_KEY` | üres (ingyenes endpoint) | Google API kulcs |

## Megjegyzések

- **API kulcs nélkül** az ingyenes Google Translate endpoint-ot használja (~100 kérés/óra limit)
- **API kulccsal** a hivatalos Google Cloud Translation API-t használja (500 karakter ingyenes, utána fizetős)
- A program **csak olvas** – nem módosít semmit a CS2-ben, így VAC ban kockázat **nincs**
- Csak az újonnan érkező üzeneteket fordítja, a régieket nem

## Hogyan néz ki?

```
[14:23:01] [ALL] RussianPlayer: привет всем
           └─ [RU→HU] sziasztok mindenki

[14:23:15] [CT] GermanPlayer: nice shot!
           (nem fordítja, már angol/felismert célnyelv)

[14:23:30] [ALL] Player123: gg wp
```
