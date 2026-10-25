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
    // Guided recording studio for voice banking: one sentence at a time, with quality checks.
    public class VoiceBankForm : Form
    {
        VoiceBank vb;
        int index;
        Recorder rec;
        bool recording;
        DateTime recStart;
        short[] lastTake;
        CancellationTokenSource playCts;
        readonly Func<string, string> getSetting;
        readonly Action<string, string> setSetting;

        readonly Label lblProject = new Label { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 8, 12, 3) };
        readonly Label lblNum = new Label { AutoSize = true, Font = new Font("Segoe UI", 10f), Margin = new Padding(3, 6, 3, 3) };
        readonly Label lblSentence = new Label { Dock = DockStyle.Fill, Font = new Font("Tahoma", 22f), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(20) };
        readonly ProgressBar level = new ProgressBar { Width = 260, Height = 16, Maximum = 100, Margin = new Padding(3, 10, 3, 3) };
        readonly Label lblQuality = new Label { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(3, 8, 3, 3) };
        readonly Label lblProgress = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3) };
        readonly ComboBox cboMic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckBox chkAuto = new CheckBox { Text = "Go to the next sentence after a good recording", AutoSize = true, Checked = true, Margin = new Padding(12, 8, 3, 3) };
        Button bRec, bPlay, bPrev, bNext, bDel;
        readonly System.Windows.Forms.Timer meter = new System.Windows.Forms.Timer { Interval = 50 };
        readonly bool fakeMic = Environment.GetEnvironmentVariable("PS_FAKE_MIC") == "1";   // test machines without a microphone

        public VoiceBankForm(Func<string, string> getSetting, Action<string, string> setSetting)
        {
            this.getSetting = getSetting; this.setSetting = setSetting;
            Text = "Voice banking — record your own voice";
            Font = new Font("Segoe UI", 9.5f);
            Size = new Size(1000, 640); MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += (s, e) => { if (HandleKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; } };   // in case a control swallowed it
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 6, 6, 2) };
            top.Controls.Add(Btn("New voice bank…", (s, e) => NewBank()));
            top.Controls.Add(Btn("Open…", (s, e) => OpenBank()));
            top.Controls.Add(Btn("Add my own sentences…", (s, e) => AddSentences()));
            top.Controls.Add(Btn("Export for training…", (s, e) => Export()));
            top.Controls.Add(Btn("Open folder", (s, e) => { if (vb != null) try { Process.Start("explorer.exe", "\"" + vb.Folder + "\""); } catch { } }));
            top.Controls.Add(Btn("How it works", (s, e) => ShowHelp()));

            var mics = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 0, 6, 2) };
            mics.Controls.Add(new Label { Text = "Microphone:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            mics.Controls.Add(cboMic);
            mics.Controls.Add(chkAuto);
            mics.Controls.Add(lblProject);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(6, 4, 6, 8) };
            bRec = Big("●  Record   (Space)", Color.FromArgb(170, 40, 40), (s, e) => ToggleRecord());
            bPlay = Big("▶  Listen   (P)", Color.FromArgb(40, 90, 160), (s, e) => PlayTake());
            bPrev = Big("◀  Previous", Color.FromArgb(90, 90, 90), (s, e) => Go(index - 1));
            bNext = Big("Next  ▶", Color.FromArgb(90, 90, 90), (s, e) => Go(index + 1));
            bDel = Big("Delete recording", Color.FromArgb(120, 70, 50), (s, e) => DeleteTake());
            bottom.Controls.AddRange(new Control[] { bRec, bPlay, bPrev, bNext, bDel, level });
            var info = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(6, 0, 6, 0) };
            info.Controls.Add(lblNum); info.Controls.Add(lblQuality);
            var prog = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(6, 0, 6, 0) };
            prog.Controls.Add(lblProgress);
            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            center.Controls.Add(lblSentence);
            Controls.Add(center); Controls.Add(info); Controls.Add(prog); Controls.Add(bottom); Controls.Add(mics); Controls.Add(top);

            cboMic.Items.Add(Lang.T("Windows default microphone"));
            try { if (WaveOut.Available) foreach (var d in Recorder.Devices()) cboMic.Items.Add(d); } catch { }
            string mic = getSetting != null ? getSetting("vb_mic") : null;
            cboMic.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(mic)) for (int i = 0; i < cboMic.Items.Count; i++) if (cboMic.Items[i].ToString() == mic) cboMic.SelectedIndex = i;
            if (getSetting != null && getSetting("vb_auto") == "0") chkAuto.Checked = false;
            meter.Tick += (s, e) => { var r = rec; if (r != null) level.Value = Math.Min(100, (int)(r.Level * 100)); else if (!fakeMic) level.Value = 0; };
            FormClosing += (s, e) =>
            {
                if (recording) StopRecord(false);
                if (playCts != null) playCts.Cancel();
                if (setSetting != null) { setSetting("vb_mic", cboMic.SelectedItem != null ? cboMic.SelectedItem.ToString() : ""); setSetting("vb_auto", chkAuto.Checked ? "1" : "0"); if (vb != null) { setSetting("vb_last", vb.Folder); setSetting("vb_index", index.ToString()); } }
            };
            Lang.Apply(this); PersianUi.Apply(this);
            if (Lang.Fa) RightToLeft = RightToLeft.Yes;

            string last = getSetting != null ? getSetting("vb_last") : null;
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                try { Load(VoiceBank.Open(last)); int ix; if (int.TryParse(getSetting("vb_index") ?? "", out ix)) Go(ix); } catch { }
            if (vb == null) ShowNoProject();
        }

        static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3) };
            b.Click += click; return b;
        }

        static Button Big(string text, Color c, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(3), BackColor = c, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Cursor = Cursors.Hand };
            b.Click += click; return b;
        }

        void ShowNoProject()
        {
            lblSentence.Text = Lang.T("Voice banking saves your voice so a Piper voice can be made that sounds like you.\n\nClick \"New voice bank…\" to start. You'll read sentences aloud, one at a time.");
            lblSentence.Font = new Font("Segoe UI", 14f);
            lblSentence.RightToLeft = Lang.Fa ? RightToLeft.Yes : RightToLeft.No;
            foreach (var b in new[] { bRec, bPlay, bPrev, bNext, bDel }) b.Enabled = false;
            lblNum.Text = ""; lblQuality.Text = ""; lblProgress.Text = ""; lblProject.Text = "";
        }

        void Load(VoiceBank b)
        {
            vb = b; index = 0;
            lblSentence.Font = new Font("Tahoma", 22f);
            foreach (var x in new[] { bRec, bPrev, bNext }) x.Enabled = true;
            // start at the first sentence without a recording
            for (int i = 0; i < vb.Sentences.Count; i++) if (!vb.Has(i)) { index = i; break; }
            Show(index);
        }

        void Show(int i)
        {
            if (vb == null || vb.Sentences.Count == 0) { ShowNoProject(); return; }
            index = Math.Max(0, Math.Min(vb.Sentences.Count - 1, i));
            string s = vb.Sentences[index];
            lblSentence.RightToLeft = TextTools.StartsRtl(s) ? RightToLeft.Yes : RightToLeft.No;
            lblSentence.Text = s;
            bool has = vb.Has(index);
            lblNum.Text = string.Format(Lang.T("Sentence {0} of {1}"), index + 1, vb.Sentences.Count) + (has ? "   ✔ " + Lang.T("recorded") : "");
            lblNum.ForeColor = has ? Color.DarkGreen : SystemColors.ControlText;
            bPlay.Enabled = bDel.Enabled = has;
            bPrev.Enabled = index > 0; bNext.Enabled = index < vb.Sentences.Count - 1;
            lblProject.Text = vb.Name + "  ·  " + (vb.Lang == "fa" ? Lang.T("Persian") : Lang.T("English"));
            UpdateProgress();
            if (!has) { lblQuality.Text = Lang.T("Press Space (or Record), read the sentence naturally, then press Space again."); lblQuality.ForeColor = Color.DimGray; }
            else { lblQuality.Text = Lang.T("Already recorded. Press P to listen, or Space to record it again."); lblQuality.ForeColor = Color.DimGray; }
        }

        void UpdateProgress()
        {
            if (vb == null) return;
            int n = vb.RecordedCount;
            double min = vb.RecordedSeconds() / 60.0;
            double sec = vb.RecordedSeconds();
            lblProgress.Text = string.Format(Lang.T("{0} of {1} sentences recorded · {2} of speech (15–20 minutes is the minimum for training, 1 hour or more is better)"), n, vb.Sentences.Count,
                ((int)(sec / 60)) + ":" + ((int)(sec % 60)).ToString("00"));
        }

        void Go(int i) { if (recording) return; if (vb != null) Show(i); }

        // ---------------------------------------------------------------- record
        void ToggleRecord() { if (recording) StopRecord(true); else StartRecord(); }

        void StartRecord()
        {
            if (vb == null) return;
            if (playCts != null) playCts.Cancel();
            try
            {
                if (!fakeMic)
                {
                    rec = new Recorder();
                    int dev = cboMic.SelectedIndex - 1;   // -1 = Windows default
                    try { rec.Start(dev, VoiceBank.Rate); }
                    catch { rec = new Recorder(); rec.Start(dev, 44100); }
                }
                recording = true; recStart = DateTime.Now;
                meter.Start();
                bRec.Text = Lang.T("■  Stop   (Space)");
                bRec.BackColor = Color.FromArgb(200, 120, 0);
                lblQuality.Text = Lang.T("Recording… read the sentence, then press Space.");
                lblQuality.ForeColor = Color.Firebrick;
                foreach (var b in new[] { bPlay, bPrev, bNext, bDel }) b.Enabled = false;
            }
            catch (Exception ex)
            {
                rec = null;
                MessageBox.Show(this, Lang.T("The microphone could not be started: ") + ex.Message, "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void StopRecord(bool keep)
        {
            recording = false; meter.Stop(); level.Value = 0;
            bRec.Text = Lang.T("●  Record   (Space)");
            bRec.BackColor = Color.FromArgb(170, 40, 40);
            short[] pcm; int rate;
            if (fakeMic) { pcm = FakeSpeech((DateTime.Now - recStart).TotalSeconds); rate = VoiceBank.Rate; }
            else
            {
                var r = rec; rec = null;
                pcm = r != null ? r.Stop() : new short[0];
                rate = r != null ? r.Rate : VoiceBank.Rate;
            }
            if (!keep) { Show(index); return; }
            if (rate != VoiceBank.Rate) pcm = Downsample(pcm, rate, VoiceBank.Rate);
            TakeInfo info;
            var trimmed = TakeTools.TrimAndCheck(pcm, VoiceBank.Rate, out info);
            if (info.TooShort || (info.TooQuiet && info.Seconds == 0))
            {
                Show(index);
                lblQuality.Text = Lang.T("Nothing usable was heard — check the microphone and try again.") + "  (" + Lang.T(info.Problems) + ")";
                lblQuality.ForeColor = Color.Firebrick;
                return;
            }
            vb.SaveTake(index, trimmed);
            lastTake = trimmed;
            int was = index;
            bool good = info.Ok && !info.Noisy && !info.Cut;
            if (good && chkAuto.Checked && index < vb.Sentences.Count - 1) Show(index + 1); else Show(index);
            lblQuality.Text = string.Format(Lang.T("Sentence {0} saved: {1:0.0} s."), was + 1, info.Seconds) + " " +
                (good ? Lang.T("Sounds good.") : Lang.T("Check it: ") + TranslateProblems(info) + ". " + Lang.T("Press ◀ and Space to record it again if needed."));
            lblQuality.ForeColor = good ? Color.DarkGreen : Color.DarkOrange;
        }

        static string TranslateProblems(TakeInfo info)
        {
            var l = new List<string>();
            if (info.TooQuiet) l.Add(Lang.T("too quiet — move closer to the microphone"));
            if (info.Clipped) l.Add(Lang.T("too loud — move back a little"));
            if (info.Noisy) l.Add(Lang.T("background noise — find a quieter place"));
            if (info.Cut) l.Add(Lang.T("the end may be cut off — wait a moment before stopping"));
            return string.Join(", ", l);
        }

        // 44.1 kHz → 22.05 kHz with a low-pass filter, so the voice stays clean for training
        public static short[] Downsample(short[] s, int from, int to)
        {
            if (from == to * 2)
            {
                const int taps = 31; var h = new double[taps]; double sum = 0;
                for (int k = 0; k < taps; k++)
                {
                    int m = k - taps / 2;
                    double sinc = m == 0 ? 0.5 : Math.Sin(Math.PI * 0.5 * m) / (Math.PI * m);
                    h[k] = sinc * (0.54 - 0.46 * Math.Cos(2 * Math.PI * k / (taps - 1)));
                    sum += h[k];
                }
                var o = new short[s.Length / 2];
                for (int i = 0; i < o.Length; i++)
                {
                    double acc = 0; int c = 2 * i;
                    for (int k = 0; k < taps; k++) { int j = c + k - taps / 2; if (j >= 0 && j < s.Length) acc += s[j] * h[k]; }
                    acc /= sum;
                    o[i] = (short)Math.Max(-32768, Math.Min(32767, acc));
                }
                return o;
            }
            return Audio.Resample(s, from, to);
        }

        static short[] FakeSpeech(double secs)
        {
            int n = (int)(Math.Max(0.3, secs) * VoiceBank.Rate);
            var s = new short[n]; var rnd = new Random(1);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)VoiceBank.Rate;
                bool talk = t > 0.4 && t < secs - 0.3;
                double env = talk ? 0.5 + 0.5 * Math.Sin(2 * Math.PI * 3 * t) : 0;
                s[i] = (short)(env * 9000 * Math.Sin(2 * Math.PI * 160 * t) + rnd.Next(-60, 60));
            }
            return s;
        }

        void PlayTake()
        {
            if (vb == null || !vb.Has(index)) return;
            if (playCts != null) { playCts.Cancel(); playCts = null; return; }
            short[] pcm;
            try { pcm = Audio.ReadWavPcm(vb.WavPath(index)); } catch { return; }
            var cts = playCts = new CancellationTokenSource();
            Task.Run(() =>
            {
                try { if (WaveOut.Available) WaveOut.PlaySync(Audio.Resample(pcm, VoiceBank.Rate, 48000), 48000, new List<int> { -1 }, cts.Token); else Thread.Sleep(pcm.Length * 1000 / VoiceBank.Rate); }
                catch { }
                BeginInvoke(new Action(() => { if (playCts == cts) playCts = null; }));
            });
        }

        void DeleteTake()
        {
            if (vb == null || !vb.Has(index)) return;
            if (MessageBox.Show(this, Lang.T("Delete the recording of this sentence?"), "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(vb.WavPath(index)); } catch { }
            Show(index);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        bool HandleKey(Keys keyData)
        {
            if (vb != null && !(ActiveControl is ComboBox && ((ComboBox)ActiveControl).DroppedDown))
            {
                switch (keyData)
                {
                    case Keys.Space: ToggleRecord(); return true;
                    case Keys.P: if (!recording) PlayTake(); return true;
                    case Keys.Right: case Keys.PageDown: Go(Lang.Fa ? index - 1 : index + 1); return true;
                    case Keys.Left: case Keys.PageUp: Go(Lang.Fa ? index + 1 : index - 1); return true;
                    case Keys.Escape: if (recording) { StopRecord(false); return true; } break;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- projects
        void NewBank()
        {
            using (var f = new Form { Text = Lang.T("New voice bank"), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Font = Font })
            {
                var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
                t.Controls.Add(new Label { Text = "Name (for example your first name):", AutoSize = true });
                var name = new TextBox { Width = 320, Text = Environment.UserName };
                t.Controls.Add(name);
                t.Controls.Add(new Label { Text = "Which language will you record?", AutoSize = true, Margin = new Padding(3, 10, 3, 3) });
                var fa = new RadioButton { Text = Lang.T("Persian") + " — " + string.Format(Lang.T("{0} sentences"), VoiceBank.BuiltIn("fa").Count), AutoSize = true, Checked = Lang.Fa };
                var en = new RadioButton { Text = Lang.T("English") + " — " + string.Format(Lang.T("{0} sentences"), VoiceBank.BuiltIn("en").Count), AutoSize = true, Checked = !Lang.Fa };
                t.Controls.Add(fa); t.Controls.Add(en);
                t.Controls.Add(new Label { Text = "You can add your own sentences later (things you often say are great).", AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3) });
                var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var row = new FlowLayoutPanel { AutoSize = true }; row.Controls.Add(ok); row.Controls.Add(cancel);
                t.Controls.Add(row);
                f.Controls.Add(t); f.AcceptButton = ok; f.CancelButton = cancel;
                Lang.Apply(f); PersianUi.Apply(f);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string n = name.Text.Trim(); if (n == "") n = "My voice";
                string lang = fa.Checked ? "fa" : "en";
                string safe = new string(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                string folder = Path.Combine(VoiceBank.DefaultRoot, safe + " (" + (lang == "fa" ? "Persian" : "English") + ")");
                for (int k = 2; Directory.Exists(folder); k++) folder = Path.Combine(VoiceBank.DefaultRoot, safe + " (" + (lang == "fa" ? "Persian" : "English") + ") " + k);
                try { Load(VoiceBank.Create(folder, n, lang, VoiceBank.BuiltIn(lang))); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Piper Studio"); }
            }
        }

        void OpenBank()
        {
            using (var d = new FolderBrowserDialog { Description = Lang.T("Choose a voice bank folder (it has script.txt and a wavs folder):"), SelectedPath = Directory.Exists(VoiceBank.DefaultRoot) ? VoiceBank.DefaultRoot : "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { Load(VoiceBank.Open(d.SelectedPath)); }
                catch (Exception ex) { MessageBox.Show(this, Lang.T(ex.Message), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void AddSentences()
        {
            if (vb == null) { NewBank(); if (vb == null) return; }
            string t = Dialogs.Prompt(this, Lang.T("Add my own sentences"), Lang.T("One sentence per line — things you often say are the most useful:"), "", true);
            if (string.IsNullOrWhiteSpace(t)) return;
            int n = vb.AddSentences(t.Replace("\r", "").Split('\n'));
            UpdateProgress();
            Show(index);
            lblQuality.Text = string.Format(Lang.T("{0} sentences added at the end."), n);
            lblQuality.ForeColor = Color.DarkGreen;
        }

        void Export()
        {
            if (vb == null) return;
            if (vb.RecordedCount == 0) { MessageBox.Show(this, Lang.T("Record some sentences first."), "Piper Studio"); return; }
            using (var d = new SaveFileDialog { Filter = "Zip (*.zip)|*.zip", FileName = vb.SafeName + "-voicebank.zip", InitialDirectory = vb.Folder })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    vb.Export(d.FileName);
                    Cursor = Cursors.Default;
                    double min = vb.RecordedSeconds() / 60;
                    MessageBox.Show(this, string.Format(Lang.T("Saved {0} recordings ({1:0.0} minutes) to\n{2}\n\nInside is TRAINING.txt: how to turn it into a Piper voice with a graphics card or free Google Colab.{3}"),
                        vb.RecordedCount, min, d.FileName, min < 15 ? "\n\n" + Lang.T("Tip: 15–20 minutes is the minimum for a usable voice — keep recording when you can.") : ""), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void ShowHelp()
        {
            MessageBox.Show(this, Lang.Fa ? Lang.T("VOICE_BANK_HELP") : HelpEn, Lang.T("Voice banking"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public const string HelpEn =
            "Voice banking records your voice while you can still speak, so a Piper voice can be made that sounds like you — " +
            "for example if an illness like ALS is affecting your speech.\n\n" +
            "1. New voice bank → choose the language.\n" +
            "2. Read each sentence naturally: Space to start, Space to stop. P listens, ◀ ▶ (or arrow keys) move between sentences.\n" +
            "3. Use the same microphone, room and distance every time. A quiet room and a headset microphone work best. Take breaks — it's fine to record a few sentences a day.\n" +
            "4. Record at least 15–20 minutes of speech; an hour or more gives a much better voice. Add your own sentences — the words you use most.\n" +
            "5. Export for training makes a zip with your recordings and instructions. Training needs a strong NVIDIA graphics card or free Google Colab, and takes hours. " +
            "The result is a normal Piper voice: put it in the voices folder and it appears in Piper Studio.\n\n" +
            "Keep the zip safe even if you don't train it now — it is your voice.";
    }
}
