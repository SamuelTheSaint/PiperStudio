# Piper Studio

**Free text-to-speech for Windows — Persian (فارسی) and English — made for people who find speaking difficult.**

Type, and Piper Studio speaks for you in a natural voice. Save speech as audio files, talk in Telegram / Discord / calls through a virtual microphone, or send voice messages from any Telegram chat with your own bot. Everything runs offline on your PC, using the open-source [Piper](https://github.com/rhasspy/piper) engine.

> **فارسی:** برنامه‌ای رایگان برای تبدیل متن به گفتار، به‌ویژه برای کسانی که صحبت کردن برایشان دشوار است. فایل `README-FA.txt` داخل بسته را ببینید.

## Download

Get the latest **`PiperStudio-v…-Windows.zip`** from the [Releases](../../releases) page, extract it, and run `PiperStudio.exe`. Windows 10/11, nothing to install.

If Windows says *"Windows protected your PC"*, click **More info → Run anyway** (the app isn't code-signed).

## Features

- Persian and English interface
- Natural voices: Persian (amir) and English (ryan) included; 40+ languages available
- **Finglish** mode — type Persian with English letters
- **Mixed Persian + English** text, each language read by its own voice
- Speed, expressiveness, pauses, volume; `[pause 2]` markers; whole-word pronunciation fixes
- Export to WAV, MP3, OGG, FLAC, M4A — one file or one per paragraph; batch convert text files; optional **subtitles (.srt)**
- **"Say now"** box: type a sentence, press Enter, it's spoken immediately (the next one waits its turn)
- **Quick phrases** in groups — one click or Ctrl+1…9 — and a **history** of everything you said (↑ / ↓ to repeat)
- **Read selected text aloud in any program** (Ctrl+Alt+Space); Ctrl+Alt+P brings the window up; optional tray mode
- **Virtual microphone** (with free VB-CABLE): speak into Telegram, Discord, calls
- **Telegram inline bot**: type `@yourbot sentence` in any chat to send it as a voice message
- **Tracks editor** (like a small Audacity): unlimited tracks with waveforms, drag & drop audio files from Windows, choose the playback device; speech clips per sentence, background music that automatically gets quieter under speech (ducking), stereo and pan, record your own voice (lined up automatically, with a one-click delay measurement); drag, trim, split, fades, volume, mute/solo, undo; save projects and export the mix with subtitles
- **Diagnostics** (F12): checks the engine, voices, FFmpeg, sound devices, microphone and Telegram, and makes a report that never contains your text or bot token
- Private by design: everything runs offline; settings live in your Windows profile (or next to the app with `portable.txt`); the bot token is encrypted with Windows

## Build from source

Run `build.bat` — it uses the C# compiler included with Windows (.NET Framework 4.x); no Visual Studio needed. Then put the Piper engine files and voices next to `PiperStudio.exe` (see the release zip for the layout).

## Credits & licenses

Piper Studio © 2026 Samuel — MIT License. Uses Piper (MIT), eSpeak NG (GPL-3.0), ONNX Runtime (MIT). Voices: amir (CC0 data), ryan (CC BY-NC-SA 4.0 data, non-commercial). See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

**Free to share — please don't sell it.**
