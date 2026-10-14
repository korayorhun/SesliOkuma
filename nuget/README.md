# SesliOkuma.Engine

The read-aloud engine behind [Sesli Okuma](https://github.com/korayorhun/SesliOkuma), as a plain .NET Framework 4.8 class library with no UI framework dependency. MIT.

## What it does

| Type | Role |
|---|---|
| `TextCapture` | The text the user means in the foreground app: selection via UI Automation, a transparent Ctrl+C that restores the clipboard, the paragraph under the pointer, or the clipboard (`Grab`, or the steps one by one). |
| `SpeechText` / `SpeechMap` | Turns shown text into speech-ready text and keeps an offset map both ways: list bullets become pauses, `25°C` / `m²` / `€` are read as words, emoji are dropped, ALL-CAPS words are read as words, English words inside Turkish text get a Turkish pronunciation (built-in brand/term dictionary + 108k-word CMUdict-derived respelling + the user's `okunus.txt`). |
| `SpeechEngine` / `VoiceInfo` | SAPI voices (including the neural voices exposed by NaturalVoiceSAPIAdapter): list, best voice per language, speak, pause/resume, word position, WAV export. |
| `LocalTrVoice` / `EmaVoiceInstaller` | The offline Turkish neural voice (EMA Lightning by Canberk Aslan, Apache-2.0, on ONNX Runtime): on-demand download of the voice package, streaming playback with word timings. |
| `Reader` | A reading session over any voice: start at an offset, pause/resume, previous/next sentence, restart on rate change, switch voice mid-read, `CharIndex`/`WordLength` for highlighting, remaining-text fraction, completion events. Drive it with `Tick()` every ~120 ms. |
| `ScreenOcr` | Reads text off the screen with the OCR built into Windows 10/11 (per-monitor-DPI-correct capture, crop, recognize). |
| `Translator` | Free MyMemory translation (no account) or DeepL with a key; sync and async forms. |
| `AudioDuck` | Lowers other applications' audio while reading and restores it, including after a crash. |
| `Announcer` | Short spoken status cues on a separate channel (used by Sesli Okuma's accessibility mode). |
| `EngineLog` / `EnginePaths` | Where the engine logs and where it keeps per-user data. |

## Smallest example

```csharp
using SesliOkuma.Engine;

EngineLog.Sink = Console.Error.WriteLine;
var engine = new SpeechEngine();
engine.RefreshVoices();
string text = TextCapture.Grab(allowPointer: true, allowClipboard: true).Text;
var voice = engine.BestFor(TextLanguage.IsPrimary(text, "tr") ? "tr" : "en");

var reader = new Reader(engine, () => 0);          // 0 = normal rate
reader.Position += () => Console.WriteLine(reader.FullText.Substring(reader.CharIndex, reader.WordLength));
reader.Completed += finished => Console.WriteLine("done");
reader.Start(text, voice);
while (reader.Active) { Thread.Sleep(120); reader.Tick(); }
```

The full sample is in the repository under `samples/ReadAloudConsole`.

## Notes

- Target: .NET Framework 4.8, Windows 10/11, x64 process for the local voice. The library is built with the in-box C# compiler (C# 5 source) so the app needs no SDK; consumers can use any C# version.
- `SpeechText.Build(text, lang2)` applies English respelling only for Turkish voices (`lang2 == "tr"`); the user's `okunus.txt` applies to every voice.
- Screen OCR needs the Windows OCR language pack of the target language (Settings → Language); `ScreenOcr.Recognize` reports when it fell back to another language.
- Credits for the Turkish voice: [EMA Lightning](https://huggingface.co/canberkkkkkk/ema-lightning) — Canberk Aslan (Apache-2.0), [normalizer-tr](https://github.com/erdemtuna/normalizer-tr) — Erdem Tuna (Apache-2.0), ONNX Runtime — Microsoft (MIT). English respelling data derives from the [CMU Pronouncing Dictionary](https://github.com/cmusphinx/cmudict) with Turkish words excluded via the [zemberek-nlp](https://github.com/ahmetaa/zemberek-nlp) lexicon (Apache-2.0).
