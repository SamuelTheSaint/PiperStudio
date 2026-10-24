# Piper Studio

**Free text-to-speech for Windows — Persian (فارسی) and English — made for people who find speaking difficult.**

Type, and Piper Studio speaks for you in a natural voice. Save speech as audio files, talk in Telegram / Discord / calls through a virtual microphone, or send voice messages from any Telegram chat with your own bot. Everything runs offline on your PC, using the open-source [Piper](https://github.com/rhasspy/piper) engine.

> **فارسی:** برنامه‌ای رایگان برای تبدیل متن به گفتار، به‌ویژه برای کسانی که صحبت کردن برایشان دشوار است. فایل `README-FA.txt` داخل بسته را ببینید.

## Download

Get **`PiperStudio-v1.0-Windows.zip`** from the [Releases](../../releases) page, extract it, and run `PiperStudio.exe`. Windows 10/11, nothing to install.

If Windows says *"Windows protected your PC"*, click **More info → Run anyway** (the app isn't code-signed).

## Features

- Persian and English interface
- Natural voices: Persian (amir) and English (ryan) included; 40+ languages available
- **Finglish** mode — type Persian with English letters
- **Mixed Persian + English** text, each language read by its own voice
- Speed, expressiveness, pauses, volume; `[pause 2]` markers; pronunciation fixes
- Export to WAV, MP3, OGG, FLAC, M4A — one file or one per paragraph; batch convert text files
- **"Say now"** box: type a sentence, press Enter, it's spoken immediately
- **Virtual microphone** (with free VB-CABLE): speak into Telegram, Discord, calls
- **Telegram inline bot**: type `@yourbot sentence` in any chat to send it as a voice message

## Build from source

Run `build.bat` — it uses the C# compiler included with Windows (.NET Framework 4.x); no Visual Studio needed. Then put the Piper engine files and voices next to `PiperStudio.exe` (see the release zip for the layout).

## Credits & licenses

Piper Studio © 2026 Samuel — MIT License. Uses Piper (MIT), eSpeak NG (GPL-3.0), ONNX Runtime (MIT). Voices: amir (CC0 data), ryan (CC BY-NC-SA 4.0 data, non-commercial). See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

**Free to share — please don't sell it.**
