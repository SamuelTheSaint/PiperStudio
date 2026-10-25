using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    // What the Tracks window needs from the main window.
    public class TracksHost
    {
        public PiperRunner Runner;
        public Func<Job> GetJob;
        public Func<string> GetFfmpeg;
        public Func<string> GetOutDir;
        public Func<string> GetEditorText;
        public Action<string> Log;
        public Func<string, string> GetSetting;
        public Action<string, string> SetSetting;
    }

    public class TracksForm : Form
    {
        readonly TracksHost host;
        readonly TimelineView tl = new TimelineView { Dock = DockStyle.Fill };
        readonly History hist = new History();
        string projectPath;
        bool modified;

        Button bPlay, bStop, bRec;
        Label lblTime, lblVoice, lblLevel;
        ComboBox cboMic, cboOut;
        List<string> outNames = new List<string>(), micNames = new List<string>();
        CheckBox chkPlayWhileRec, chkSentences;
        TextBox txtSpeech;
        ToolStripStatusLabel status;
        System.Windows.Forms.Timer timer;

        CancellationTokenSource playCts;
        Stopwatch playClock;
        long playFrom;
        Recorder rec;
        NumericUpDown numLatency;
        ToolStripMenuItem miSubs;
        bool generating;
        int recCount;

        public TracksForm(TracksHost host)
        {
            this.host = host;
            Text = Lang.T("Tracks editor") + " — Piper Studio";
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1200, 760);
            MinimumSize = new Size(900, 520);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Lang.Apply(this);

            tl.BeforeChange += () => { hist.Push(tl.P); };
            tl.Changed += () => { MarkModified(); };
            tl.Seek += OnSeek;
            tl.HoverHint += h => SetStatus(h);
            tl.SelectionChanged += () => UpdateTime();
            tl.ClipDoubleClick += (c, t) => EditClip(c);
            tl.ClipMenu += ShowClipMenu;
            tl.TrackMenu += ShowTrackMenu;
            tl.DeleteTrack += t => DeleteTrack(t);
            tl.AddTrackClicked += () => AddTrack(null, true);
            tl.FilesDropped += (files, track, pos) => ImportFiles(files, track, pos);
            KeyDown += OnKey;
            FormClosing += OnClosing;

            timer = new System.Windows.Forms.Timer { Interval = 30 };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            NewProject();
            Shown += (s, e) => UpdateVoiceLabel();
        }

        // ================================================================== UI
        Button Btn(string text, string tip, EventHandler click, Color? color = null)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(2, 3, 2, 3) };
            if (color.HasValue) { b.BackColor = color.Value; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); }
            b.Click += click;
            if (tip != null) new ToolTip().SetToolTip(b, Lang.T(tip));
            return b;
        }

        static Control Sep() { return new Label { Width = 1, Height = 26, BackColor = Color.Silver, Margin = new Padding(6, 4, 6, 0) }; }

        void BuildUi()
        {
            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add(new ToolStripMenuItem("&New project", null, (s, e) => { if (ConfirmDiscard()) NewProject(); }) { ShortcutKeys = Keys.Control | Keys.N });
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Open project…", null, (s, e) => OpenProject()) { ShortcutKeys = Keys.Control | Keys.O });
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Save project", null, (s, e) => SaveProject(false)) { ShortcutKeys = Keys.Control | Keys.S });
            mFile.DropDownItems.Add(new ToolStripMenuItem("Save project &as…", null, (s, e) => SaveProject(true)));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Import audio (music, sounds)…", null, (s, e) => ImportAudio()) { ShortcutKeys = Keys.Control | Keys.I });
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Export mix…", null, (s, e) => ExportMix()) { ShortcutKeys = Keys.Control | Keys.E });
            mFile.DropDownItems.Add(new ToolStripMenuItem("Export each &track separately…", null, (s, e) => ExportTracks()));
            mFile.DropDownItems.Add(new ToolStripMenuItem("Export s&ubtitles (.srt)…", null, (s, e) => ExportSubtitles()));
            miSubs = new ToolStripMenuItem("Also save subtitles (.srt) when exporting the mix") { CheckOnClick = true, Checked = host.GetSetting == null || host.GetSetting("tracks_srt") != "0" };
            miSubs.CheckedChanged += (s, e) => { if (host.SetSetting != null) host.SetSetting("tracks_srt", miSubs.Checked ? "1" : "0"); };
            mFile.DropDownItems.Add(miSubs);
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("&Close", null, (s, e) => Close()));
            var mEdit = new ToolStripMenuItem("&Edit");
            mEdit.DropDownItems.Add(new ToolStripMenuItem("&Undo", null, (s, e) => Undo()) { ShortcutKeys = Keys.Control | Keys.Z });
            mEdit.DropDownItems.Add(new ToolStripMenuItem("&Redo", null, (s, e) => Redo()) { ShortcutKeys = Keys.Control | Keys.Y });
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(new ToolStripMenuItem("&Split clip at playhead   (S)", null, (s, e) => SplitAtPlayhead()));
            mEdit.DropDownItems.Add(new ToolStripMenuItem("&Duplicate clip", null, (s, e) => Duplicate()) { ShortcutKeys = Keys.Control | Keys.D });
            mEdit.DropDownItems.Add(new ToolStripMenuItem("D&elete clip   (Del)", null, (s, e) => DeleteClip()));
            mEdit.DropDownItems.Add(new ToolStripMenuItem("Clip &properties…   (Enter)", null, (s, e) => { if (tl.SelClip != null) EditClip(tl.SelClip); }));
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(new ToolStripMenuItem("&Add track", null, (s, e) => AddTrack(null, true)) { ShortcutKeys = Keys.Control | Keys.T });
            var mView = new ToolStripMenuItem("&View");
            mView.DropDownItems.Add(new ToolStripMenuItem("Zoom &in   (Ctrl + wheel)", null, (s, e) => tl.Zoom(1.5, tl.Width / 2)));
            mView.DropDownItems.Add(new ToolStripMenuItem("Zoom &out", null, (s, e) => tl.Zoom(1 / 1.5, tl.Width / 2)));
            mView.DropDownItems.Add(new ToolStripMenuItem("&Fit project in window", null, (s, e) => tl.ZoomToFit()) { ShortcutKeys = Keys.Control | Keys.F });
            menu.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mView });

            // transport + tools bar
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 2, 4, 2) };
            bPlay = Btn("▶ Play", "Play / pause (Space)", (s, e) => TogglePlay(), Color.FromArgb(40, 120, 70));
            bStop = Btn("■ Stop", "Stop and go to the start (Home)", (s, e) => { StopAll(); tl.Playhead = 0; tl.EnsureVisible(0); tl.Invalidate(); UpdateTime(); }, Color.FromArgb(110, 110, 120));
            bRec = Btn("● Record", "Record your voice from the microphone (R)", (s, e) => ToggleRecord(), Color.FromArgb(170, 50, 50));
            lblTime = new Label { AutoSize = true, Font = new Font("Consolas", 12f, FontStyle.Bold), Margin = new Padding(8, 7, 8, 0), Text = "00:00.00" };
            bar.Controls.AddRange(new Control[] { bPlay, bStop, bRec, lblTime, Sep() });
            bar.Controls.Add(Btn("+ Track", "Add an empty track (Ctrl+T)", (s, e) => AddTrack(null, true)));
            bar.Controls.Add(Btn("♪ Import audio…", "Import music or sound effects (Ctrl+I)", (s, e) => ImportAudio()));
            bar.Controls.Add(Sep());
            bar.Controls.Add(Btn("✂ Split", "Split the selected clip at the playhead (S)", (s, e) => SplitAtPlayhead()));
            bar.Controls.Add(Btn("⧉ Duplicate", "Duplicate the selected clip (Ctrl+D)", (s, e) => Duplicate()));
            bar.Controls.Add(Btn("✕ Delete", "Delete the selected clip (Del)", (s, e) => DeleteClip()));
            bar.Controls.Add(Btn("⚙ Clip…", "Clip volume, fades, text (Enter or double-click)", (s, e) => { if (tl.SelClip != null) EditClip(tl.SelClip); }));
            bar.Controls.Add(Sep());
            bar.Controls.Add(Btn("↶", "Undo (Ctrl+Z)", (s, e) => Undo()));
            bar.Controls.Add(Btn("↷", "Redo (Ctrl+Y)", (s, e) => Redo()));
            bar.Controls.Add(Btn("−", "Zoom out", (s, e) => tl.Zoom(1 / 1.5, tl.Width / 2)));
            bar.Controls.Add(Btn("+", "Zoom in (Ctrl + mouse wheel)", (s, e) => tl.Zoom(1.5, tl.Width / 2)));
            bar.Controls.Add(Btn("⤢ Fit", "Fit the whole project in the window (Ctrl+F)", (s, e) => tl.ZoomToFit()));
            bar.Controls.Add(Sep());
            bar.Controls.Add(Btn("💾 Save", "Save project (Ctrl+S)", (s, e) => SaveProject(false)));
            bar.Controls.Add(Btn("CC", "Export subtitles (.srt) from the speech clips", (s, e) => ExportSubtitles()));
            bar.Controls.Add(Btn("⬇ Export mix…", "Export everything as one audio file (Ctrl+E)", (s, e) => ExportMix(), Color.FromArgb(40, 90, 160)));

            // recording bar
            var rbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 0, 4, 2) };
            rbar.Controls.Add(new Label { Text = "Play to:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
            cboOut = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            rbar.Controls.Add(cboOut);
            rbar.Controls.Add(new Label { Text = "Microphone:", AutoSize = true, Margin = new Padding(12, 7, 3, 0) });
            cboMic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            rbar.Controls.Add(cboMic);
            var bRefresh = Btn("⟳", "Refresh the device lists (after plugging in headphones or a microphone)", (s, e) => FillDevices());
            rbar.Controls.Add(bRefresh);
            FillDevices();
            cboOut.SelectedIndexChanged += (s, e) => { if (host.SetSetting != null) host.SetSetting("tracks_out", cboOut.SelectedIndex > 0 ? outNames[cboOut.SelectedIndex - 1] : ""); };
            cboMic.SelectedIndexChanged += (s, e) => { if (host.SetSetting != null) host.SetSetting("tracks_mic", cboMic.SelectedIndex > 0 ? micNames[cboMic.SelectedIndex - 1] : ""); };
            chkPlayWhileRec = new CheckBox { Text = "Play the other tracks while recording (use headphones)", AutoSize = true, Checked = true, Margin = new Padding(10, 6, 3, 0) };
            rbar.Controls.Add(chkPlayWhileRec);
            rbar.Controls.Add(new Label { Text = "Recording delay (ms):", AutoSize = true, Margin = new Padding(12, 7, 3, 0) });
            numLatency = new NumericUpDown { Minimum = 0, Maximum = 1000, Increment = 10, Width = 64, Margin = new Padding(3, 4, 3, 0) };
            int lat; if (host.GetSetting != null && int.TryParse(host.GetSetting("tracks_latency"), out lat)) numLatency.Value = Math.Max(0, Math.Min(1000, lat));
            numLatency.ValueChanged += (s, e) => { if (host.SetSetting != null) host.SetSetting("tracks_latency", ((int)numLatency.Value).ToString()); };
            new ToolTip().SetToolTip(numLatency, Lang.T("Recordings made while other tracks play are moved this much earlier, so your voice lines up with the music. Use Measure… to find the right value."));
            rbar.Controls.Add(numLatency);
            rbar.Controls.Add(Btn("Measure…", "Measure the delay automatically with a few beeps", (s, e) => CalibrateLatency()));
            lblLevel = new Label { AutoSize = true, Margin = new Padding(10, 7, 3, 0), ForeColor = Color.Firebrick, Font = new Font("Consolas", 9f) };
            rbar.Controls.Add(lblLevel);

            // speech panel
            var sp = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
            sp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            head.Controls.Add(new Label { Text = "Speech:", AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(3, 5, 3, 0) });
            lblVoice = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 0) };
            head.Controls.Add(lblVoice);
            var bFromEditor = new LinkLabel { Text = "Use the text from the main window", AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
            bFromEditor.LinkClicked += (s, e) => { if (host.GetEditorText != null) txtSpeech.Text = host.GetEditorText(); };
            head.Controls.Add(bFromEditor);
            sp.Controls.Add(head, 0, 0); sp.SetColumnSpan(head, 2);
            txtSpeech = new TextBox { Multiline = true, Height = 64, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Tahoma", 11f), AcceptsReturn = true };
            txtSpeech.TextChanged += (s, e) => { var want = TextTools.StartsRtl(txtSpeech.Text) ? RightToLeft.Yes : RightToLeft.No; if (txtSpeech.RightToLeft != want) txtSpeech.RightToLeft = want; };
            txtSpeech.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; AddSpeech(); } };
            sp.Controls.Add(txtSpeech, 0, 1);
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(6, 0, 0, 0) };
            var bAdd = Btn("＋ Add speech at playhead", "Turn the text into a clip on the selected track (Ctrl+Enter)", (s, e) => AddSpeech(), Color.FromArgb(70, 120, 200));
            chkSentences = new CheckBox { Text = "One clip per sentence", AutoSize = true, Checked = true };
            right.Controls.Add(bAdd); right.Controls.Add(chkSentences);
            sp.Controls.Add(right, 1, 1);

            var st = new StatusStrip();
            status = new ToolStripStatusLabel(Lang.T("Ready")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            st.Items.Add(status);

            Controls.Add(tl);
            Controls.Add(sp);
            Controls.Add(st);
            Controls.Add(rbar);
            Controls.Add(bar);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        void FillDevices()
        {
            string wantOut = cboOut.SelectedIndex > 0 ? outNames[cboOut.SelectedIndex - 1] : (host.GetSetting != null ? host.GetSetting("tracks_out") : "");
            string wantMic = cboMic.SelectedIndex > 0 ? micNames[cboMic.SelectedIndex - 1] : (host.GetSetting != null ? host.GetSetting("tracks_mic") : "");
            outNames = WaveOut.Available ? WaveOut.Devices() : new List<string>();
            micNames = SafeMics();
            cboOut.Items.Clear(); cboOut.Items.Add(Lang.T("Default speakers"));
            foreach (var d in outNames) cboOut.Items.Add(d);
            cboMic.Items.Clear(); cboMic.Items.Add(Lang.T("Default microphone"));
            foreach (var d in micNames) cboMic.Items.Add(d);
            int oi = outNames.IndexOf(wantOut ?? ""), mi = micNames.IndexOf(wantMic ?? "");
            cboOut.SelectedIndex = oi >= 0 ? oi + 1 : 0;
            cboMic.SelectedIndex = mi >= 0 ? mi + 1 : 0;
        }

        static List<string> SafeMics() { try { return WaveOut.Available ? Recorder.Devices() : new List<string>(); } catch { return new List<string>(); } }

        void SetStatus(string s) { status.Text = s; }
        void Log(string s) { if (host.Log != null) host.Log("[tracks] " + s); }

        void UI(Action a) { if (IsDisposed) return; try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { } }

        void ShowError(Exception ex)
        {
            Log("ERROR: " + ex.Message);
            SetStatus(Lang.T("Error: ") + ex.Message.Split('\n')[0]);
            MessageBox.Show(this, ex.Message, Lang.T("Piper Studio — error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ================================================================== project
        void NewProject()
        {
            StopAll();
            tl.P = new Project();
            tl.SelTrack = null; tl.SelClip = null; tl.Playhead = 0; tl.ScrollX = 0;
            tl.ForgetPeaks(); hist.Clear();
            projectPath = null; modified = false;
            UpdateTitle(); tl.UpdateScroll(); tl.Invalidate(); UpdateTime();
        }

        void MarkModified()
        {
            modified = true; UpdateTitle(); tl.UpdateScroll(); tl.Invalidate(); UpdateTime();
            if (playCts != null) playSnap = tl.P.Clone();   // let the playing audio hear the change
        }

        void UpdateTitle()
        {
            string name = projectPath != null ? Path.GetFileNameWithoutExtension(projectPath) : Lang.T("Untitled");
            Text = (modified ? "• " : "") + name + " — " + Lang.T("Tracks editor") + " — Piper Studio";
        }

        bool ConfirmDiscard()
        {
            if (!modified) return true;
            var r = MessageBox.Show(this, Lang.T("Save changes to this project?"), "Piper Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveProject(false);
            return true;
        }

        bool SaveProject(bool saveAs)
        {
            if (projectPath == null || saveAs)
            {
                using (var d = new SaveFileDialog { Filter = Lang.T("Piper Studio project") + " (*.pstudio)|*.pstudio", FileName = projectPath != null ? Path.GetFileName(projectPath) : "my project.pstudio", InitialDirectory = SafeOutDir() })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    projectPath = d.FileName;
                }
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                tl.P.Save(projectPath);
                modified = false; UpdateTitle();
                SetStatus(Lang.T("Saved ") + projectPath);
                return true;
            }
            catch (Exception ex) { ShowError(ex); return false; }
            finally { Cursor = Cursors.Default; }
        }

        void OpenProject()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = Lang.T("Piper Studio project") + " (*.pstudio)|*.pstudio", InitialDirectory = SafeOutDir() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { LoadFile(d.FileName); }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        public void LoadFile(string path)
        {
            StopAll();
            tl.P = Project.Load(path);
            tl.ForgetPeaks(); hist.Clear();
            tl.SelTrack = tl.P.Tracks.FirstOrDefault(); tl.SelClip = null; tl.Playhead = 0;
            projectPath = path; modified = false;
            UpdateTitle(); tl.ZoomToFit(); UpdateTime();
            SetStatus(Lang.T("Opened ") + path);
        }

        string SafeOutDir()
        {
            try { var d = host.GetOutDir(); Directory.CreateDirectory(d); return d; } catch { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            StopAll();
            timer.Stop();
        }

        // ================================================================== editing
        void Change(Action a) { hist.Push(tl.P); a(); MarkModified(); }

        Track AddTrack(string name, bool select)
        {
            var t = new Track { Name = name ?? (Lang.T("Track") + " " + (tl.P.Tracks.Count + 1)) };
            Change(() => tl.P.Tracks.Add(t));
            if (select) { tl.SelTrack = t; tl.SelClip = null; }
            return t;
        }

        // Speech goes on the selected track; with no track selected it gets a new one.
        Track TargetTrack()
        {
            if (tl.SelTrack != null && tl.P.Tracks.Contains(tl.SelTrack)) return tl.SelTrack;
            var t = new Track { Name = Lang.T("Speech") + (tl.P.Tracks.Count > 0 ? " " + (tl.P.Tracks.Count + 1) : "") };
            tl.P.Tracks.Add(t);
            tl.SelTrack = t;
            return t;
        }

        void DeleteTrack(Track t)
        {
            if (t.Clips.Count > 0 && MessageBox.Show(this, Lang.T("Delete this track and its clips?"), "Piper Studio", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Change(() => { tl.P.Tracks.Remove(t); FixSelection(); });
        }

        Track FindTrack(Clip c) { return tl.P.Tracks.FirstOrDefault(t => t.Clips.Contains(c)); }

        void SplitAtPlayhead()
        {
            var c = tl.SelClip;
            if (c == null && tl.SelTrack != null) { c = tl.SelTrack.Clips.FirstOrDefault(k => k.Start < tl.Playhead && k.End > tl.Playhead); }
            if (c == null || tl.Playhead <= c.Start + 10 || tl.Playhead >= c.End - 10) { SetStatus(Lang.T("Put the playhead inside a clip to split it.")); return; }
            var t = FindTrack(c);
            Change(() =>
            {
                long cut = c.In + (tl.Playhead - c.Start);
                var b = c.Clone(); b.In = cut; b.Start = tl.Playhead; b.FadeIn = 0;
                c.Out = cut; c.FadeOut = 0;
                t.Clips.Add(b);
                tl.SelClip = b;
            });
        }

        void Duplicate()
        {
            var c = tl.SelClip; if (c == null) return;
            var t = FindTrack(c);
            Change(() => { var d = c.Clone(); d.Start = c.End; t.Clips.Add(d); tl.SelClip = d; });
        }

        void DeleteClip()
        {
            var c = tl.SelClip; if (c == null) return;
            var t = FindTrack(c);
            Change(() => { t.Clips.Remove(c); tl.SelClip = null; });
        }

        void Undo() { if (!hist.CanUndo) return; StopPlayback(); tl.P = hist.Undo(tl.P); FixSelection(); modified = true; UpdateTitle(); tl.UpdateScroll(); tl.Invalidate(); }
        void Redo() { if (!hist.CanRedo) return; StopPlayback(); tl.P = hist.Redo(tl.P); FixSelection(); modified = true; UpdateTitle(); tl.UpdateScroll(); tl.Invalidate(); }

        void FixSelection()
        {
            tl.SelClip = null;
            if (!tl.P.Tracks.Contains(tl.SelTrack)) tl.SelTrack = tl.P.Tracks.FirstOrDefault();
        }

        void ShowClipMenu(Clip c, Track t, Point pt)
        {
            var m = new ContextMenuStrip();
            m.Items.Add(Lang.T("Properties (volume, fades, text)…"), null, (s, e) => EditClip(c));
            if (c.Kind == "speech" && !string.IsNullOrEmpty(c.Text))
                m.Items.Add(Lang.T("Regenerate with current voice"), null, (s, e) => Regenerate(c, c.Text));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Split at playhead"), null, (s, e) => { tl.SelClip = c; SplitAtPlayhead(); });
            m.Items.Add(Lang.T("Duplicate"), null, (s, e) => { tl.SelClip = c; Duplicate(); });
            m.Items.Add(Lang.T("Move to a new track"), null, (s, e) => Change(() => { t.Clips.Remove(c); var nt = new Track { Name = Lang.T("Track") + " " + (tl.P.Tracks.Count + 1) }; nt.Clips.Add(c); tl.P.Tracks.Add(nt); tl.SelTrack = nt; }));
            m.Items.Add(Lang.T("Fade in + out (0.5 s)"), null, (s, e) => Change(() => { c.FadeIn = Math.Min(0.5, c.Length / (double)tl.Rate / 3); c.FadeOut = c.FadeIn; }));
            m.Items.Add(Lang.T("Export this clip…"), null, (s, e) => ExportClip(c));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Delete"), null, (s, e) => { tl.SelClip = c; DeleteClip(); });
            m.Show(tl, pt);
        }

        void ShowTrackMenu(Track t, Point pt)
        {
            var m = new ContextMenuStrip();
            m.Items.Add(Lang.T("Rename track…"), null, (s, e) =>
            {
                string n = Prompt(Lang.T("Track name:"), t.Name);
                if (n != null && n.Trim() != "") Change(() => t.Name = n.Trim());
            });
            m.Items.Add(Lang.T("Move up"), null, (s, e) => Change(() => { int i = tl.P.Tracks.IndexOf(t); if (i > 0) { tl.P.Tracks.RemoveAt(i); tl.P.Tracks.Insert(i - 1, t); } }));
            m.Items.Add(Lang.T("Move down"), null, (s, e) => Change(() => { int i = tl.P.Tracks.IndexOf(t); if (i < tl.P.Tracks.Count - 1) { tl.P.Tracks.RemoveAt(i); tl.P.Tracks.Insert(i + 1, t); } }));
            m.Items.Add(Lang.T("Volume 100%"), null, (s, e) => Change(() => t.Volume = 1));
            m.Items.Add(Lang.T("Pan: centre"), null, (s, e) => Change(() => t.Pan = 0));
            m.Items.Add(new ToolStripSeparator());
            var duck = new ToolStripMenuItem(Lang.T("Duck: get quieter while other tracks play (D)"), null, (s, e) => Change(() => t.Duck = !t.Duck)) { Checked = t.Duck };
            m.Items.Add(duck);
            var amount = new ToolStripMenuItem(Lang.T("Ducking amount"));
            foreach (var db in new[] { 6, 9, 12, 18, 24 })
            {
                int dbv = db;
                amount.DropDownItems.Add(new ToolStripMenuItem("−" + db + " dB" + (db == 12 ? "  (" + Lang.T("default") + ")" : ""), null, (s, e) => Change(() => { t.DuckDb = dbv; t.Duck = true; })) { Checked = t.Duck && Math.Abs(t.DuckDb - db) < 0.1 });
            }
            m.Items.Add(amount);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Export this track…"), null, (s, e) => ExportTrack(t));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Add track below"), null, (s, e) => Change(() => { var nt = new Track { Name = Lang.T("Track") + " " + (tl.P.Tracks.Count + 1) }; tl.P.Tracks.Insert(tl.P.Tracks.IndexOf(t) + 1, nt); tl.SelTrack = nt; }));
            m.Items.Add(Lang.T("Delete track"), null, (s, e) => DeleteTrack(t));
            m.Show(tl, pt);
        }

        string Prompt(string label, string value)
        {
            using (var f = new Form { Text = "Piper Studio", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 110), MaximizeBox = false, MinimizeBox = false, Font = Font })
            {
                var l = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
                var tb = new TextBox { Text = value, Location = new Point(12, 36), Width = 356 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(212, 72), Width = 75 };
                var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(293, 72), Width = 75 };
                f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
            }
        }

        void EditClip(Clip c)
        {
            using (var f = new Form { Text = Lang.T("Clip properties"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) })
            {
                var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
                Action<string, Control> row = (lbl, ctl) => { g.Controls.Add(new Label { Text = Lang.T(lbl), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }); g.Controls.Add(ctl); };
                var name = new TextBox { Text = c.Name, Width = 360 };
                row("Name", name);
                TextBox text = null;
                if (c.Kind == "speech")
                {
                    text = new TextBox { Text = c.Text ?? "", Width = 360, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Tahoma", 10f) };
                    if (TextTools.StartsRtl(text.Text)) text.RightToLeft = RightToLeft.Yes;
                    row("Text", text);
                }
                var gain = new NumericUpDown { Minimum = 0, Maximum = 400, Value = (decimal)Math.Round(c.Gain * 100), Width = 80 };
                row("Volume (%)", gain);
                double secs = c.Length / (double)tl.Rate;
                var fin = new NumericUpDown { Minimum = 0, Maximum = (decimal)Math.Max(0, secs), DecimalPlaces = 2, Increment = 0.1m, Value = (decimal)Math.Min(secs, c.FadeIn), Width = 80 };
                var fout = new NumericUpDown { Minimum = 0, Maximum = (decimal)Math.Max(0, secs), DecimalPlaces = 2, Increment = 0.1m, Value = (decimal)Math.Min(secs, c.FadeOut), Width = 80 };
                row("Fade in (s)", fin);
                row("Fade out (s)", fout);
                var startBox = new NumericUpDown { Minimum = 0, Maximum = 36000, DecimalPlaces = 2, Increment = 0.1m, Value = (decimal)(c.Start / (double)tl.Rate), Width = 100 };
                row("Starts at (s)", startBox);
                row("", new Label { Text = string.Format(Lang.T("Length: {0:0.00} s · type: {1}"), secs, Lang.T(c.Kind)), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 6) });
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
                var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Width = 80 };
                buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
                bool regen = false;
                if (text != null)
                {
                    var rb = new Button { Text = Lang.T("Save and regenerate speech"), AutoSize = true };
                    rb.Click += (s, e) => { regen = true; f.DialogResult = DialogResult.OK; f.Close(); };
                    buttons.Controls.Add(rb);
                }
                g.Controls.Add(buttons); g.SetColumnSpan(buttons, 2);
                f.Controls.Add(g);
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Change(() =>
                {
                    c.Name = name.Text; c.Gain = (float)gain.Value / 100f;
                    c.FadeIn = (double)fin.Value; c.FadeOut = (double)fout.Value;
                    c.Start = (long)((double)startBox.Value * tl.Rate);
                });
                if (text != null && (regen || text.Text != (c.Text ?? ""))) Regenerate(c, text.Text);
            }
        }

        // ================================================================== speech
        void UpdateVoiceLabel()
        {
            Job j = null;
            try { j = host.GetJob(); } catch { }
            lblVoice.Text = j == null ? Lang.T("(choose a voice in the main window)")
                : Lang.T("Voice: ") + j.Main.FileName + (j.Mixed && j.Second != null ? " + " + j.Second.FileName : "") + "   " + Lang.T("(change it in the main window)");
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); UpdateVoiceLabel(); }

        void AddSpeech()
        {
            if (generating) return;
            string text = txtSpeech.Text.Trim();
            if (text.Length == 0) { SetStatus(Lang.T("Type some text in the Speech box first.")); return; }
            var job = host.GetJob();
            if (job == null) { MessageBox.Show(this, Lang.T("Choose a voice in the main Piper Studio window first."), "Piper Studio"); return; }
            var track = TargetTrack();
            long at = tl.Playhead;
            var pieces = chkSentences.Checked ? TextTools.MakeSentences(text).Select(c => c.Text.Trim()).Where(s => s.Length > 0).ToList() : new List<string> { text };
            generating = true;
            SetStatus(Lang.T("Generating speech…"));
            hist.Push(tl.P);
            int rate = tl.Rate;
            Task.Run(() =>
            {
                try
                {
                    long pos = at;
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        string piece = pieces[i];
                        var pcm = Synth.Chunk(host.Runner, piece, job, false, CancellationToken.None);
                        pcm = Audio.Resample(pcm, job.Rate, rate);
                        var clip = Clip.FromData(pcm, pos, Short(piece), "speech");
                        clip.Text = piece;
                        pos += pcm.Length;
                        int ii = i;
                        UI(() =>
                        {
                            track.Clips.Add(clip); tl.SelClip = clip; tl.SelTrack = track;
                            tl.EnsureVisible(clip.End);
                            MarkModified();
                            SetStatus(string.Format(Lang.T("Generating speech… {0} of {1}"), ii + 1, pieces.Count));
                        });
                    }
                    UI(() => { tl.Playhead = pos; UpdateTime(); SetStatus(Lang.T("Speech added. Drag clips to move them, drag their edges to trim.")); });
                }
                catch (Exception ex) { UI(() => ShowError(ex)); }
                finally { UI(() => generating = false); }
            });
        }

        static string Short(string s) { s = s.Replace('\n', ' ').Trim(); return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }

        void Regenerate(Clip c, string text)
        {
            var job = host.GetJob();
            if (job == null || text.Trim().Length == 0) return;
            SetStatus(Lang.T("Generating speech…"));
            int rate = tl.Rate;
            Task.Run(() =>
            {
                try
                {
                    var pcm = Audio.Resample(Synth.Chunk(host.Runner, text, job, false, CancellationToken.None), job.Rate, rate);
                    UI(() => Change(() => { c.Data = pcm; c.In = 0; c.Out = pcm.Length; c.Text = text; c.Name = Short(text); SetStatus(Lang.T("Speech regenerated.")); }));
                }
                catch (Exception ex) { UI(() => ShowError(ex)); }
            });
        }

        // ================================================================== import
        void ImportAudio()
        {
            using (var d = new OpenFileDialog { Filter = Lang.T("Audio files") + "|*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac;*.wma;*.opus|" + Lang.T("All files") + "|*.*", Multiselect = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ImportFiles(d.FileNames, null, 0);
            }
        }

        static readonly string[] AudioExt = { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".mp4", ".webm", ".mkv", ".mov", ".aiff", ".aif" };

        // target == null: every file gets its own new track. Otherwise files are placed one after another on that track.
        void ImportFiles(string[] files, Track target, long pos)
        {
            var list = new List<string>();
            foreach (var f in files)
            {
                if (Directory.Exists(f)) { list.AddRange(Directory.GetFiles(f).Where(x => AudioExt.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x)); continue; }
                if (f.EndsWith(".pstudio", StringComparison.OrdinalIgnoreCase)) { if (ConfirmDiscard()) try { LoadFile(f); } catch (Exception ex) { ShowError(ex); } return; }
                list.Add(f);
            }
            if (list.Count == 0) return;
            string ff = host.GetFfmpeg();
            SetStatus(Lang.T("Importing…")); Cursor = Cursors.WaitCursor;
            int rate = tl.Rate;
            Task.Run(() =>
            {
                var loaded = new List<KeyValuePair<string, AudioImport.Result>>();
                var failed = new List<string>();
                foreach (var f in list)
                {
                    try { loaded.Add(new KeyValuePair<string, AudioImport.Result>(f, AudioImport.LoadStereo(f, rate, ff))); }
                    catch (Exception ex) { failed.Add(Path.GetFileName(f) + ": " + ex.Message.Split('\n')[0]); }
                }
                UI(() =>
                {
                    Cursor = Cursors.Default;
                    int newTracks = 0;
                    if (loaded.Count > 0)
                        Change(() =>
                        {
                            long p = pos;
                            foreach (var kv in loaded)
                            {
                                string name = Path.GetFileNameWithoutExtension(kv.Key);
                                var t = target;
                                if (t == null) { t = new Track { Name = name, Duck = true }; tl.P.Tracks.Add(t); newTracks++; }
                                var c = Clip.FromStereo(kv.Value.Left, kv.Value.Right, target == null ? pos : p, name, "audio");
                                t.Clips.Add(c); tl.SelClip = c; tl.SelTrack = t;
                                p = c.End;
                            }
                        });
                    if (failed.Count > 0) MessageBox.Show(this, Lang.T("Some files couldn't be imported:") + "\n\n" + string.Join("\n", failed), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else SetStatus(string.Format(Lang.T("Imported {0} file(s)."), loaded.Count) + (newTracks > 0 ? " " + Lang.T("New tracks duck automatically under speech (the D button) — click D to turn it off.") : ""));
                });
            });
        }

        // ================================================================== transport
        volatile Project playSnap;      // what the audio thread mixes from (refreshed after every edit)
        WaveStreamPlayer player;
        long pendingSeekAt = -1;
        DateTime lastSeek;
        Stopwatch recClock;
        bool recWithPlayback;

        void TogglePlay()
        {
            if (rec != null) { ToggleRecord(); return; }
            if (playCts != null) { StopPlayback(); return; }
            if (tl.Playhead >= tl.P.Length) tl.Playhead = 0;
            StartPlayback();
        }

        void StartPlayback()
        {
            long from = tl.Playhead, to = tl.P.Length;
            if (to <= from) { SetStatus(Lang.T("Nothing to play here.")); return; }
            StartMixPlayback(from, to);
            bPlay.Text = Lang.T("❚❚ Pause");
        }

        void OnSeek(long s)
        {
            if (playCts != null && rec == null)
            {
                StopPlayback();
                pendingSeekAt = s; lastSeek = DateTime.Now;   // restarts from the new spot once you stop dragging
            }
            else if (pendingSeekAt >= 0) { pendingSeekAt = s; lastSeek = DateTime.Now; }
            UpdateTime();
        }

        // Streams the mix in small pieces: starts at once, uses little memory, and edits are heard while playing.
        void StartMixPlayback(long from, long to)
        {
            var cts = new CancellationTokenSource();
            int outDevice = cboOut.SelectedIndex - 1;
            playSnap = tl.P.Clone();
            playCts = cts; playFrom = from; playClock = Stopwatch.StartNew();
            tl.Playing = true;
            int rate = tl.Rate;
            var pl = WaveOut.Available ? new WaveStreamPlayer(outDevice, rate, 2) : null;
            player = pl;
            var th = new Thread(() =>
            {
                try
                {
                    if (pl != null)
                    {
                        long pos = from;
                        List<long[]> ranges = null; Project rangesFor = null;
                        pl.Run(frames =>
                        {
                            var snap = playSnap;
                            long stop = snap.Length;
                            if (pos >= stop) return null;
                            int n = (int)Math.Min(frames, stop - pos);
                            if (snap != rangesFor) { ranges = snap.DuckRanges(); rangesFor = snap; }
                            var chunk = snap.MixStereo(pos, n, null, ranges);
                            pos += n;
                            return chunk;
                        }, cts.Token);
                    }
                    else
                    {
                        // test environments without Windows audio
                        var end = DateTime.Now.AddSeconds((to - from) / (double)rate);
                        while (DateTime.Now < end && !cts.IsCancellationRequested) Thread.Sleep(20);
                    }
                }
                catch (Exception ex) { UI(() => ShowError(ex)); }
                UI(() => { if (playCts == cts && rec == null) StopPlayback(); });
            }) { IsBackground = true, Name = "TracksPlayback" };
            th.Start();
        }

        void StopPlayback()
        {
            if (playCts != null) { playCts.Cancel(); playCts = null; }
            tl.Playing = false;
            bPlay.Text = Lang.T("▶ Play");
            tl.Invalidate();
        }

        void StopAll()
        {
            pendingSeekAt = -1;
            if (rec != null) FinishRecording();
            StopPlayback();
        }

        void Tick()
        {
            if (rec != null)
            {
                tl.RecLength = (long)(recClock.Elapsed.TotalSeconds * tl.Rate);
                tl.Playhead = tl.RecStart + tl.RecLength;
                int bars = (int)(rec.Level * 20);
                lblLevel.Text = "REC " + new string('█', bars) + new string('·', 20 - bars);
            }
            else if (playCts != null)
            {
                var pl = player;
                long played = pl == null ? (long)(playClock.Elapsed.TotalSeconds * tl.Rate) : (pl.Started ? pl.PlayedFrames : 0);
                tl.Playhead = Math.Min(playFrom + played, Math.Max(playFrom, tl.P.Length));
            }
            if (pendingSeekAt >= 0 && rec == null && (DateTime.Now - lastSeek).TotalMilliseconds > 150 && (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                tl.Playhead = pendingSeekAt; pendingSeekAt = -1;
                StartPlayback();
            }
            if (playCts != null || rec != null) { tl.EnsureVisible(tl.Playhead); tl.Invalidate(); UpdateTime(); }
        }

        void UpdateTime()
        {
            lblTime.Text = TimelineView.Time(tl.Playhead, tl.Rate) + " / " + TimelineView.Time(tl.P.Length, tl.Rate);
        }

        void ToggleRecord()
        {
            if (rec != null) { FinishRecording(); return; }
            if (!WaveOut.Available) { MessageBox.Show(this, Lang.T("Recording needs Windows audio."), "Piper Studio"); return; }
            pendingSeekAt = -1;
            StopPlayback();
            // record onto the selected track if it's free from the playhead on; otherwise a new track
            var t = tl.SelTrack;
            if (t == null || t.Clips.Any(c => c.End > tl.Playhead))
            {
                recCount++;
                t = new Track { Name = Lang.T("My voice") + (recCount > 1 ? " " + recCount : "") };
                hist.Push(tl.P);
                tl.P.Tracks.Add(t);
            }
            tl.SelTrack = t;
            try
            {
                rec = new Recorder();
                rec.Start(cboMic.SelectedIndex - 1, tl.Rate);
            }
            catch (Exception ex) { rec = null; ShowError(ex); return; }
            tl.RecTrack = t; tl.RecStart = tl.Playhead; tl.RecLength = 0;
            recClock = Stopwatch.StartNew();
            recWithPlayback = false;
            if (chkPlayWhileRec.Checked && tl.P.Length > tl.Playhead)
            {
                StartMixPlayback(tl.Playhead, tl.P.Length);
                recWithPlayback = true;
            }
            bRec.Text = Lang.T("■ Stop recording");
            SetStatus(Lang.T("Recording… press R, Space or the button to stop."));
            tl.UpdateScroll(); tl.Invalidate();
        }

        void FinishRecording()
        {
            var r = rec; rec = null;
            short[] data;
            try { data = r.Stop(); } catch (Exception ex) { ShowError(ex); data = new short[0]; }
            // Line the recording up with what you heard: remove the time between starting the microphone and the
            // first sound, plus the measured speaker + microphone delay (Recording delay box).
            long shift = 0;
            var pl = player;
            if (recWithPlayback && pl != null && pl.Started)
            {
                shift = (long)((pl.FirstWriteTicks - r.StartTicks) / (double)Stopwatch.Frequency * tl.Rate);
                shift += (long)((double)numLatency.Value / 1000.0 * tl.Rate);
            }
            StopPlayback();
            var t = tl.RecTrack; long start = tl.RecStart - Math.Max(0, shift);
            tl.RecStart = -1; tl.RecTrack = null; lblLevel.Text = "";
            bRec.Text = Lang.T("● Record");
            if (data.Length < tl.Rate / 10) { SetStatus(Lang.T("Recording was too short.")); tl.Invalidate(); return; }
            hist.Push(tl.P);
            var c = Clip.FromData(data, start, Lang.T("Recording") + " " + DateTime.Now.ToString("HH:mm:ss"), "recording");
            if (c.Start < 0) { c.In = Math.Min(c.Out - 1, -c.Start); c.Start = 0; }
            t.Clips.Add(c); tl.SelClip = c;
            MarkModified();
            SetStatus(string.Format(Lang.T("Recorded {0:0.0} s."), data.Length / (double)tl.Rate) + (shift > 0 ? " " + string.Format(Lang.T("(moved {0} ms earlier to line up with the music)"), shift * 1000 / tl.Rate) : ""));
        }

        // Plays beeps and listens with the microphone to measure how late recordings arrive.
        void CalibrateLatency()
        {
            if (!WaveOut.Available) { MessageBox.Show(this, Lang.T("Recording needs Windows audio."), "Piper Studio"); return; }
            if (MessageBox.Show(this, Lang.T("This measures the delay between your speakers and your microphone, so recordings line up with the music.\n\n1. Take off your headphones and put them right next to the microphone (or turn your speakers up).\n2. Keep the room quiet.\n3. Click OK — you'll hear 6 short beeps."),
                Lang.T("Measure recording delay"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            StopAll();
            Cursor = Cursors.WaitCursor;
            SetStatus(Lang.T("Measuring… please stay quiet."));
            int outDev = cboOut.SelectedIndex - 1, micDev = cboMic.SelectedIndex - 1, rate = tl.Rate;
            Task.Run(() =>
            {
                try
                {
                    double[] times;
                    var beeps = Latency.MakeBeeps(rate, out times);
                    var r = new Recorder();
                    r.Start(micDev, rate);
                    Thread.Sleep(150);
                    var pl = new WaveStreamPlayer(outDev, rate, 2);
                    int pos = 0;
                    pl.Run(frames =>
                    {
                        if (pos >= beeps.Length) return null;
                        int n = Math.Min(frames, beeps.Length - pos);
                        var chunk = new short[n * 2];
                        for (int i = 0; i < n; i++) chunk[2 * i] = chunk[2 * i + 1] = beeps[pos + i];
                        pos += n;
                        return chunk;
                    }, CancellationToken.None);
                    Thread.Sleep(800);
                    var data = r.Stop();
                    double playStart = (pl.FirstWriteTicks - r.StartTicks) / (double)Stopwatch.Frequency;
                    int matched;
                    double ms = Latency.Detect(data, rate, times, playStart, out matched);
                    UI(() =>
                    {
                        Cursor = Cursors.Default;
                        if (double.IsNaN(ms))
                        {
                            SetStatus(Lang.T("Couldn't hear the beeps."));
                            MessageBox.Show(this, Lang.T("The microphone didn't pick up the beeps clearly. Turn the speakers up or move the headphones closer to the microphone, and try again.\n\nYou can also type the delay yourself."), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        int v = (int)Math.Round(Math.Min(1000, ms));
                        numLatency.Value = v;
                        SetStatus(string.Format(Lang.T("Recording delay measured: {0} ms ({1} of 6 beeps heard)."), v, matched));
                        MessageBox.Show(this, string.Format(Lang.T("Measured delay: {0} ms.\n\nFrom now on, recordings made while other tracks play are moved {0} ms earlier so they line up."), v), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex) { UI(() => { Cursor = Cursors.Default; ShowError(ex); }); }
            });
        }

        // ================================================================== export
        bool exporting;

        void ExportMix()
        {
            if (tl.P.Length == 0) { SetStatus(Lang.T("The project is empty.")); return; }
            ExportProject(tl.P.Clone(), null, projectPath != null ? Path.GetFileNameWithoutExtension(projectPath) : "mix", miSubs.Checked);
        }

        void ExportTrack(Track t)
        {
            int i = tl.P.Tracks.IndexOf(t);
            if (i < 0 || t.Clips.Count == 0) return;
            var p = tl.P.Clone();
            ExportProject(p, p.Tracks[i], t.Name, false);
        }

        void ExportClip(Clip c)
        {
            var p = new Project { Rate = tl.Rate };
            var t = new Track(); var k = c.Clone(); k.Start = 0; t.Clips.Add(k); p.Tracks.Add(t);
            ExportProject(p, null, c.Name, false);
        }

        void ExportProject(Project p, Track only, string name, bool withSubs)
        {
            if (exporting) { SetStatus(Lang.T("Still exporting…")); return; }
            string path;
            using (var d = new SaveFileDialog { Filter = "MP3|*.mp3|WAV|*.wav|OGG|*.ogg|FLAC|*.flac|M4A|*.m4a", FileName = SafeName(name), InitialDirectory = SafeOutDir() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }
            string ff = host.GetFfmpeg();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".wav" && ff == null) { ShowError(new Exception(Lang.T("This format needs FFmpeg. Set ffmpeg.exe on the Export tab of the main window, or export as WAV."))); return; }
            exporting = true; Cursor = Cursors.WaitCursor;
            SetStatus(Lang.T("Exporting…"));
            Task.Run(() =>
            {
                try
                {
                    bool stereo = only == null ? p.HasStereo() : (Math.Abs(only.Pan) > 0.001f || only.Clips.Any(k => k.DataR != null));
                    string wav = ext == ".wav" ? path : Path.Combine(Path.GetTempPath(), "piperstudio_" + Guid.NewGuid().ToString("N") + ".wav");
                    int lastPct = -1;
                    p.MixToWav(wav, 0, p.Length, only, f => { int pct = (int)(f * 100); if (pct != lastPct) { lastPct = pct; UI(() => SetStatus(Lang.T("Exporting…") + " " + pct + "%")); } }, CancellationToken.None);
                    if (ext != ".wav")
                    {
                        try { Encode(ff, wav, path, stereo); }
                        finally { try { File.Delete(wav); } catch { } }
                    }
                    string srt = null;
                    if (withSubs)
                    {
                        var cues = Subtitles.MakeCues(p.SubtitleEntries().Select(x => new Subtitles.Cue { Start = x.Start / (double)p.Rate, End = x.End / (double)p.Rate, Text = x.Text }));
                        if (cues.Count > 0) { srt = Path.ChangeExtension(path, ".srt"); Subtitles.Write(srt, cues); }
                    }
                    UI(() =>
                    {
                        exporting = false; Cursor = Cursors.Default;
                        SetStatus(Lang.T("Exported ") + path + (srt != null ? "  " + Lang.T("(+ subtitles)") : ""));
                        Log("Exported " + path + (srt != null ? " + " + srt : ""));
                        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
                    });
                }
                catch (Exception ex) { UI(() => { exporting = false; Cursor = Cursors.Default; ShowError(ex); }); }
            });
        }

        void ExportSubtitles()
        {
            var entries = tl.P.SubtitleEntries();
            if (entries.Count == 0) { MessageBox.Show(this, Lang.T("There are no speech clips to make subtitles from. Subtitles come from the text of speech clips (added with the Speech box)."), "Piper Studio"); return; }
            using (var d = new SaveFileDialog { Filter = Lang.T("Subtitles") + " (*.srt)|*.srt", FileName = SafeName(projectPath != null ? Path.GetFileNameWithoutExtension(projectPath) : "subtitles") + ".srt", InitialDirectory = SafeOutDir() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var cues = Subtitles.MakeCues(entries.Select(x => new Subtitles.Cue { Start = x.Start / (double)tl.Rate, End = x.End / (double)tl.Rate, Text = x.Text }));
                    Subtitles.Write(d.FileName, cues);
                    SetStatus(string.Format(Lang.T("Saved {0} subtitle lines to {1}"), cues.Count, d.FileName));
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        void ExportTracks()
        {
            if (tl.P.Length == 0) { SetStatus(Lang.T("The project is empty.")); return; }
            if (exporting) { SetStatus(Lang.T("Still exporting…")); return; }
            string folder;
            using (var d = new FolderBrowserDialog { SelectedPath = SafeOutDir(), ShowNewFolderButton = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                folder = d.SelectedPath;
            }
            var p = tl.P.Clone();
            exporting = true; Cursor = Cursors.WaitCursor;
            Task.Run(() =>
            {
                try
                {
                    long len = p.Length;
                    int n = 0;
                    foreach (var t in p.Tracks)
                    {
                        if (t.Clips.Count == 0) continue;
                        n++;
                        int nn = n;
                        UI(() => SetStatus(Lang.T("Exporting…") + " " + Lang.T("Track") + " " + nn));
                        p.MixToWav(Path.Combine(folder, n.ToString("00") + " " + SafeName(t.Name) + ".wav"), 0, len, t, null, CancellationToken.None);
                    }
                    UI(() =>
                    {
                        exporting = false; Cursor = Cursors.Default;
                        SetStatus(string.Format(Lang.T("Exported {0} track(s) as WAV to {1}"), n, folder));
                        try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
                    });
                }
                catch (Exception ex) { UI(() => { exporting = false; Cursor = Cursors.Default; ShowError(ex); }); }
            });
        }

        static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = s.Trim().TrimEnd('.');
            return s.Length == 0 ? "audio" : (s.Length > 60 ? s.Substring(0, 60) : s);
        }

        static void Encode(string ff, string wav, string path, bool stereo)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string codec = ext == ".mp3" ? "-c:a libmp3lame -b:a 192k" : ext == ".ogg" ? "-c:a libvorbis -q:a 6" : ext == ".flac" ? "-c:a flac" : "-c:a aac -b:a 192k";
            var psi = new ProcessStartInfo(ff, string.Format("-y -hide_banner -loglevel error -i \"{0}\" {1}{2} \"{3}\"", wav, stereo ? "" : "-ac 1 ", codec, path)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var pr = Process.Start(psi))
            {
                string err = pr.StandardError.ReadToEnd();
                pr.WaitForExit();
                if (pr.ExitCode != 0) throw new Exception("FFmpeg: " + err);
            }
        }

        // ================================================================== keys
        void OnKey(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox || ActiveControl is ComboBox || ActiveControl is NumericUpDown) return;
            switch (e.KeyCode)
            {
                case Keys.Space: TogglePlay(); break;
                case Keys.R: ToggleRecord(); break;
                case Keys.S: if (!e.Control) SplitAtPlayhead(); else return; break;
                case Keys.Delete: case Keys.Back: DeleteClip(); break;
                case Keys.Enter: if (tl.SelClip != null) EditClip(tl.SelClip); break;
                case Keys.Home: StopPlayback(); tl.Playhead = 0; tl.EnsureVisible(0); tl.Invalidate(); UpdateTime(); break;
                case Keys.End: StopPlayback(); tl.Playhead = tl.P.Length; tl.EnsureVisible(tl.Playhead); tl.Invalidate(); UpdateTime(); break;
                case Keys.Left: case Keys.Right:
                    if (tl.SelClip != null)
                    {
                        long step = tl.Rate / (e.Shift ? 1 : 20) * (e.KeyCode == Keys.Left ? -1 : 1);
                        Change(() => tl.SelClip.Start = Math.Max(0, tl.SelClip.Start + step));
                    }
                    break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }
}
