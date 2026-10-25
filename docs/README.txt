PIPER STUDIO 1.2 — free text-to-speech for Windows
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
Easiest: run PiperStudio-Setup.exe. It installs for your Windows account (no
admin rights needed), adds Piper Studio to the Start menu, opens .pstudio
projects with a double-click, and can be removed in Settings → Apps.
Or use the zip:
1. Extract the whole zip to a folder (for example D:\PiperStudio).
   Do not run it from inside the zip.
2. Double-click PiperStudio.exe.
   - If Windows shows "Windows protected your PC", click "More info" → "Run anyway".
     (The app is free and open source, but not signed with a paid certificate.)
3. Choose your language. The app remembers it (change it later: Language menu).
4. Type something in the big box and press F5.

Requirements: Windows 10 or 11 (64-bit). Nothing else to install.
Piper Studio tells you when a new version is out (a yellow link at the top;
Help → "Check for updates").


INCLUDED VOICES
---------------
  fa_IR-amir-medium   Persian, male
  en_US-ryan-high     English (US), male

Choose the voice on the Voice tab. More voices (40+ languages, including more
Persian voices: gyro, ganji, reza_ibrahim): Voices menu → "Get more voices"
(Ctrl+G) — listen to a sample, then click Download. It needs Hugging Face
(in Iran maybe a VPN). You can also put .onnx + .onnx.json files in the
"voices" folder yourself, then Voices → Refresh voice list.
Optional: 28 very natural English voices from Kokoro — see section 6.


1) PLAY AND SAVE
----------------
- Type or paste text, press F5 (Play). Esc stops. Select part of the text to
  play only that part.
- Ctrl+E saves the speech as an audio file (Export tab: folder, name, format).
  WAV always works; MP3/OGG/FLAC/M4A need ffmpeg.exe (included if the package
  has it, otherwise see the Export tab).
- Each line is a paragraph. Type [pause 2] to add 2 seconds of silence.
- Tick "Also save subtitles (.srt)" on the Export tab to get subtitles for videos.
- Export tab → Loudness: −14 LUFS for YouTube / Instagram / TikTok, −16 for
  podcasts. Every file comes out equally loud, with peaks under −1 dB.
- Voice presets (top of the Voice tab): pick "Calm", "Fast chat", "Clear and
  slow"… or set things up and click "Save as…" to keep your own (it remembers
  the voice too). Ctrl+Shift+1 … 9 switch presets.

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
- Select time: drag over an empty part of the tracks (or hold Shift and drag
  anywhere). Space plays the selection; Del removes that time from ALL tracks
  (everything after moves left); Ctrl+L makes it silent; File → "Export the
  selected time…" saves just that part; Esc clears it.
- Snapping (🧲, on by default): clips jump to other clips' edges, the playhead
  and the selection when you drag them close. Hold Alt to place freely.
- Crossfades: drag a clip so it overlaps the one before it on the same track —
  the first fades out while the second fades in (an orange X shows it).
- Clean up a recording: right-click it → "Reduce background noise" (light /
  medium / strong). Best: first select a moment where only the noise is heard.
- File → "Loudness of exported mix": −14 LUFS for YouTube / Instagram, −16 for
  podcasts. "Measure the loudness of the mix" tells you where you are now.


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


6) KOKORO VOICES (optional — very natural English)
-------------------------------------------------
Kokoro is a second, free speech engine with 28 English voices (US and UK,
men and women) that sound more natural than most Piper voices.
- Voice tab → "Get Kokoro voices…". Choose:
    Smaller and faster (130 MB download) — best for talking live, or
    Best quality (350 MB download) — a bit slower on older PCs.
  It downloads once; after that it works without internet.
- Then choose a Kokoro voice in the voice lists. The best ones have a ★
  (Heart, Bella, Nicole, Emma, Michael, Fenrir, Puck).
- Kokoro speaks ENGLISH ONLY. For Persian + English text: keep amir as the main
  voice, tick "Read English words with a second voice" and choose a Kokoro voice
  as the English voice.
