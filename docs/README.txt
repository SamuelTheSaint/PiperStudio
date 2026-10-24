PIPER STUDIO 1.0 — free text-to-speech for Windows
===================================================

Piper Studio reads anything you type in a natural voice — in Persian and English.
It was made especially for people who find speaking difficult: you can type and
let the app speak for you, save the speech as audio files, speak into Telegram /
Discord / calls through a virtual microphone, or send voice messages with your
own Telegram bot.

Everything runs on your own computer. Your text is not sent anywhere
(except to Telegram, if you use the bot feature).

(فارسی: فایل README-FA.txt را باز کنید)


GETTING STARTED
---------------
1. Extract the whole zip to a folder (for example D:\PiperStudio).
   Do not run it from inside the zip.
2. Double-click PiperStudio.exe.
   - If Windows shows "Windows protected your PC", click "More info" → "Run anyway".
     (The app is free and open source, but not signed with a paid certificate.)
3. Choose your language. The app remembers it (change it later: Language menu).
4. Type something in the big box and press F5.

Requirements: Windows 10 or 11 (64-bit). Nothing else to install.


INCLUDED VOICES
---------------
  fa_IR-amir-medium   Persian, male
  en_US-ryan-high     English (US), male

Choose the voice on the Voice tab. More voices (40+ languages):
Voices menu → "Download more voices". Put each .onnx file together with its
.onnx.json file in the "voices" folder, then Voices → Refresh voice list.


1) PLAY AND SAVE
----------------
- Type or paste text, press F5 (Play). Esc stops. Select part of the text to
  play only that part.
- Ctrl+E saves the speech as an audio file (Export tab: folder, name, format).
  WAV always works; MP3/OGG/FLAC/M4A need ffmpeg.exe (included if the package
  has it, otherwise see the Export tab).
- Each line is a paragraph. Type [pause 2] to add 2 seconds of silence.

Persian tips:
- Typing Persian with English letters (Finglish)? Tick "Finglish" on the Voice tab.
  Write aa for آ, kh for خ, sh for ش, ch for چ, gh for ق/غ.
- Persian and English in one text: on the Voice tab tick
  "Read English words with a second voice" and pick the English voice.
- A word pronounced wrongly? On the Text tab add a rule like   AI=ای آی


2) SPEAK IN TELEGRAM, DISCORD AND CALLS (virtual microphone)
-------------------------------------------------------------
One-time setup:
1. Install the free VB-CABLE: https://vb-audio.com/Cable/
   (run VBCABLE_Setup_x64.exe as administrator, then restart the PC).
2. Piper Studio → Mic tab → "Play to" = CABLE Input.
3. Telegram Desktop → Settings → Advanced → Speakers and Camera
   (or Call Settings) → Microphone / Input device = CABLE Output.

Use: start recording a voice message in Telegram (or be in a call), type your
sentence in the "Say now" box at the bottom of Piper Studio and press Enter.
Stop recording when it finishes, and send.

Want your real microphone AND Piper at the same time (no switching)?
Windows Sound settings → More sound settings → Recording → your microphone →
Properties → Listen → tick "Listen to this device", playback through
"CABLE Input". Use headphones to avoid echo.


3) TELEGRAM BOT (type "@yourbot sentence" in any chat)
------------------------------------------------------
1. In Telegram open @BotFather → send /newbot → choose a name and a username
   ending in "bot". Copy the token it gives you (keep it secret!).
2. @BotFather → /setinline → choose your bot → type a placeholder such as
   "Type to speak…".
3. Piper Studio → Bot tab → paste the token → Start bot.
4. Open your bot in Telegram and press Start. You are now its only owner.
5. In any chat type   @yourbot Hello everyone   wait a second, and tap the
   voice that appears. It is sent as a voice message.

Notes: needs ffmpeg.exe and internet access to Telegram (in Iran: a VPN; if
your VPN app only gives a proxy, enter it in the Proxy field, e.g.
http://127.0.0.1:10809). Works while Piper Studio is open on your PC — also
from Telegram on your phone. Inline text is limited to 256 characters; for
longer messages send the text to your bot and forward its voice reply.


SHORTCUTS
---------
F5 play · Esc stop · Ctrl+E export · Ctrl+O open text · Ctrl+S save text ·
Ctrl+B batch export · Enter in "Say now" speaks immediately · ↑ repeats the last one


PROBLEMS?
---------
- Look at the Log tab — it shows what happened and any error message.
- Slow? "medium" voices are faster than "high" voices.
- Nothing heard via Telegram? Check the Mic tab device and Telegram's microphone.


CREDITS AND LICENSES
--------------------
Piper Studio by Samuel — free, MIT license (see LICENSE.txt).
Speech engine: Piper (MIT) with eSpeak NG (GPL-3.0) and ONNX Runtime (MIT).
Voices: amir (data: CC0), ryan (data: CC BY-NC-SA 4.0 — non-commercial).
Details and source-code links: THIRD-PARTY-NOTICES.txt

Piper Studio is free. Please share it, but don't sell it.
