PIPER STUDIO 1.0 — free text-to-speech for Windows
===================================================

Piper Studio reads anything you type in a natural voice — in Persian and English.
It was made especially for people who find speaking difficult: you can type and
let the app speak for you, save the speech as audio files, speak into Telegram /
Discord / calls through a virtual microphone, or send voice messages with your
own Telegram bot. The Tracks editor mixes speech, background music
and your own recorded voice.

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
- Tick "Also save subtitles (.srt)" on the Export tab to get subtitles for videos.

Persian tips:
- Typing Persian with English letters (Finglish)? Tick "Finglish" on the Voice tab.
  Write aa for آ, kh for خ, sh for ش, ch for چ, gh for ق/غ.
- Persian and English in one text: on the Voice tab tick
  "Read English words with a second voice" and pick the English voice.
- A word pronounced wrongly? On the Text tab add a rule like   AI=ای آی
  Only whole words change ("AI" doesn't touch "MAIN"). A rule written in
  lowercase matches any case: pixune=پیکسیون also fixes Pixune and PIXUNE.


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


4) TRACKS EDITOR (music, recording, editing — like a small Audacity)
--------------------------------------------------------------------
Open it with the purple "Tracks editor" button (or Tools menu, Ctrl+T).

- Tracks: add as many as you like ("+ Add track" under the tracks, or + Track).
  Drag audio files from Windows straight onto the timeline: drop on a track to
  add them there, or below the tracks to put each file on a new track. Drag a
  clip below the last track to move it to a new track. ✕ deletes a track.
- "Play to" chooses the speakers/headphones used for playback.
- Speech: type text in the Speech box at the bottom and click
  "Add speech at playhead" (Ctrl+Enter). Each sentence becomes its own clip,
  using the voice chosen in the main window.
- Background music: "Import audio…" (MP3, WAV, OGG… — MP3 needs ffmpeg.exe).
  Each file gets its own track, keeps its stereo, and "ducks" automatically:
  it gets quieter while speech plays and comes back up in between (the purple
  D button; right-click the track name to choose how much).
- Your own voice: choose your microphone and press Record (R). It records onto
  a new track while the other tracks play (use headphones). Press R again to stop.
  Recordings are lined up with the music automatically. For the best timing,
  click "Measure…" once (next to "Recording delay"): it plays a few beeps and
  measures your speakers + microphone delay.
- Editing: drag a clip to move it (also to another track), drag its edges to
  trim, S splits at the red playhead, Del deletes, Ctrl+D duplicates,
  double-click for volume, fade in/out and text (change the text and the
  speech is regenerated). Right-click clips and track headers for more.
- M = mute, S = solo, D = duck, the green bar = track volume (middle line =
  100%), the blue bar = left/right pan (double-click to centre).
- Space play/pause, Home go to start, click the ruler to move the playhead,
  Ctrl + mouse wheel zoom, Ctrl+Z / Ctrl+Y undo / redo.
- Save the project (.pstudio) to continue later. "Export mix…" saves
  everything as one MP3/WAV/OGG file — plus a subtitles file (.srt) made from
  the speech clips (CC button, or File → "Export subtitles"). File menu →
  "Export each track separately" saves each track as its own WAV.


5) QUICK PHRASES, HISTORY AND SHORTCUTS (for everyday talking)
--------------------------------------------------------------
- Quick phrases (under the text box): click a phrase to say it at once, or
  press Ctrl+1 … 9. Groups like "Basics", "Chat", "Help" are at the top —
  ＋ adds a group, "＋ Add phrase" adds a phrase, right-click a phrase or
  group to edit, move or delete it, ✎ edits everything as text.
- If you type the next sentence while one is still being spoken, it waits its
  turn. Esc stops speaking and clears the queue.
- History tab: everything you said. Double-click to say it again, or save it
  as a quick phrase. In the Say now box, ↑ and ↓ go through it.
- Shortcuts tab — these work in ANY program:
    Ctrl+Alt+Space  read the selected text aloud (press again to stop).
                    Nothing selected? It reads the text on the clipboard.
    Ctrl+Alt+P      bring Piper Studio to the front, ready to type.
    Ctrl+Alt+1 … 9  speak quick phrase 1–9 — e.g. during a Telegram call
                    (turn it on first; off by default because some keyboard
                    layouts use AltGr + numbers).
- Tray: tick "Keep running in the tray when closed" to keep the shortcuts and
  the bot working after you close the window. Right-click the icon near the
  clock → Exit to quit.


SHORTCUTS
---------
F5 play · Esc stop · Ctrl+E export · Ctrl+O open text · Ctrl+S save text ·
Ctrl+B batch export · Ctrl+T Tracks editor · F12 Diagnostics ·
Enter in "Say now" speaks immediately · ↑ / ↓ earlier sentences ·
Ctrl+1 … 9 quick phrases · Ctrl+Alt+Space read selected text (any program)


PROBLEMS?
---------
- Help → Diagnostics (F12) checks everything (engine, voices, FFmpeg, sound
  devices, microphone, Telegram) and tells you what's wrong. "Copy report" gives
  you a text to send to whoever helps you — it never includes what you typed or
  your bot token.
- The Log tab shows what happened and any error message.
- Slow? "medium" voices are faster than "high" voices.
- Nothing heard via Telegram? Check the Mic tab device and Telegram's microphone.


SETTINGS AND PRIVACY
--------------------
- Settings, quick phrases and history are saved in your Windows user folder
  (%APPDATA%\PiperStudio — Help → "Open settings folder"), so Piper Studio also
  works from Program Files. Want everything in one folder (e.g. on a USB stick)?
  Create an empty file called portable.txt next to PiperStudio.exe.
- Your Telegram bot token is stored encrypted with Windows: only your Windows
  account on this PC can read it.
- History stays on your PC. Untick "Remember history after closing" on the
  History tab to keep nothing, or click "Clear history".


CREDITS AND LICENSES
--------------------
Piper Studio by Samuel — free, MIT license (see LICENSE.txt).
Speech engine: Piper (MIT) with eSpeak NG (GPL-3.0) and ONNX Runtime (MIT).
Voices: amir (data: CC0), ryan (data: CC BY-NC-SA 4.0 — non-commercial).
Details and source-code links: THIRD-PARTY-NOTICES.txt

Piper Studio is free. Please share it, but don't sell it.