- Speed and volume work for Kokoro; "Expressiveness" and "Rhythm variation" only
  change Piper voices.
- Download blocked or slow? Download the .tar.bz2 file in your browser or
  download manager (the links are in the Kokoro window), then click
  "Use files I already have…" and pick the file.
- Kokoro works everywhere Piper does: Say now, quick phrases, export, the
  Tracks editor, the Telegram bot and the read-aloud shortcut.
- Already installed Kokoro yourself with Python (pip install kokoro)? Piper
  Studio finds it in C:\kokoro\.venv (or click "Use my Python Kokoro…" and pick
  its python.exe). Its voices appear as "Kokoro (Python) …". It only works on
  that PC, takes a few seconds to start, and downloads each new voice from
  Hugging Face the first time you use it.


7) WORD PREDICTION AND ACCESSIBILITY (Access tab)
------------------------------------------------
- Word prediction: while you type, up to 5 words appear above the Say now
  box. Tab or Alt+1 takes the first one, Alt+2 … Alt+5 the others, or click.
  It learns YOUR words (Persian and English) and which word you usually say
  next, from Say now, quick phrases and the bot. "Forget learned words" clears it.
- Big text and buttons; high contrast (black and yellow) — it turns on by
  itself when Windows high contrast is on.
- "F1 … F12 speak quick phrases 1–12": one key per phrase, like a
  communication board (Play is then Ctrl+Enter).
- Everything works with the keyboard: F6 / Shift+F6 jump between Say now,
  quick phrases, the text and the tabs; Tab and arrow keys move between
  buttons; the Menu key (or Shift+F10) on a phrase edits it.


8) VOICE BANKING (Tools → Voice banking)
---------------------------------------
For people who are losing their voice (for example with ALS) — or anyone who
wants to keep it: record your voice now, so a Piper voice can be made that
sounds like you.
- New voice bank → choose Persian or English (about 190 sentences each). Add
  your own sentences too — the things you say most.
- Space starts and stops recording, P listens, ← → move between sentences.
  Each take is checked: too quiet, too loud, background noise, cut off.
- Same microphone, room and distance every time; a headset mic in a quiet room
  is best. A few sentences a day is fine. 15–20 minutes of speech is the
  minimum for training; an hour or more sounds much better.
- "Export for training…" makes a zip with your recordings, metadata.csv,
  TRAINING.txt and train.sh. Training needs a strong NVIDIA graphics card or
  free Google Colab and takes hours — TRAINING.txt explains every step. The
  result is a normal Piper voice: put the .onnx and .onnx.json in the voices
  folder and it appears in the voice list.
- Keep the zip safe even if you don't train it now: it is your voice.


9) ON AN ANDROID PHONE
---------------------
Piper Studio is for Windows, but the same Persian and English Piper voices can
be your phone's voice with the free app SherpaTTS:
1. Install F-Droid (f-droid.org), then search for "SherpaTTS" and install it.
2. Open SherpaTTS → Manage languages → choose Persian (fa) → pick a voice such
   as fa_IR-amir-medium or fa_IR-gyro-medium → download (needs Hugging Face;
   in Iran maybe a VPN). English voices like en_US-ryan-high are there too.
3. Android Settings → search "Text-to-speech output" → Preferred engine:
   SherpaTTS, language: Persian.
Then apps that read aloud (TalkBack, e-book readers, some keyboards) speak
with it. Note: SherpaTTS says Google's new developer-verification rules may
stop it working on certified Android phones from 2026/2027.


SHORTCUTS
---------
F5 play · Esc stop · Ctrl+E export · Ctrl+O open text · Ctrl+S save text ·
Ctrl+B batch export · Ctrl+T Tracks editor · F12 Diagnostics ·
Enter in "Say now" speaks immediately · ↑ / ↓ earlier sentences ·
Ctrl+1 … 9 quick phrases · Ctrl+Alt+Space read selected text (any program) ·
Tab / Alt+1 … 5 word suggestions · F6 next area · Ctrl+Enter play ·
Ctrl+Shift+1 … 9 voice presets · Ctrl+G get more voices


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
