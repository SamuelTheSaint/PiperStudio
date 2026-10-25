using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static bool Headless;
        public const string Version = "1.1";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args);
            if (args.Length > 1 && args[0] == "--dumpstrings") { Headless = true; var f = new MainForm(); Lang.Dump(f, args[1]); var tf = new TracksForm(new TracksHost { GetJob = () => null, GetOutDir = () => "." }); Lang.Dump(tf, args[1] + ".tracks"); return 0; }
            try { if (Environment.OSVersion.Platform == PlatformID.Win32NT) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    public partial class MainForm : Form
    {
        readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        // settings live in %APPDATA%\PiperStudio (or next to the exe in portable mode) — see AppPaths
        string IniPath { get { return AppPaths.In("PiperStudio.ini"); } }
        string ReplPath { get { return AppPaths.In("replacements.txt"); } }
        string LastTextPath { get { return AppPaths.In("PiperStudio_lasttext.txt"); } }
        List<string> migratedFiles = new List<string>();
        Dictionary<string, string> ini = new Dictionary<string, string>();

        readonly PiperRunner runner = new PiperRunner();
        List<VoiceInfo> voices = new List<VoiceInfo>();
        CancellationTokenSource cts;
        SoundPlayer player;
        ComboBox cboDevice; CheckBox chkMonitor, chkTopMost; TextBox txtQuick; string lastQuick = "";
        bool busy, currentQuick, restarting, firstRun;
        string langOverride;
        TelegramBot bot;
        TextBox txtToken, txtProxy; Label lblOwner, lblBotState; Button btnBot; CheckBox chkBotAuto; long botOwner; string botOwnerName = "";

        // controls
        TextBox txt, txtPiper, txtOutDir, txtFileName, txtFfmpeg, txtRepl, txtLog;
        ComboBox cboVoice, cboSpeaker, cboVoice2, cboSpeaker2, cboDir, cboFormat, cboBitrate, cboRate;
        CheckBox chkFinglish, chkMixed, chkSplit, chkOpenAfter, chkNoOverwrite, chkNormalize, chkSrt;
        TrackBar trkSpeed, trkNoise, trkNoiseW, trkVolume;
        Label lblCount, lblFfmpeg, lblVoiceInfo, lblFinglishTip;
        NumericUpDown numSentence, numParagraph;
        Button btnPlay, btnStop, btnExport;
        ToolStripStatusLabel status;
        ToolStripProgressBar progress;
        SplitContainer split;
        GroupBox grpMixed;
        float textFontSize = 13f;

        public MainForm()
        {
            Text = "Piper Studio";
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1180, 780);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            migratedFiles = AppPaths.MigrateOldSettings(appDir, AppPaths.DataDir);
            firstRun = !File.Exists(IniPath);
            ReadIni();
            if (firstRun && !Program.Headless) ini["lang"] = AskLanguage();
            Lang.Fa = Ini("lang", "en") == "fa";
            SuspendLayout();
            BuildUi();
            Lang.Apply(this);
            ResumeLayout(true);

            runner.Log = s => AddLog(s);
            LoadSettings();
            RefreshVoices();
            ApplySettingsToUi();
            LoadPhrases();
            LoadHistory();
            SetupTray();
            if (migratedFiles.Count > 0)
            {
                SaveSettings();   // re-save right away, so the bot token is stored encrypted
                AddLog("Moved settings to " + AppPaths.DataDir + " (" + string.Join(", ", migratedFiles) + ")");
            }
            UpdateFfmpegLabel();
            UpdateCount();
            WarmUp();
            if (chkBotAuto.Checked && txtToken.Text.Trim() != "") Shown += (s, e) => ToggleBot();
            cboSpeaker.SelectedIndexChanged += (s, e) => WarmUp();
            cboVoice.SelectedIndexChanged += (s, e) => WarmUp();
            chkMixed.CheckedChanged += (s, e) => WarmUp();
            chkFinglish.CheckedChanged += (s, e) => WarmUp();
            KeyDown += OnKeyDown;
            FormClosing += (s, e) =>
            {
                if (HideToTrayInsteadOfClosing(e)) return;
                if (tracks != null && !tracks.IsDisposed) { tracks.Close(); if (!tracks.IsDisposed) { e.Cancel = true; exiting = false; return; } }
                StopAll(); if (bot != null) bot.Stop(); SaveSettings(); SaveHistory(); runner.Shutdown();
            };
            FormClosed += (s, e) => { DisposeTray(); if (restarting) try { Process.Start(Application.ExecutablePath); } catch { } };
            Shown += (s, e) =>
            {
                RegisterHotkeys();
                if (firstRun) BeginInvoke(new Action(ShowWelcome));
                try { split.SplitterDistance = Math.Max(300, split.Width - (int)(400 * DeviceDpi() / 96f)); } catch { }
                if (string.IsNullOrEmpty(runner.PiperExe))
                    SetStatus("piper.exe not found — put Piper Studio in the same folder as piper.exe, or set the path on the Voice tab.");
                else if (voices.Count == 0)
                    SetStatus("No voices found. Put .onnx + .onnx.json voice files next to piper.exe or in a 'voices' folder.");
            };
        }

        int DeviceDpi() { using (var g = CreateGraphics()) return (int)g.DpiX; }

        // ================================================================== UI
        void BuildUi()
        {
            // ----- menu
            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Open text file…", null, (s, e) => OpenText()) { ShortcutKeys = Keys.Control | Keys.O });
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Save text as…", null, (s, e) => SaveText()) { ShortcutKeys = Keys.Control | Keys.S });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Export audio…", null, (s, e) => Export()) { ShortcutKeys = Keys.Control | Keys.E });
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Batch export text files…", null, (s, e) => BatchExport()) { ShortcutKeys = Keys.Control | Keys.B });
            mFile.DropDownItems.Add(new ToolStripMenuItem("Open e&xport folder", null, (s, e) => OpenFolder(OutDir())));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var mVoices = new ToolStripMenuItem("&Voices");
            mVoices.DropDownItems.Add(new ToolStripMenuItem("&Refresh voice list", null, (s, e) => RefreshVoices()));
            mVoices.DropDownItems.Add(new ToolStripMenuItem("Open &voices folder", null, (s, e) => { var d = AppPaths.VoicesDir; Directory.CreateDirectory(d); OpenFolder(d); }));
            mVoices.DropDownItems.Add(new ToolStripMenuItem("&Download more voices (website)…", null, (s, e) => OpenUrl("https://huggingface.co/rhasspy/piper-voices/tree/main")));
            mVoices.DropDownItems.Add(new ToolStripMenuItem("&Listen to voice samples (website)…", null, (s, e) => OpenUrl("https://rhasspy.github.io/piper-samples/")));
            var mTools = new ToolStripMenuItem("&Tools");
            mTools.DropDownItems.Add(new ToolStripMenuItem("&Tracks editor (music, recording, editing)…", null, (s, e) => OpenTracks()) { ShortcutKeys = Keys.Control | Keys.T });
            var mHelp = new ToolStripMenuItem("&Help");
            mHelp.DropDownItems.Add(new ToolStripMenuItem("&Tips", null, (s, e) => ShowTips()));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("&Diagnostics (check that everything works)…", null, (s, e) => ShowDiagnostics()) { ShortcutKeys = Keys.F12 });
            mHelp.DropDownItems.Add(new ToolStripMenuItem("Open &settings folder", null, (s, e) => OpenFolder(AppPaths.DataDir)));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => MessageBox.Show(this, Lang.Fa ? Lang.T("ABOUT") :
                "Piper Studio " + Program.Version + "\nA free text-to-speech app, made especially for people who find speaking difficult.\n\nEngine: Piper (github.com/rhasspy/piper)\nMade by Samuel", Lang.T("About"), MessageBoxButtons.OK, MessageBoxIcon.Information)));
            var mLang = new ToolStripMenuItem("&Language / زبان");
            mLang.DropDownItems.Add(new ToolStripMenuItem("English", null, (s, e) => SwitchLanguage(false)) { Checked = !Lang.Fa });
            mLang.DropDownItems.Add(new ToolStripMenuItem("فارسی", null, (s, e) => SwitchLanguage(true)) { Checked = Lang.Fa });
            menu.Items.AddRange(new ToolStripItem[] { mFile, mVoices, mTools, mLang, mHelp });

            // ----- action bar
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 4), WrapContents = false };
            btnPlay = BigButton("▶  Play   (F5)", Color.FromArgb(40, 120, 70));
            btnStop = BigButton("■  Stop   (Esc)", Color.FromArgb(150, 50, 50));
            btnExport = BigButton("⬇  Export audio   (Ctrl+E)", Color.FromArgb(40, 90, 160));
            btnPlay.Click += (s, e) => Play();
            btnStop.Click += (s, e) => StopAll();
            btnExport.Click += (s, e) => Export();
            btnStop.Enabled = false;
            var btnTracks = BigButton("🎚  Tracks editor", Color.FromArgb(120, 80, 160));
            btnTracks.Click += (s, e) => OpenTracks();
            var hint = new Label { AutoSize = true, Margin = new Padding(12, 12, 3, 3), ForeColor = Color.DimGray,
                Text = "Select part of the text to play or export only that part." };
            bar.Controls.AddRange(new Control[] { btnPlay, btnStop, btnExport, btnTracks, hint });

            // ----- status bar
            var st = new StatusStrip();
            status = new ToolStripStatusLabel(Lang.T("Ready")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            progress = new ToolStripProgressBar { Size = new Size(220, 16), Visible = false };
            st.Items.Add(status); st.Items.Add(progress);

            // ----- split
            split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6 };

            // left: editor
            txt = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true, AcceptsTab = false,
                HideSelection = false, MaxLength = 0, Font = new Font("Tahoma", textFontSize), BorderStyle = BorderStyle.FixedSingle };
            txt.TextChanged += (s, e) => { UpdateCount(); AutoDirection(); };
            var edBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2, 2, 2, 4) };
            edBar.Controls.Add(SmallButton("Open…", (s, e) => OpenText()));
            edBar.Controls.Add(SmallButton("Save…", (s, e) => SaveText()));
            edBar.Controls.Add(SmallButton("Paste", (s, e) => { if (Clipboard.ContainsText()) txt.SelectedText = Clipboard.GetText(); }));
            edBar.Controls.Add(SmallButton("Clear", (s, e) => txt.Clear()));
            edBar.Controls.Add(new Label { Text = "Direction:", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
            cboDir = Combo(new[] { "Auto", "Left to right", "Right to left" }, 110);
            cboDir.SelectedIndex = 0;
            cboDir.SelectedIndexChanged += (s, e) => AutoDirection();
            edBar.Controls.Add(cboDir);
            edBar.Controls.Add(new Label { Text = "Text size:", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
            edBar.Controls.Add(SmallButton("A−", (s, e) => SetTextSize(textFontSize - 1)));
            edBar.Controls.Add(SmallButton("A+", (s, e) => SetTextSize(textFontSize + 1)));
            lblCount = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = Color.DimGray };
            edBar.Controls.Add(lblCount);
            split.Panel1.Padding = new Padding(6, 0, 0, 6);
            var quick = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 3, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtQuick = new TextBox { Dock = DockStyle.Fill, Font = new Font("Tahoma", 12f), Margin = new Padding(3, 3, 3, 3) };
            txtQuick.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickSay(); }
                else if (e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; HistoryNavigate(+1); }
                else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; HistoryNavigate(-1); }
            };
            txtQuick.GotFocus += (s, e) => WarmUp();
            txtQuick.TextChanged += (s, e) => { if (!settingQuickFromHistory) histNav = -1; };
            txtQuick.TextChanged += (s, e) => { var want = TextTools.StartsRtl(txtQuick.Text) ? RightToLeft.Yes : RightToLeft.No; if (txtQuick.RightToLeft != want) txtQuick.RightToLeft = want; };
            var lq = new Label { Text = "Say now:", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            var bq = SmallButton("Say  ▶", (s, e) => QuickSay());
            quick.Controls.Add(lq, 0, 0); quick.Controls.Add(txtQuick, 1, 0); quick.Controls.Add(bq, 2, 0);
            var qnote = new Label { Text = "Type a sentence and press Enter — it is spoken right away (to the device chosen on the Mic tab). ↑ ↓ go through what you said before.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 0) };
            quick.Controls.Add(qnote, 0, 1); quick.SetColumnSpan(qnote, 3);
            Control phead, pbuttons;
            BuildPhrasePanels(out phead, out pbuttons);
            split.Panel1.Controls.Add(txt);
            split.Panel1.Controls.Add(edBar);
            split.Panel1.Controls.Add(phead);
            split.Panel1.Controls.Add(pbuttons);
            split.Panel1.Controls.Add(quick);

            // right: tabs
            var tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
            tabs.TabPages.Add(VoiceTab());
            tabs.TabPages.Add(SoundTab());
            tabs.TabPages.Add(ExportTab());
            tabs.TabPages.Add(MicTab());
            tabs.TabPages.Add(BotTab());
            tabs.TabPages.Add(HistoryTab());
            tabs.TabPages.Add(ShortcutsTab());
            tabs.TabPages.Add(TextTab());
            tabs.TabPages.Add(LogTab());
            split.Panel2.Padding = new Padding(0, 0, 6, 6);
            split.Panel2.Controls.Add(tabs);

            Controls.Add(split);
            Controls.Add(st);
            Controls.Add(bar);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        TabPage VoiceTab()
        {
            var g = Grid();
            cboVoice = Combo(null, 0); cboVoice.SelectedIndexChanged += (s, e) => OnVoiceChanged();
            cboSpeaker = Combo(null, 0);
            AddRow(g, "Voice", cboVoice);
            AddRow(g, "Speaker", cboSpeaker);
            lblVoiceInfo = Note("");
            AddFull(g, lblVoiceInfo);

            chkFinglish = new CheckBox { Text = "Finglish: my Persian is typed with English letters", AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
            chkFinglish.CheckedChanged += (s, e) => UpdateVoiceOptions();
            AddFull(g, chkFinglish);
            lblFinglishTip = Note("e.g. \"salaam, khoobi? man emruz raftam daneshgah\". Write aa for آ, kh for خ, sh for ش, ch for چ, gh for ق/غ.");
            AddFull(g, lblFinglishTip);

            grpMixed = new GroupBox { Text = "Mixed Persian + English", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Margin = new Padding(3, 12, 3, 3) };
            var gm = Grid(); gm.Padding = new Padding(0);
            chkMixed = new CheckBox { Text = "Read English words with a second voice", AutoSize = true };
            chkMixed.CheckedChanged += (s, e) => UpdateVoiceOptions();
            AddFull(gm, chkMixed);
            AddFull(gm, Note("Persian script is read by the main voice, words in English letters by this one."));
            cboVoice2 = Combo(null, 0); cboVoice2.SelectedIndexChanged += (s, e) => FillSpeakers(cboVoice2, cboSpeaker2, null);
            cboSpeaker2 = Combo(null, 0);
            AddRow(gm, "English voice", cboVoice2);
            AddRow(gm, "Speaker", cboSpeaker2);
            grpMixed.Controls.Add(gm);
            AddFull(g, grpMixed);

            var grpEng = new GroupBox { Text = "Piper engine", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Margin = new Padding(3, 12, 3, 3) };
            var ge = Grid(); ge.Padding = new Padding(0);
            txtPiper = new TextBox();
            AddRow(ge, "piper.exe", PathPicker(txtPiper, true, "piper.exe|piper.exe|Programs|*.exe", () => { runner.PiperExe = txtPiper.Text.Trim(); RefreshVoices(); }));
            AddFull(ge, Note("Voices are loaded from the piper.exe folder, Piper Studio's folder, and a 'voices' subfolder."));
            grpEng.Controls.Add(ge);
            AddFull(g, grpEng);
            return Page("Voice", g);
        }

        TabPage SoundTab()
        {
            var g = Grid();
            trkSpeed = Slider(g, "Speed", 50, 200, 100, v => v + "%");
            trkNoise = Slider(g, "Expressiveness", 0, 100, 67, v => (v / 100.0).ToString("0.00"));
            AddFull(g, Note("Higher = more lively intonation, lower = flatter and more stable."));
            trkNoiseW = Slider(g, "Rhythm variation", 0, 100, 80, v => (v / 100.0).ToString("0.00"));
            trkVolume = Slider(g, "Volume", 20, 300, 100, v => v + "%");
            numSentence = Num(0, 5, 0.2m, 0.05m);
            AddRow(g, "Pause after sentences (s)", numSentence);
            numParagraph = Num(0, 10, 0.5m, 0.1m);
            AddRow(g, "Pause between paragraphs (s)", numParagraph);
            var reset = SmallButton("Reset to voice defaults", (s, e) => ResetSound());
            AddFull(g, reset);
            AddFull(g, Note("Tip: type [pause] or [pause 2.5] anywhere in the text to insert silence."));
            return Page("Sound", g);
        }

        TabPage ExportTab()
        {
            var g = Grid();
            txtOutDir = new TextBox();
            AddRow(g, "Save to folder", PathPicker(txtOutDir, false, null, null));
            AddFull(g, SmallButton("Open folder", (s, e) => OpenFolder(OutDir())));
            txtFileName = new TextBox();
            AddRow(g, "File name", txtFileName);
            AddFull(g, Note("Leave empty to name the file after the first words of the text."));
            cboFormat = Combo(new[] { "WAV", "MP3", "OGG", "FLAC", "M4A" }, 0);
            cboFormat.SelectedIndexChanged += (s, e) => UpdateFfmpegLabel();
            AddRow(g, "Format", cboFormat);
            cboBitrate = Combo(new[] { "96k", "128k", "192k", "256k", "320k" }, 0);
            AddRow(g, "MP3/M4A bitrate", cboBitrate);
            cboRate = Combo(new[] { "Voice default", "22050 Hz", "44100 Hz", "48000 Hz" }, 0);
            AddRow(g, "Sample rate", cboRate);
            AddFull(g, Note("48000 Hz matches most video projects."));
            chkSplit = new CheckBox { Text = "One file per paragraph (name_001, name_002 …)", AutoSize = true };
            chkNoOverwrite = new CheckBox { Text = "Never overwrite — add (2), (3) … to the name", AutoSize = true, Checked = true };
            chkOpenAfter = new CheckBox { Text = "Open the folder when export finishes", AutoSize = true, Checked = true };
            chkSrt = new CheckBox { Text = "Also save subtitles (.srt) — timed sentence by sentence", AutoSize = true };
            AddFull(g, chkSplit); AddFull(g, chkNoOverwrite); AddFull(g, chkOpenAfter); AddFull(g, chkSrt);

            var grp = new GroupBox { Text = "FFmpeg (needed for MP3 / OGG / FLAC / M4A)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Margin = new Padding(3, 12, 3, 3) };
            var gf = Grid(); gf.Padding = new Padding(0);
            txtFfmpeg = new TextBox();
            AddRow(gf, "ffmpeg.exe", PathPicker(txtFfmpeg, true, "ffmpeg.exe|ffmpeg.exe|Programs|*.exe", UpdateFfmpegLabel));
            lblFfmpeg = Note("");
            AddFull(gf, lblFfmpeg);
            grp.Controls.Add(gf);
            AddFull(g, grp);
            return Page("Export", g);
        }

        TabPage MicTab()
        {
            var g = Grid();
            AddFull(g, new Label { Text = "Speak into Telegram, Discord, etc.", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 4, 3, 4) });
            cboDevice = Combo(null, 0);
            AddRow(g, "Play to", cboDevice);
            AddFull(g, SmallButton("Refresh device list", (s, e) => RefreshDevices(DeviceName())));
            chkMonitor = new CheckBox { Text = "Also play on my speakers/headphones so I can hear it", AutoSize = true, Checked = true };
            chkTopMost = new CheckBox { Text = "Keep Piper Studio on top of other windows", AutoSize = true };
            chkTopMost.CheckedChanged += (s, e) => TopMost = chkTopMost.Checked;
            AddFull(g, chkMonitor); AddFull(g, chkTopMost);
            AddFull(g, new Label { Text = "One-time setup", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            AddFull(g, Note("1. Install the free VB-CABLE virtual audio cable (button below), then restart your PC.\n" +
                "2. Here, set \"Play to\" = CABLE Input.\n" +
                "3. In Telegram: Settings → Advanced → Call settings / Speakers and camera → Input device (microphone) = CABLE Output.\n\n" +
                "Then: hold/press Telegram's mic button, type here in \"Say now\" and press Enter, and release/stop when it finishes."));
            AddFull(g, SmallButton("Get VB-CABLE (vb-audio.com)…", (s, e) => OpenUrl("https://vb-audio.com/Cable/")));
            AddFull(g, Note("Note: while Telegram uses CABLE Output, your real microphone isn't used — switch it back when you want to talk normally."));
            return Page("Mic", g);
        }

        // ---- output devices
        List<string> deviceNames = new List<string>();

        void RefreshDevices(string keep)
        {
            cboDevice.BeginUpdate(); cboDevice.Items.Clear();
            cboDevice.Items.Add(Lang.T("Default speakers"));
            deviceNames = WaveOut.Available ? WaveOut.Devices() : new List<string>();
            foreach (var d in deviceNames) cboDevice.Items.Add(d);
            cboDevice.EndUpdate();
            int idx = 0;
            if (!string.IsNullOrEmpty(keep))
                for (int i = 0; i < deviceNames.Count; i++) if (deviceNames[i] == keep) { idx = i + 1; break; }
            cboDevice.SelectedIndex = idx;
            cboDevice.Enabled = WaveOut.Available;
        }

        string DeviceName() { return cboDevice.SelectedIndex > 0 ? deviceNames[cboDevice.SelectedIndex - 1] : ""; }

        List<int> OutputIds()
        {
            var ids = new List<int>();
            int dev = cboDevice.SelectedIndex - 1; // -1 = default
            ids.Add(dev);
            if (dev >= 0 && chkMonitor.Checked) ids.Add(-1);
            return ids;
        }

        // Load the current voice(s) into a background piper so the first sentence is quick.
        void WarmUp()
        {
            var job = MakeJob(true);
            if (job == null) return;
            Task.Run(() =>
            {
                runner.Warm(job.Main, job.MainSpeaker, job.Finglish, job.Params);
                if (job.Mixed && job.Second != null) runner.Warm(job.Second, job.SecondSpeaker, false, job.Params);
            });
        }

        void QuickSay()
        {
            string t = txtQuick.Text;
            if (t.Trim().Length == 0) return;
            lastQuick = t;
            SpeakNow(t, "say", null);
            txtQuick.Clear();
            txtQuick.Focus();
        }

        // Everything that should be spoken right away goes through here: Say now, phrases, shortcuts, history.
        // While something is still being spoken, new sentences wait their turn (Esc clears the queue).
        class SpeakRequest { public string Text, Source; public List<int> Devices; }
        readonly Queue<SpeakRequest> speakQueue = new Queue<SpeakRequest>();

        void SpeakNow(string text, string source, List<int> devices)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            AddHistory(text, source);
            AddLog((source == "phrase" ? "Phrase: " : source == "selection" ? "Read aloud: " : "Say: ") + (text.Length > 80 ? text.Substring(0, 80) + "…" : text));
            if (busy)
            {
                speakQueue.Enqueue(new SpeakRequest { Text = text, Source = source, Devices = devices });
                SetStatus(string.Format(Lang.T("Queued — {0} waiting. Esc stops."), speakQueue.Count));
                return;
            }
            PlayText(text, -1, devices);
        }

        void SpeakNextQueued()
        {
            if (busy || speakQueue.Count == 0) return;
            var r = speakQueue.Dequeue();
            PlayText(r.Text, -1, r.Devices);
        }

        TabPage BotTab()
        {
            var g = Grid();
            AddFull(g, new Label { Text = "Telegram voice bot", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 4, 3, 2) });
            AddFull(g, Note("In any Telegram chat type  @YourBot your sentence  — a voice message appears; tap it to send. Uses the voice and settings chosen in Piper Studio. Works while Piper Studio is open."));
            txtToken = new TextBox { UseSystemPasswordChar = true };
            AddRow(g, "Bot token", txtToken);
            var show = new CheckBox { Text = "Show token", AutoSize = true };
            show.CheckedChanged += (s, e) => txtToken.UseSystemPasswordChar = !show.Checked;
            AddFull(g, show);
            txtProxy = new TextBox();
            AddRow(g, "Proxy (optional)", txtProxy);
            AddFull(g, Note("Only if your VPN app gives you an HTTP proxy instead of working system-wide, e.g. http://127.0.0.1:10809 (v2rayN). Leave empty otherwise."));
            lblOwner = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            AddRow(g, "Owner", lblOwner);
            AddFull(g, SmallButton("Forget owner", (s, e) =>
            {
                if (MessageBox.Show(this, "Forget the owner? The next person who sends /start to the bot becomes its owner.", "Piper Studio", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                botOwner = 0; botOwnerName = ""; if (bot != null) bot.OwnerId = 0; UpdateOwnerLabel();
            }));
            btnBot = BigButton(Lang.T("Start bot"), Color.FromArgb(40, 140, 200));
            btnBot.Click += (s, e) => ToggleBot();
            AddFull(g, btnBot);
            lblBotState = Note("Stopped");
            AddFull(g, lblBotState);
            chkBotAuto = new CheckBox { Text = "Start the bot automatically when Piper Studio opens", AutoSize = true };
            AddFull(g, chkBotAuto);
            AddFull(g, new Label { Text = "Setup", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 12, 3, 2) });
            AddFull(g, Note("1. In Telegram open @BotFather → /newbot → copy the token here.\n2. @BotFather → /setinline → choose your bot → type a placeholder like  Type to speak…\n3. Click Start bot, then open your bot in Telegram and press Start (this makes you its only owner).\n4. Needs FFmpeg (Export tab) and your VPN on.\n\nTip: inline text can be up to 256 characters. For longer messages, send the text to the bot directly and forward its voice reply."));
            AddFull(g, SmallButton("Open @BotFather", (s, e) => OpenUrl("https://t.me/BotFather")));
            return Page("Bot", g);
        }

        static string TrBot(string m)
        {
            foreach (var k in new[] { "Running as @", "Couldn't connect: ", "Reconnecting… (" })
                if (m.StartsWith(k)) return Lang.T(k) + m.Substring(k.Length);
            return Lang.T(m);
        }

        void UpdateOwnerLabel()
        {
            lblOwner.Text = botOwner == 0 ? Lang.T("(nobody yet — press Start in your bot)") : (botOwnerName != "" ? botOwnerName + "  ·  " : "") + "id " + botOwner;
        }

        void ToggleBot()
        {
            if (bot != null && bot.Running) { bot.Stop(); btnBot.Text = Lang.T("Start bot"); lblBotState.Text = Lang.T("Stopping…"); return; }
            try
            {
                bot = new TelegramBot
                {
                    Token = txtToken.Text.Trim(), Proxy = txtProxy.Text.Trim(), OwnerId = botOwner, Runner = runner,
                    ApiBase = Ini("botapi", "https://api.telegram.org"),
                    GetJob = () => (Job)Invoke(new Func<Job>(() => MakeJob(true))),
                    GetFfmpeg = () => (string)Invoke(new Func<string>(FindFfmpeg)),
                    Log = s => AddLog(s),
                    Spoke = t => UI(() => AddHistory(t, "bot")),
                    OwnerClaimed = (id, name) => UI(() => { botOwner = id; botOwnerName = name; UpdateOwnerLabel(); SaveSettings(); }),
                    StateChanged = (on, msg) => UI(() =>
                    {
                        lblBotState.Text = TrBot(msg); lblBotState.ForeColor = on ? Color.DarkGreen : Color.DimGray;
                        btnBot.Text = on ? Lang.T("Stop bot") : Lang.T("Start bot");
                        if (!on && msg.StartsWith("Couldn't")) lblBotState.ForeColor = Color.Firebrick;
                    })
                };
                bot.Start();
                btnBot.Text = Lang.T("Stop bot"); lblBotState.Text = Lang.T("Connecting…"); lblBotState.ForeColor = Color.DimGray;
                WarmUp();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        TabPage TextTab()
        {
            var page = new TabPage("Text") { Padding = new Padding(8) };
            var top = Grid(); top.Padding = new Padding(0);
            chkNormalize = new CheckBox { Text = "Fix Arabic letters (ي → ی, ك → ک) and remove ـ", AutoSize = true, Checked = true };
            AddFull(top, chkNormalize);
            AddFull(top, new Label { Text = "Replacements — one per line, as  from=to", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 10, 3, 2) });
            AddFull(top, Note("Use it to fix pronunciation or expand abbreviations. Only whole words are changed. A rule written in lowercase matches any case (pixune → Pixune). Leave the right side empty to delete a word. Lines starting with # are ignored."));
            txtRepl = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Tahoma", 10f), AcceptsReturn = true };
            page.Controls.Add(txtRepl);
            page.Controls.Add(top);
            return page;
        }

        TabPage LogTab()
        {
            var page = new TabPage("Log") { Padding = new Padding(6) };
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = Color.White };
            var b = SmallButton("Clear log", (s, e) => txtLog.Clear()); b.Dock = DockStyle.Bottom;
            page.Controls.Add(txtLog); page.Controls.Add(b);
            return page;
        }

        // ---- small UI factories
        static TableLayoutPanel Grid()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }
        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) };
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.Controls.Add(l); t.Controls.Add(c);
        }
        static void AddFull(TableLayoutPanel t, Control c)
        {
            if (c is GroupBox) c.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            t.Controls.Add(c); t.SetColumnSpan(c, 2);
        }
        static TabPage Page(string title, Control content)
        {
            var p = new TabPage(title);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(content);
            p.Controls.Add(scroll);
            return p;
        }
        static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 6) };
        }
        static ComboBox Combo(string[] items, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            if (width > 0) c.Width = width;
            if (items != null) { c.Items.AddRange(items); if (items.Length > 0) c.SelectedIndex = 0; }
            return c;
        }
        static Button SmallButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 1, 4, 1) };
            b.Click += click; return b;
        }
        static Button BigButton(string text, Color color)
        {
            return new Button { Text = text, AutoSize = true, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(3), BackColor = color, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
        }
        static NumericUpDown Num(decimal min, decimal max, decimal val, decimal inc)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = val, Increment = inc, DecimalPlaces = 2, Width = 80, Anchor = AnchorStyles.Left };
        }
        static TrackBar Slider(TableLayoutPanel g, string name, int min, int max, int val, Func<int, string> fmt)
        {
            var t = new TrackBar { Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 10, AutoSize = false, Height = 30 };
            name = Lang.T(name);
            var l = new Label { Text = name + ":  " + fmt(val), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3), MinimumSize = new Size(150, 0) };
            t.ValueChanged += (s, e) => l.Text = name + ":  " + fmt(t.Value);
            t.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            g.Controls.Add(l); g.Controls.Add(t);
            return t;
        }
        Control PathPicker(TextBox box, bool file, string filter, Action changed)
        {
            var host = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Dock = DockStyle.Fill; box.Margin = new Padding(3, 4, 3, 3);
            var b = new Button { Text = "…", Width = 32, AutoSize = false, Height = box.Height + 2 };
            b.Click += (s, e) =>
            {
                if (file)
                {
                    using (var d = new OpenFileDialog { Filter = filter })
                    {
                        try { if (File.Exists(box.Text)) d.InitialDirectory = Path.GetDirectoryName(box.Text); } catch { }
                        if (d.ShowDialog(this) == DialogResult.OK) { box.Text = d.FileName; if (changed != null) changed(); }
                    }
                }
                else
                {
                    using (var d = new FolderBrowserDialog { SelectedPath = box.Text, ShowNewFolderButton = true })
                        if (d.ShowDialog(this) == DialogResult.OK) { box.Text = d.SelectedPath; if (changed != null) changed(); }
                }
            };
            box.Leave += (s, e) => { if (changed != null) changed(); };
            host.Controls.Add(box, 0, 0); host.Controls.Add(b, 1, 0);
            return host;
        }

        // ================================================================== voices
        IEnumerable<string> VoiceFolders()
        {
            var l = new List<string> { appDir, Path.Combine(appDir, "voices"), AppPaths.In("voices") };
            if (!string.IsNullOrEmpty(runner.PiperExe))
            {
                string pd = Path.GetDirectoryName(Path.GetFullPath(runner.PiperExe));
                l.Add(pd); l.Add(Path.Combine(pd, "voices"));
            }
            return l.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        void RefreshVoices()
        {
            string keep1 = SelectedVoice(cboVoice) != null ? SelectedVoice(cboVoice).FileName : Ini("voice", "");
            string keep2 = SelectedVoice(cboVoice2) != null ? SelectedVoice(cboVoice2).FileName : Ini("voice2", "");
            voices = VoiceInfo.Scan(VoiceFolders());
            foreach (var cb in new[] { cboVoice, cboVoice2 })
            {
                cb.BeginUpdate(); cb.Items.Clear();
                foreach (var v in voices) cb.Items.Add(v);
                cb.EndUpdate();
            }
            SelectVoice(cboVoice, keep1, null);
            SelectVoice(cboVoice2, keep2, "en");
            AddLog("Found " + voices.Count + " voice(s).");
            UpdateVoiceOptions();
        }

        void SelectVoice(ComboBox cb, string fileName, string preferLang)
        {
            for (int i = 0; i < cb.Items.Count; i++)
                if (((VoiceInfo)cb.Items[i]).FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) { cb.SelectedIndex = i; return; }
            if (preferLang != null)
                for (int i = 0; i < cb.Items.Count; i++)
                    if (((VoiceInfo)cb.Items[i]).LangFamily == preferLang) { cb.SelectedIndex = i; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        static VoiceInfo SelectedVoice(ComboBox cb) { return cb == null ? null : cb.SelectedItem as VoiceInfo; }

        void OnVoiceChanged()
        {
            FillSpeakers(cboVoice, cboSpeaker, null);
            UpdateVoiceOptions();
        }

        void FillSpeakers(ComboBox voiceBox, ComboBox spkBox, int? select)
        {
            var v = SelectedVoice(voiceBox);
            int keep = select ?? (spkBox.SelectedItem is SpeakerItem ? ((SpeakerItem)spkBox.SelectedItem).Id : 0);
            spkBox.BeginUpdate(); spkBox.Items.Clear();
            if (v != null && v.NumSpeakers > 1)
                foreach (var kv in v.Speakers) spkBox.Items.Add(new SpeakerItem { Id = kv.Value, Name = kv.Key });
            else spkBox.Items.Add(new SpeakerItem { Id = 0, Name = Lang.T("(single speaker)") });
            spkBox.EndUpdate();
            spkBox.SelectedIndex = 0;
            for (int i = 0; i < spkBox.Items.Count; i++) if (((SpeakerItem)spkBox.Items[i]).Id == keep) { spkBox.SelectedIndex = i; break; }
            spkBox.Enabled = v != null && v.NumSpeakers > 1;
        }

        class SpeakerItem
        {
            public int Id; public string Name;
            public override string ToString() { return Name.StartsWith("(") ? Name : "#" + Id + "  " + Name; }
        }

        void UpdateVoiceOptions()
        {
            var v = SelectedVoice(cboVoice);
            bool fa = v != null && v.IsPersian;
            chkFinglish.Enabled = fa;
            lblFinglishTip.Visible = fa;
            grpMixed.Enabled = !(fa && chkFinglish.Checked);
            cboVoice2.Enabled = cboSpeaker2.Enabled = chkMixed.Checked;
            if (chkMixed.Checked) FillSpeakers(cboVoice2, cboSpeaker2, null);
            if (v != null)
                lblVoiceInfo.Text = string.Format("File: {0}.onnx   ·   {1} Hz   ·   language: {2}", v.FileName, v.SampleRate, v.LangCode);
            else lblVoiceInfo.Text = "No voice selected.";
        }

        // ================================================================== job / playback
        Job MakeJob(bool quiet)
        {
            var v = SelectedVoice(cboVoice);
            if (string.IsNullOrEmpty(runner.PiperExe) || !File.Exists(runner.PiperExe))
            {
                if (!quiet) MessageBox.Show(this, Lang.T("piper.exe was not found.\n\nSet its location on the Voice tab (Piper engine)."), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (v == null)
            {
                if (!quiet) MessageBox.Show(this, Lang.T("No voice is selected.\n\nPut a voice (.onnx and .onnx.json) next to piper.exe or in the 'voices' folder, then use Voices → Refresh."), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            var job = new Job
            {
                Main = v,
                MainSpeaker = cboSpeaker.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker.SelectedItem).Id : 0,
                Finglish = v.IsPersian && chkFinglish.Checked,
                Mixed = chkMixed.Checked && grpMixed.Enabled,
                Second = SelectedVoice(cboVoice2),
                SecondSpeaker = cboSpeaker2.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker2.SelectedItem).Id : 0,
                Normalize = chkNormalize.Checked,
                ParagraphPause = (double)numParagraph.Value,
                Gain = trkVolume.Value / 100.0,
                Replacements = TextTools.ParseReplacements(txtRepl.Text)
            };
            job.Params.LengthScale = 100.0 / trkSpeed.Value * v.LengthScale;
            job.Params.NoiseScale = trkNoise.Value / 100.0;
            job.Params.NoiseW = trkNoiseW.Value / 100.0;
            job.Params.SentenceSilence = (double)numSentence.Value;
            if (job.Mixed && job.Second == null) job.Mixed = false;
            return job;
        }

        void GetTarget(out string text, out int offset)
        {
            if (txt.SelectionLength > 0) { text = txt.SelectedText; offset = txt.SelectionStart; }
            else { text = txt.Text; offset = 0; }
        }

        void SetBusy(bool b, string what)
        {
            busy = b;
            btnPlay.Enabled = btnExport.Enabled = !b;
            btnStop.Enabled = b;
            txt.ReadOnly = b;
            progress.Visible = b;
            if (!b) progress.Value = 0;
            if (what != null) SetStatus(what);
            if (!b && speakQueue.Count > 0) BeginInvoke(new Action(SpeakNextQueued));
        }

        void Play()
        {
            if (busy) return;
            string text; int offset; GetTarget(out text, out offset);
            if (text.Trim().Length == 0) { SetStatus(Lang.T("Nothing to read — type or paste some text first.")); return; }
            PlayText(text, offset);
        }

        // offset < 0 = text is not from the editor (no highlighting)
        bool PlayText(string text, int offset, List<int> devices = null)
        {
            if (busy) return false;
            bool highlight = offset >= 0;
            var job = MakeJob(false); if (job == null) return false;
            var chunks = highlight ? TextTools.MakeChunks(text, offset) : TextTools.MakeSentences(text);
            currentQuick = !highlight;
            var outIds = devices ?? OutputIds();
            bool useWaveOut = WaveOut.Available;
            int selStart = txt.SelectionStart, selLen = txt.SelectionLength;
            cts = new CancellationTokenSource(); var ct = cts.Token;
            SetBusy(true, Lang.T("Generating…"));
            progress.Maximum = chunks.Count; progress.Value = 0;
            var queue = new BlockingCollection<KeyValuePair<Chunk, short[]>>(2);

            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < chunks.Count && !ct.IsCancellationRequested; i++)
                    {
                        var pcm = Synth.Chunk(runner, chunks[i].Text, job, highlight && i < chunks.Count - 1, ct);
                        if (pcm.Length > 0) queue.Add(new KeyValuePair<Chunk, short[]>(chunks[i], pcm), ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { UI(() => ShowError(ex)); }
                finally { queue.CompleteAdding(); }
            });

            var th = new Thread(() =>
            {
                int n = 0;
                try
                {
                    foreach (var item in queue.GetConsumingEnumerable(ct))
                    {
                        var c = item.Key; n++;
                        int nn = n;
                        UI(() =>
                        {
                            if (highlight) try { txt.Select(c.Start, c.Length); txt.ScrollToCaret(); } catch { }
                            progress.Value = Math.Min(progress.Maximum, nn);
                            SetStatus(string.Format(Lang.T("Playing paragraph {0} of {1}…"), nn, chunks.Count));
                        });
                        if (useWaveOut)
                            WaveOut.PlaySync(Audio.Resample(item.Value, job.Rate, 48000), 48000, outIds, ct);
                        else
                        {
                            // no Windows audio (test machines): just take as long as the sound would
                            var until = DateTime.Now.AddSeconds(item.Value.Length / (double)job.Rate);
                            while (DateTime.Now < until && !ct.IsCancellationRequested) Thread.Sleep(10);
                        }
                        if (ct.IsCancellationRequested) break;
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { UI(() => ShowError(ex)); }
                UI(() =>
                {
                    bool stopped = ct.IsCancellationRequested;
                    SetBusy(false, stopped ? Lang.T("Stopped.") : Lang.T("Finished playing."));
                    if (highlight) try { txt.Select(selStart, selLen); } catch { }
                });
            }) { IsBackground = true };
            th.Start();
            return true;
        }

        void StopAll()
        {
            speakQueue.Clear();
            if (cts != null) cts.Cancel();
            if (!currentQuick) runner.Kill(); // "Say now" sentences are short: let them finish so piper stays loaded
            var p = player; if (p != null) try { p.Stop(); } catch { }
        }

        // ================================================================== export
        string OutDir()
        {
            string d = txtOutDir.Text.Trim();
            if (d == "") d = AppPaths.DefaultExportDir;
            return d;
        }

        string FormatExt() { return "." + cboFormat.SelectedItem.ToString().ToLowerInvariant(); }

        int TargetRate(int voiceRate)
        {
            switch (cboRate.SelectedIndex) { case 1: return 22050; case 2: return 44100; case 3: return 48000; default: return voiceRate; }
        }

        static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.');
            return s;
        }

        static string AutoName(string text)
        {
            text = TextTools.PauseRx.Replace(text, " ");
            var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Take(6);
            string n = SafeName(string.Join(" ", words));
            if (n.Length > 50) n = n.Substring(0, 50).Trim();
            return n.Length == 0 ? "speech" : n;
        }

        string UniquePath(string folder, string name, string ext, bool noOverwrite)
        {
            string p = Path.Combine(folder, name + ext);
            if (!noOverwrite) return p;
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(folder, name + " (" + i + ")" + ext);
            return p;
        }

        class ExportOptions { public string Folder, Ext, Ffmpeg, Bitrate; public bool Split, NoOverwrite, Srt; public int RateChoice; }

        ExportOptions GetExportOptions(bool quiet)
        {
            var o = new ExportOptions
            {
                Folder = OutDir(), Ext = FormatExt(), Ffmpeg = FindFfmpeg(), Bitrate = cboBitrate.SelectedItem.ToString(),
                Split = chkSplit.Checked, NoOverwrite = chkNoOverwrite.Checked, Srt = chkSrt.Checked, RateChoice = cboRate.SelectedIndex
            };
            if (o.Ext != ".wav" && o.Ffmpeg == null)
            {
                if (!quiet) MessageBox.Show(this, cboFormat.SelectedItem + Lang.T(" export needs FFmpeg, which wasn't found.\n\nSet the path to ffmpeg.exe on the Export tab, or choose WAV."), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            try { Directory.CreateDirectory(o.Folder); }
            catch (Exception ex) { MessageBox.Show(this, "Can't use the export folder:\n" + ex.Message, "Piper Studio"); return null; }
            return o;
        }

        void Export()
        {
            if (busy) return;
            string text; int offset; GetTarget(out text, out offset);
            if (text.Trim().Length == 0) { SetStatus(Lang.T("Nothing to export — type or paste some text first.")); return; }
            var job = MakeJob(false); if (job == null) return;
            var opt = GetExportOptions(false); if (opt == null) return;
            string name = SafeName(txtFileName.Text);
            if (name == "") name = AutoName(text);
            bool openAfter = chkOpenAfter.Checked;
            RunExport(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(name, text) }, job, opt, openAfter);
        }

        void BatchExport()
        {
            if (busy) return;
            var job = MakeJob(false); if (job == null) return;
            var opt = GetExportOptions(false); if (opt == null) return;
            using (var d = new OpenFileDialog { Filter = "Text files|*.txt|All files|*.*", Multiselect = true, Title = "Choose text files — each becomes one audio file" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var items = d.FileNames.Select(f => new KeyValuePair<string, string>(Path.GetFileNameWithoutExtension(f), ReadTextFile(f))).ToList();
                RunExport(items, job, opt, chkOpenAfter.Checked);
            }
        }

        void RunExport(List<KeyValuePair<string, string>> items, Job job, ExportOptions opt, bool openAfter)
        {
            var allChunks = items.Select(it => TextTools.MakeChunks(it.Value, 0)).ToList();
            int total = allChunks.Sum(c => c.Count);
            cts = new CancellationTokenSource(); var ct = cts.Token;
            SetBusy(true, Lang.T("Exporting…"));
            progress.Maximum = Math.Max(1, total); progress.Value = 0;
            var sw = Stopwatch.StartNew();
            Task.Run(() =>
            {
                var written = new List<string>();
                int done = 0, srtFiles = 0;
                try
                {
                    for (int f = 0; f < items.Count; f++)
                    {
                        var chunks = allChunks[f];
                        var pieces = new List<short[]>();
                        var pieceCues = new List<List<Subtitles.Cue>>();
                        double rate = job.Rate;
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            int fi = f, ci = i;
                            UI(() => SetStatus(string.Format(Lang.T("Exporting{0}: paragraph {1} of {2}…"), items.Count > 1 ? " file " + (fi + 1) + "/" + items.Count : "", ci + 1, chunks.Count)));
                            bool pause = !opt.Split && i < chunks.Count - 1;
                            if (opt.Srt)
                            {
                                // sentence by sentence, so every subtitle line gets its exact time
                                var parts = new List<short[]>();
                                var cues = new List<Subtitles.Cue>();
                                long at = 0;
                                foreach (var sn in TextTools.MakeSentences(chunks[i].Text))
                                {
                                    var pcm = Synth.Chunk(runner, sn.Text, job, false, ct);
                                    if (pcm.Length == 0) continue;
                                    cues.Add(new Subtitles.Cue { Start = at / rate, End = (at + pcm.Length) / rate, Text = sn.Text });
                                    parts.Add(pcm); at += pcm.Length;
                                }
                                if (pause) parts.Add(Audio.Silence(job.ParagraphPause, job.Rate));
                                pieces.Add(Audio.Concat(parts));
                                pieceCues.Add(cues);
                            }
                            else pieces.Add(Synth.Chunk(runner, chunks[i].Text, job, pause, ct));
                            done++; int dd = done;
                            UI(() => progress.Value = Math.Min(progress.Maximum, dd));
                        }
                        if (opt.Split)
                            for (int i = 0; i < pieces.Count; i++)
                            {
                                string path = WriteAudio(pieces[i], job.Rate, opt, items[f].Key + "_" + (i + 1).ToString("000"));
                                written.Add(path);
                                if (opt.Srt && pieceCues[i].Count > 0) { Subtitles.Write(Path.ChangeExtension(path, ".srt"), Subtitles.MakeCues(pieceCues[i])); srtFiles++; }
                            }
                        else
                        {
                            string path = WriteAudio(Audio.Concat(pieces), job.Rate, opt, items[f].Key);
                            written.Add(path);
                            if (opt.Srt)
                            {
                                var all = new List<Subtitles.Cue>();
                                double offset = 0;
                                for (int i = 0; i < pieces.Count; i++)
                                {
                                    foreach (var c in pieceCues[i]) all.Add(new Subtitles.Cue { Start = c.Start + offset, End = c.End + offset, Text = c.Text });
                                    offset += pieces[i].Length / rate;
                                }
                                if (all.Count > 0) { Subtitles.Write(Path.ChangeExtension(path, ".srt"), Subtitles.MakeCues(all)); srtFiles++; }
                            }
                        }
                    }
                    UI(() =>
                    {
                        SetBusy(false, string.Format(Lang.T("Exported {0} file(s) in {1:0.0}s → {2}"), written.Count, sw.Elapsed.TotalSeconds, written.Count == 1 ? written[0] : opt.Folder) + (srtFiles > 0 ? "  " + Lang.T("(+ subtitles)") : ""));
                        foreach (var w in written) AddLog("Saved: " + w);
                        if (openAfter && written.Count > 0) SelectInExplorer(written[0], written.Count == 1);
                    });
                }
                catch (OperationCanceledException) { UI(() => SetBusy(false, Lang.T("Export cancelled."))); }
                catch (Exception ex) { UI(() => { SetBusy(false, Lang.T("Export failed.")); ShowError(ex); }); }
            });
        }

        string WriteAudio(short[] pcm, int voiceRate, ExportOptions opt, string name)
        {
            string path = UniquePath(opt.Folder, name, opt.Ext, opt.NoOverwrite);
            int rate = opt.RateChoice == 0 ? voiceRate : new[] { voiceRate, 22050, 44100, 48000 }[opt.RateChoice];
            if (opt.Ext == ".wav")
            {
                Audio.WriteWav(path, Audio.Resample(pcm, voiceRate, rate), rate);
                return path;
            }
            string tmp = Path.Combine(Path.GetTempPath(), "piperstudio_" + Guid.NewGuid().ToString("N") + ".wav");
            Audio.WriteWav(tmp, pcm, voiceRate);
            try
            {
                string codec;
                switch (opt.Ext)
                {
                    case ".mp3": codec = "-c:a libmp3lame -b:a " + opt.Bitrate; break;
                    case ".ogg": codec = "-c:a libvorbis -q:a 6"; break;
                    case ".flac": codec = "-c:a flac"; break;
                    default: codec = "-c:a aac -b:a " + opt.Bitrate; break;
                }
                string args = string.Format("-y -hide_banner -loglevel error -i \"{0}\" -ar {1} -ac 1 {2} \"{3}\"", tmp, rate, codec, path);
                var psi = new ProcessStartInfo(opt.Ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new Exception("FFmpeg failed:\n" + err);
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
            return path;
        }

        // ================================================================== ffmpeg
        string FindFfmpeg()
        {
            var cands = new List<string>();
            string t = txtFfmpeg.Text.Trim().Trim('"');
            if (t != "") cands.Add(t);
            cands.Add(Path.Combine(appDir, "ffmpeg.exe"));
            cands.Add(Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe"));
            if (!string.IsNullOrEmpty(runner.PiperExe)) cands.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(runner.PiperExe)), "ffmpeg.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { if (dir.Trim() != "") { cands.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe")); cands.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg")); } } catch { }
            }
            foreach (var c in cands) try { if (File.Exists(c)) return c; } catch { }
            return null;
        }

        void UpdateFfmpegLabel()
        {
            if (lblFfmpeg == null) return;
            var f = FindFfmpeg();
            lblFfmpeg.Text = f != null ? Lang.T("Found: ") + f : Lang.T("Not found. WAV export still works. For other formats, point this to ffmpeg.exe (for example the one you use with yt-dlp).");
            lblFfmpeg.ForeColor = f != null ? Color.DarkGreen : (cboFormat.SelectedIndex > 0 ? Color.Firebrick : Color.DimGray);
        }

        // ================================================================== text helpers
        void AutoDirection()
        {
            bool rtl;
            if (cboDir.SelectedIndex == 1) rtl = false;
            else if (cboDir.SelectedIndex == 2) rtl = true;
            else rtl = TextTools.StartsRtl(txt.Text);
            var want = rtl ? RightToLeft.Yes : RightToLeft.No;
            if (txt.RightToLeft != want) txt.RightToLeft = want;
        }

        void UpdateCount()
        {
            string t = txt.Text;
            int words = t.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int paras = TextTools.MakeChunks(t, 0).Count;
            lblCount.Text = string.Format(Lang.T("{0} words · {1} paragraphs · {2} characters"), words, paras, t.Length);
        }

        void SetTextSize(float s)
        {
            textFontSize = Math.Max(8, Math.Min(36, s));
            txt.Font = new Font(txt.Font.FontFamily, textFontSize);
        }

        static string ReadTextFile(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                int skip = (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) ? 3 : 0;
                if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
                if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
                return utf8.GetString(b, skip, b.Length - skip);
            }
            catch (DecoderFallbackException)
            {
                try { return Encoding.GetEncoding(1256).GetString(b); } catch { return Encoding.Default.GetString(b); }
            }
        }

        void OpenText()
        {
            using (var d = new OpenFileDialog { Filter = "Text files|*.txt;*.md|All files|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    txt.Text = ReadTextFile(d.FileName).Replace("\r\n", "\n").Replace("\n", "\r\n");
                    if (txtFileName.Text.Trim() == "") txtFileName.Text = "";
                    SetStatus("Opened " + d.FileName);
                }
        }

        void SaveText()
        {
            using (var d = new SaveFileDialog { Filter = "Text file (UTF-8)|*.txt", FileName = AutoName(txt.Text) + ".txt" })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(d.FileName, txt.Text, new UTF8Encoding(false));
                    SetStatus("Saved " + d.FileName);
                }
        }

        void ResetSound()
        {
            var v = SelectedVoice(cboVoice);
            trkSpeed.Value = 100;
            trkNoise.Value = (int)Math.Round((v != null ? v.NoiseScale : 0.667) * 100);
            trkNoiseW.Value = (int)Math.Round(Math.Min(1, v != null ? v.NoiseW : 0.8) * 100);
            trkVolume.Value = 100;
            numSentence.Value = 0.2m; numParagraph.Value = 0.5m;
        }

        void ShowDiagnostics()
        {
            var ctx = new DiagContext
            {
                Runner = runner,
                Version = Program.Version,
                GetJob = () => (Job)Invoke(new Func<Job>(() => MakeJob(true))),
                GetVoices = () => new List<VoiceInfo>(voices),
                GetFfmpeg = () => (string)Invoke(new Func<string>(FindFfmpeg)),
                GetOutDir = () => (string)Invoke(new Func<string>(OutDir)),
                GetToken = () => (string)Invoke(new Func<string>(() => txtToken.Text.Trim())),
                GetProxy = () => (string)Invoke(new Func<string>(() => txtProxy.Text.Trim())),
                GetApiBase = () => Ini("botapi", "https://api.telegram.org"),
                GetLogTail = () => (string)Invoke(new Func<string>(() => txtLog.Text)),
                GetOutputIds = () => (List<int>)Invoke(new Func<List<int>>(OutputIds))
            };
            using (var f = new DiagnosticsForm(ctx)) f.ShowDialog(this);
        }

        TracksForm tracks;

        void OpenTracks()
        {
            if (tracks != null && !tracks.IsDisposed) { tracks.WindowState = tracks.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : tracks.WindowState; tracks.Activate(); return; }
            var host = new TracksHost
            {
                Runner = runner,
                GetJob = () => (Job)Invoke(new Func<Job>(() => MakeJob(true))),
                GetFfmpeg = () => (string)Invoke(new Func<string>(FindFfmpeg)),
                GetOutDir = () => (string)Invoke(new Func<string>(OutDir)),
                GetEditorText = () => txt.SelectionLength > 0 ? txt.SelectedText : txt.Text,
                Log = s => AddLog(s),
                GetSetting = k => Ini(k, ""),
                SetSetting = (k, v) => ini[k] = v
            };
            tracks = new TracksForm(host);
            tracks.Show();
            WarmUp();
        }

        void SwitchLanguage(bool fa)
        {
            if (fa == Lang.Fa) return;
            ini["lang"] = fa ? "fa" : "en";
            bool wasFa = Lang.Fa; Lang.Fa = fa;
            string q = Lang.T("Language changed. Restart Piper Studio now?");
            Lang.Fa = wasFa;
            langOverride = fa ? "fa" : "en";
            if (MessageBox.Show(this, q, "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                restarting = true;
                Close();
            }
        }

        void ShowTips()
        {
            MessageBox.Show(this, Lang.Fa ? Lang.T("TIPS") :
                "• F5 plays, Esc stops, Ctrl+E exports. Select text first to play/export only that part.\n\n" +
                "• Quick phrases: click a phrase button (or press Ctrl+1 … 9) to say it at once. Right-click to edit; ＋ adds phrases and groups.\n\n" +
                "• History tab: everything you said. Double-click to say it again. In Say now, ↑ / ↓ go through it.\n\n" +
                "• Ctrl+Alt+Space reads the selected text aloud in any program (see the Shortcuts tab).\n\n" +
                "• Each line is a paragraph. Export can make one file, or one file per paragraph.\n\n" +
                "• Insert silence with [pause] (1 second) or [pause 2.5].\n\n" +
                "• Persian: use a Persian voice (e.g. fa_IR-amir-medium). Typing in Finglish? Tick the Finglish box on the Voice tab.\n\n" +
                "• Persian + English in one text: tick \"Read English words with a second voice\" and choose an English voice.\n\n" +
                "• Wrong pronunciation? Add a rule on the Text tab, e.g.  AI=ای آی  or  Pixune=پیکسیون\n\n" +
                "• Talk in Telegram with this voice: see the Mic tab (needs the free VB-CABLE).\n\n" +
                "• More voices: Voices → Download more voices. Put each .onnx with its .onnx.json in the 'voices' folder, then Voices → Refresh.",
                Lang.T("Tips"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================================================================== misc
        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { Play(); e.Handled = true; }
            else if (e.Control && !e.Alt && !e.Shift && ((e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)))
            {
                SpeakPhrase(e.KeyCode >= Keys.NumPad1 ? e.KeyCode - Keys.NumPad1 : e.KeyCode - Keys.D1);
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && busy) { StopAll(); e.Handled = true; }
        }

        void UI(Action a)
        {
            if (IsDisposed) return;
            try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        void SetStatus(string s) { status.Text = s; }

        void AddLog(string s)
        {
            UI(() =>
            {
                if (txtLog == null) return;
                txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n");
            });
        }

        void ShowError(Exception ex)
        {
            AddLog("ERROR: " + ex.Message);
            SetStatus("Error: " + ex.Message.Split('\n')[0]);
            MessageBox.Show(this, ex.Message, Lang.T("Piper Studio — error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static void OpenUrl(string url) { try { Process.Start(url); } catch { } }
        static void OpenFolder(string d) { try { Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); } catch { } }
        static void SelectInExplorer(string file, bool select)
        {
            try
            {
                if (select) Process.Start("explorer.exe", "/select,\"" + file + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(file) + "\"");
            }
            catch { }
        }

        // ================================================================== settings
        string Ini(string k, string def) { string v; return ini.TryGetValue(k, out v) ? v : def; }
        int IniInt(string k, int def) { int v; return int.TryParse(Ini(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
        decimal IniDec(string k, decimal def) { decimal v; return decimal.TryParse(Ini(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        bool IniBool(string k, bool def) { string v = Ini(k, ""); return v == "" ? def : v == "1"; }

        void ReadIni()
        {
            try
            {
                if (File.Exists(IniPath))
                    foreach (var line in File.ReadAllLines(IniPath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) ini[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
                    }
            }
            catch { }
        }

        static string AskLanguage()
        {
            using (var f = new Form { Text = "Piper Studio", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(420, 170), Font = new Font("Segoe UI", 10f) })
            {
                string pick = "en";
                var l = new Label { Text = "Choose your language   ·   زبان را انتخاب کنید", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 60 };
                var en = new Button { Text = "English", Size = new Size(160, 60), Location = new Point(30, 80), Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
                var fa = new Button { Text = "فارسی", Size = new Size(160, 60), Location = new Point(230, 80), Font = new Font("Tahoma", 13f, FontStyle.Bold) };
                en.Click += (s, e) => { pick = "en"; f.Close(); };
                fa.Click += (s, e) => { pick = "fa"; f.Close(); };
                f.Controls.Add(en); f.Controls.Add(fa); f.Controls.Add(l);
                f.ShowDialog();
                return pick;
            }
        }

        void ShowWelcome()
        {
            string msg = Lang.Fa
                ? "به Piper Studio خوش آمدید!\n\nاین برنامه هر متنی را که بنویسید با صدای طبیعی می‌خواند. سه راه برای استفاده:\n\n۱. پخش و ذخیره: متن را در کادر بزرگ بنویسید، F5 برای شنیدن و Ctrl+E برای ذخیرهٔ فایل صوتی.\n\n۲. صحبت در تلگرام و تماس‌ها: در زبانهٔ «میکروفون» یک بار راه‌اندازی کنید، بعد در کادر «الان بگو» بنویسید و Enter بزنید.\n\n۳. ربات تلگرام: در زبانهٔ «ربات» ربات خودتان را بسازید و در هر چت بنویسید ‎@ربات جمله‎.\n\nجمله‌های آماده (زیر کادر متن) جمله‌های پرکاربرد را با یک کلیک یا Ctrl+1 تا 9 می‌گویند، و Ctrl+Alt+Space متن انتخاب‌شده را در هر برنامه‌ای بلند می‌خواند.\n\nصداها: یک صدای فارسی (amir) و یک صدای انگلیسی (ryan) همراه برنامه است. برای انتخاب، زبانهٔ «صدا» را ببینید.\n\nراهنمای کامل: فایل README-FA.txt در همین پوشه."
                : "Welcome to Piper Studio!\n\nIt reads anything you type in a natural voice. Three ways to use it:\n\n1. Play & save: type in the big box, press F5 to listen and Ctrl+E to save an audio file.\n\n2. Talk in Telegram and calls: set up the Mic tab once, then type in \"Say now\" and press Enter.\n\n3. Telegram bot: create your own bot on the Bot tab, then type  @yourbot sentence  in any chat.\n\nQuick phrases (under the text box) say common sentences with one click or Ctrl+1 … 9, and Ctrl+Alt+Space reads selected text aloud in any program.\n\nVoices: a Persian voice (amir) and an English voice (ryan) are included — choose on the Voice tab.\n\nFull guide: README.txt in this folder.";
            MessageBox.Show(this, msg, "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void LoadSettings()
        {
            string piper = Ini("piper", "");
            if (piper == "" || !File.Exists(piper))
            {
                foreach (var c in new[] { Path.Combine(appDir, "piper.exe"), Path.Combine(appDir, "piper", "piper.exe"), Path.Combine(appDir, "piper") })
                    if (File.Exists(c)) { piper = c; break; }
            }
            runner.PiperExe = File.Exists(piper) ? piper : "";
            txtPiper.Text = runner.PiperExe;
        }

        void ApplySettingsToUi()
        {
            SelectVoice(cboVoice, Ini("voice", ""), "fa");
            FillSpeakers(cboVoice, cboSpeaker, IniInt("speaker", 0));
            chkFinglish.Checked = IniBool("finglish", false);
            chkMixed.Checked = IniBool("mixed", false);
            SelectVoice(cboVoice2, Ini("voice2", ""), "en");
            FillSpeakers(cboVoice2, cboSpeaker2, IniInt("speaker2", 0));
            trkSpeed.Value = Clamp(IniInt("speed", 100), trkSpeed);
            trkNoise.Value = Clamp(IniInt("noise", 67), trkNoise);
            trkNoiseW.Value = Clamp(IniInt("noisew", 80), trkNoiseW);
            trkVolume.Value = Clamp(IniInt("volume", 100), trkVolume);
            numSentence.Value = Math.Min(numSentence.Maximum, IniDec("sentence", 0.2m));
            numParagraph.Value = Math.Min(numParagraph.Maximum, IniDec("paragraph", 0.5m));
            txtOutDir.Text = Ini("outdir", AppPaths.DefaultExportDir);
            cboFormat.SelectedIndex = Math.Min(cboFormat.Items.Count - 1, IniInt("format", 0));
            cboBitrate.SelectedIndex = Math.Min(cboBitrate.Items.Count - 1, IniInt("bitrate", 2));
            cboRate.SelectedIndex = Math.Min(cboRate.Items.Count - 1, IniInt("rate", 0));
            chkSplit.Checked = IniBool("split", false);
            chkNoOverwrite.Checked = IniBool("nooverwrite", true);
            chkOpenAfter.Checked = IniBool("openafter", true);
            txtFfmpeg.Text = Ini("ffmpeg", "");
            chkNormalize.Checked = IniBool("normalize", true);
            cboDir.SelectedIndex = Math.Min(2, IniInt("direction", 0));
            SetTextSize((float)IniDec("fontsize", 13));
            RefreshDevices(Ini("device", ""));
            txtToken.Text = Secret.Unprotect(Ini("bottoken", "")); txtProxy.Text = Ini("botproxy", "");
            long.TryParse(Ini("botowner", "0"), out botOwner); botOwnerName = Ini("botownername", "");
            chkBotAuto.Checked = IniBool("botauto", false);
            UpdateOwnerLabel();
            chkMonitor.Checked = IniBool("monitor", true);
            chkSrt.Checked = IniBool("srt", false);
            chkHkSpeak.Checked = IniBool("hk_speak", true); chkHkShow.Checked = IniBool("hk_show", true); chkHkPhrases.Checked = IniBool("hk_phrases", false);
            chkTray.Checked = IniBool("tray", false); chkSelToSpeakers.Checked = IniBool("seltospeakers", true);
            chkTopMost.Checked = IniBool("topmost", false);
            int w = IniInt("w", 0), h = IniInt("h", 0);
            if (w >= MinimumSize.Width && h >= MinimumSize.Height) Size = new Size(w, h);
            if (IniBool("max", false)) WindowState = FormWindowState.Maximized;
            try { txtRepl.Text = File.Exists(ReplPath) ? File.ReadAllText(ReplPath, Encoding.UTF8) : "# Examples (remove the # to use):\r\n# AI=ای آی\r\n# Pixune=پیکسیون\r\n# e.g.=for example\r\n"; } catch { }
            try { if (File.Exists(LastTextPath)) txt.Text = File.ReadAllText(LastTextPath, Encoding.UTF8); } catch { }
            UpdateVoiceOptions();
        }

        static int Clamp(int v, TrackBar t) { return Math.Max(t.Minimum, Math.Min(t.Maximum, v)); }

        void SaveSettings()
        {
            var d = new Dictionary<string, string>();
            var v1 = SelectedVoice(cboVoice); var v2 = SelectedVoice(cboVoice2);
            d["piper"] = runner.PiperExe ?? "";
            d["voice"] = v1 != null ? v1.FileName : "";
            d["speaker"] = (cboSpeaker.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker.SelectedItem).Id : 0).ToString();
            d["voice2"] = v2 != null ? v2.FileName : "";
            d["speaker2"] = (cboSpeaker2.SelectedItem is SpeakerItem ? ((SpeakerItem)cboSpeaker2.SelectedItem).Id : 0).ToString();
            d["finglish"] = chkFinglish.Checked ? "1" : "0";
            d["mixed"] = chkMixed.Checked ? "1" : "0";
            d["speed"] = trkSpeed.Value.ToString(); d["noise"] = trkNoise.Value.ToString();
            d["noisew"] = trkNoiseW.Value.ToString(); d["volume"] = trkVolume.Value.ToString();
            d["sentence"] = numSentence.Value.ToString(CultureInfo.InvariantCulture);
            d["paragraph"] = numParagraph.Value.ToString(CultureInfo.InvariantCulture);
            d["outdir"] = txtOutDir.Text; d["format"] = cboFormat.SelectedIndex.ToString();
            d["bitrate"] = cboBitrate.SelectedIndex.ToString(); d["rate"] = cboRate.SelectedIndex.ToString();
            d["split"] = chkSplit.Checked ? "1" : "0"; d["nooverwrite"] = chkNoOverwrite.Checked ? "1" : "0";
            d["openafter"] = chkOpenAfter.Checked ? "1" : "0"; d["ffmpeg"] = txtFfmpeg.Text;
            d["normalize"] = chkNormalize.Checked ? "1" : "0"; d["direction"] = cboDir.SelectedIndex.ToString();
            d["fontsize"] = textFontSize.ToString(CultureInfo.InvariantCulture);
            d["bottoken"] = Secret.Protect(txtToken.Text.Trim()); d["botproxy"] = txtProxy.Text.Trim(); d["botowner"] = botOwner.ToString(CultureInfo.InvariantCulture);
            d["botownername"] = botOwnerName; d["botauto"] = chkBotAuto.Checked ? "1" : "0";
            if (ini.ContainsKey("botapi")) d["botapi"] = ini["botapi"];
            foreach (var kv in ini) if (kv.Key.StartsWith("tracks_")) d[kv.Key] = kv.Value;
            d["lang"] = langOverride ?? (Lang.Fa ? "fa" : "en");
            d["srt"] = chkSrt.Checked ? "1" : "0";
            d["hk_speak"] = chkHkSpeak.Checked ? "1" : "0"; d["hk_show"] = chkHkShow.Checked ? "1" : "0"; d["hk_phrases"] = chkHkPhrases.Checked ? "1" : "0";
            d["tray"] = chkTray.Checked ? "1" : "0"; d["seltospeakers"] = chkSelToSpeakers.Checked ? "1" : "0";
            d["keephistory"] = chkKeepHistory.Checked ? "1" : "0";
            d["phrasecat"] = phraseCat.ToString(); d["phrasesopen"] = phrasesOpen ? "1" : "0";
            d["device"] = DeviceName(); d["monitor"] = chkMonitor.Checked ? "1" : "0"; d["topmost"] = chkTopMost.Checked ? "1" : "0";
            d["max"] = WindowState == FormWindowState.Maximized ? "1" : "0";
            if (WindowState == FormWindowState.Normal) { d["w"] = Width.ToString(); d["h"] = Height.ToString(); }
            else { d["w"] = RestoreBounds.Width.ToString(); d["h"] = RestoreBounds.Height.ToString(); }
            try { File.WriteAllLines(IniPath, d.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false)); } catch { }
            try { File.WriteAllText(ReplPath, txtRepl.Text, new UTF8Encoding(false)); } catch { }
            try { File.WriteAllText(LastTextPath, txt.Text, new UTF8Encoding(false)); } catch { }
        }
    }

    // Hidden command-line test: PiperStudio.exe --selftest <piper> <model.onnx> <out.wav> <text> [second-model.onnx] [finglish]
    static class SelfTest
    {
        public static int Run(string[] a)
        {
            var r = new PiperRunner { PiperExe = a[1], Log = Console.WriteLine };
            var v = VoiceInfo.Load(a[2]);
            Console.WriteLine("Voice: " + v + " err=" + v.LoadError + "  rate=" + v.SampleRate + " persian=" + v.IsPersian);
            var job = new Job { Main = v, Normalize = true };
            if (a.Length > 5 && a[5] != "-") { job.Second = VoiceInfo.Load(a[5]); job.Mixed = true; }
            if (a.Length > 6) job.Finglish = true;
            job.Replacements = TextTools.ParseReplacements("LHC=Large Hadron Collider");
            var chunks = TextTools.MakeChunks(a[4].Replace("\\n", "\n"), 0);
            var pieces = new List<short[]>();
            for (int i = 0; i < chunks.Count; i++)
            {
                Console.WriteLine("chunk " + i + ": [" + chunks[i].Text + "]");
                pieces.Add(Synth.Chunk(r, chunks[i].Text, job, i < chunks.Count - 1, CancellationToken.None));
            }
            var all = Audio.Concat(pieces);
            Audio.WriteWav(a[3], all, v.SampleRate);
            Console.WriteLine("samples=" + all.Length + " seconds=" + (all.Length / (double)v.SampleRate).ToString("0.00"));
            return 0;
        }
    }
}
