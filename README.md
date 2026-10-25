# Piper Studio

[![Build and test](https://github.com/SamuelTheSaint/PiperStudio/actions/workflows/build.yml/badge.svg)](https://github.com/SamuelTheSaint/PiperStudio/actions/workflows/build.yml)

**Free text-to-speech for Windows — Persian (فارسی) and English — made for people who find speaking difficult.**

Type, and Piper Studio speaks for you in a natural voice. Save speech as audio files, talk in Telegram / Discord / calls through a virtual microphone, or send voice messages from any Telegram chat with your own bot. Everything runs offline on your PC, using the open-source [Piper](https://github.com/rhasspy/piper) engine.

> **فارسی:** برنامه‌ای رایگان برای تبدیل متن به گفتار، به‌ویژه برای کسانی که صحبت کردن برایشان دشوار است. فایل `README-FA.txt` داخل بسته را ببینید.

## Download

From the [Releases](../../releases) page get **`PiperStudio-v…-Setup.exe`** (installs for your user — Start menu, uninstaller, `.pstudio` files open with a double-click; no admin rights) or the portable **`PiperStudio-v…-Windows.zip`** (extract and run `PiperStudio.exe`). Windows 10/11. The app tells you when a new version is out.

If Windows says *"Windows protected your PC"*, click **More info → Run anyway** (the app isn't code-signed).

## Features

- Persian and English interface
- Natural voices: Persian (amir) and English (ryan) included; **in-app voice browser** with samples for 40+ languages (Ctrl+G)
- Persian interface with the **Vazirmatn** font and a right-to-left layout
- Optional **Kokoro** engine: 28 very natural English voices (US/UK), one-click download inside the app; mixes with Persian Piper voices
- **Finglish** mode — type Persian with English letters
- **Mixed Persian + English** text, each language read by its own voice
- Speed, expressiveness, pauses, volume; `[pause 2]` markers; whole-word pronunciation fixes
- Export to WAV, MP3, OGG, FLAC, M4A — one file or one per paragraph; batch convert text files; optional **subtitles (.srt)**
- **"Say now"** box: type a sentence, press Enter, it's spoken immediately (the next one waits its turn)
- **Quick phrases** in groups — one click or Ctrl+1…9 — and a **history** of everything you said (↑ / ↓ to repeat)
- **Word prediction** that learns your own words and phrases, in Persian and English
- **Accessibility mode**: big text and buttons, high contrast, F1–F12 phrase keys, full keyboard control
- **Voice banking** studio: record ~190 guided sentences (or your own) in your voice, with quality checks, and export a dataset + step-by-step guide to train your own Piper voice
- **Read selected text aloud in any program** (Ctrl+Alt+Space); Ctrl+Alt+P brings the window up; optional tray mode
- **Virtual microphone** (with free VB-CABLE): speak into Telegram, Discord, calls
- **Telegram inline bot**: type `@yourbot sentence` in any chat to send it as a voice message
- **Tracks editor** (like a small Audacity): unlimited tracks with waveforms, drag & drop audio files from Windows, choose the playback device; speech clips per sentence, background music that automatically gets quieter under speech (ducking), stereo and pan, record your own voice (lined up automatically, with a one-click delay measurement); drag, trim, split, fades, volume, mute/solo, undo; save projects and export the mix with subtitles
- **For videos and podcasts**: loudness normalizing (−14 / −16 / −23 LUFS, BS.1770) with a peak limiter, noise reduction for recordings, time-range selection across all tracks (play / delete / silence / export), snapping, automatic crossfades
- **Voice presets** (Calm, Fast chat …, or your own) — Ctrl+Shift+1…9
- **Diagnostics** (F12): checks the engine, voices, FFmpeg, sound devices, microphone and Telegram, and makes a report that never contains your text or bot token
- Private by design: everything runs offline; settings live in your Windows profile (or next to the app with `portable.txt`); the bot token is encrypted with Windows

## On Android

The free [SherpaTTS](https://f-droid.org/packages/org.woheller69.ttsengine/) app (F-Droid) can download the same Piper voices — including Persian `fa_IR-amir-medium`, `gyro`, `ganji` and `reza_ibrahim` — and make them the phone's system text-to-speech voice. Steps are in the guide (section 9).

## Build from source

Run `build.bat` — it uses the C# compiler included with Windows (.NET Framework 4.x); no Visual Studio needed. Every push is built and tested on Windows by [GitHub Actions](.github/workflows/build.yml) (the tests are in `tests/`); pushing a tag like `v1.3` also builds the full package and installer (`installer/PiperStudio.nsi`, NSIS) and publishes a release. Then put the Piper engine files and voices next to `PiperStudio.exe` (see the release zip for the layout).

## Credits & licenses

Piper Studio © 2026 Samuel — MIT License. Uses Piper (MIT), eSpeak NG (GPL-3.0), ONNX Runtime (MIT), and optionally [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache-2.0) with [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) voices (Apache-2.0). Voices: amir (CC0 data), ryan (CC BY-NC-SA 4.0 data, non-commercial). See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

**Free to share — please don't sell it.**
