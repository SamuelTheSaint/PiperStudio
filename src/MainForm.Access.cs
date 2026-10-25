using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PiperStudio
{
    public partial class MainForm
    {
        // ================================================================== word prediction + accessibility
        readonly WordPredictor predictor = new WordPredictor();
        FlowLayoutPanel predBar;
        TextBox predTarget;               // the box the suggestions belong to (Say now or the main text)
        List<string> predWords = new List<string>();
        CheckBox chkPredict, chkLearn, chkBig, chkContrast, chkFKeys;
        Label lblLearned;
        int learnCount;
        TabControl tabsControl;
        string WordsPath { get { return AppPaths.In("words.txt"); } }

        TabPage AccessTab()
        {
            var g = Grid();
            AddFull(g, new Label { Text = "Word prediction", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 4, 3, 4) });
            chkPredict = new CheckBox { Text = "Suggest words while I type", AutoSize = true, Checked = true };
            chkPredict.CheckedChanged += (s, e) => UpdatePredictions();
            AddFull(g, chkPredict);
            AddFull(g, Note("Suggestions appear above the Say now box (and for the main text). Tab or Alt+1 takes the first one, Alt+2 … Alt+5 the others — or just click it."));
            chkLearn = new CheckBox { Text = "Learn from what I say", AutoSize = true, Checked = true };
            AddFull(g, chkLearn);
            AddFull(g, Note("Learns your own words and which word usually comes next, in Persian and English, from Say now, quick phrases and the bot. It stays on this PC."));
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            lblLearned = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 10, 3) };
            row.Controls.Add(lblLearned);
            row.Controls.Add(SmallButton("Forget learned words", (s, e) =>
            {
                if (MessageBox.Show(this, Lang.T("Forget all the words Piper Studio has learned from you?"), "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                predictor.Clear(); try { File.Delete(WordsPath); } catch { }
                UpdateLearnedLabel(); UpdatePredictions();
            }));
            AddFull(g, row);

            AddFull(g, new Label { Text = "Easier to see and use", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            chkBig = new CheckBox { Text = "Big text and buttons", AutoSize = true };
            chkContrast = new CheckBox { Text = "High contrast (black and yellow)", AutoSize = true };
            chkBig.CheckedChanged += (s, e) => ApplyAccess();
            chkContrast.CheckedChanged += (s, e) => ApplyAccess();
            AddFull(g, chkBig);
            AddFull(g, chkContrast);
            AddFull(g, Note("High contrast turns on by itself when Windows high contrast is on."));
            chkFKeys = new CheckBox { Text = "F1 … F12 speak quick phrases 1–12", AutoSize = true };
            chkFKeys.CheckedChanged += (s, e) => RebuildPhrases();
            AddFull(g, chkFKeys);
            AddFull(g, Note("One key per phrase, like a communication board. While this is on, Play is Ctrl+Enter and Diagnostics is in the Help menu."));

            var chkVazir = new CheckBox { Text = "Persian interface: Vazirmatn font", AutoSize = true, Checked = IniBool("vazir", true) };
            var chkRtl = new CheckBox { Text = "Persian interface: right-to-left layout", AutoSize = true, Checked = IniBool("rtl", true) };
            chkVazir.CheckedChanged += (s, e) => ini["vazir"] = chkVazir.Checked ? "1" : "0";
            chkRtl.CheckedChanged += (s, e) => ini["rtl"] = chkRtl.Checked ? "1" : "0";
            AddFull(g, chkVazir); AddFull(g, chkRtl);
            AddFull(g, Note("These two change after you restart Piper Studio."));

            AddFull(g, new Label { Text = "Keyboard only", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            AddFull(g, Note("F6 / Shift+F6: jump between Say now, quick phrases, the text and the tabs\n" +
                            "Tab / arrow keys: move between buttons · Enter or Space: press\n" +
                            "Menu key (or Shift+F10) on a phrase: edit it\n" +
                            "Ctrl+Enter: play the text · Esc: stop · Ctrl+1 … 9: quick phrases\n" +
                            "↑ / ↓ in Say now: what you said before"));
            AddFull(g, new Label { Text = "Voice banking", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            AddFull(g, Note("Losing your voice, or want to keep it? Record sentences in your own voice, so a Piper voice can be made that sounds like you."));
            AddFull(g, SmallButton("Open the voice banking studio…", (s, e) => OpenVoiceBank()));
            return Page("Access", g);
        }

        void UpdateLearnedLabel()
        {
            if (lblLearned != null) lblLearned.Text = string.Format(Lang.T("{0} words learned"), predictor.LearnedWords);
        }

        void LoadAccess()
        {
            predictor.Seed(WordPredictor.Words(WordPredictor.SeedEnglish));
            predictor.Seed(WordPredictor.Words(WordPredictor.SeedPersian));
            try { predictor.Load(WordsPath); } catch { }
            chkPredict.Checked = IniBool("predict", true);
            chkLearn.Checked = IniBool("learn", true);
            chkFKeys.Checked = IniBool("fkeys", false);
            chkBig.Checked = IniBool("big", false);
            chkContrast.Checked = ini.ContainsKey("contrast") ? IniBool("contrast", false) : SafeHighContrast();
            UpdateLearnedLabel();
            ApplyAccess();
        }

        static bool SafeHighContrast() { try { return SystemInformation.HighContrast; } catch { return false; } }

        void SaveAccess(Dictionary<string, string> d)
        {
            d["predict"] = chkPredict.Checked ? "1" : "0"; d["learn"] = chkLearn.Checked ? "1" : "0";
            d["fkeys"] = chkFKeys.Checked ? "1" : "0"; d["big"] = chkBig.Checked ? "1" : "0"; d["contrast"] = chkContrast.Checked ? "1" : "0";
            if (predictor.Changed && chkLearn.Checked) try { predictor.Save(WordsPath); } catch { }
        }

        // called for everything that gets spoken
        void LearnWords(string text, string source)
        {
            if (chkLearn == null || !chkLearn.Checked) return;
            if (source != "say" && source != "phrase" && source != "bot") return;   // not text read aloud from other apps
            predictor.Learn(text);
            UpdateLearnedLabel();
            if (++learnCount % 10 == 0) try { predictor.Save(WordsPath); } catch { }
        }

        // ---------------------------------------------------------------- suggestion bar
        Control BuildPredictionBar()
        {
            predBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, 2, 0, 0), Visible = false };
            return predBar;
        }

        void HookPrediction(TextBox box)
        {
            box.TextChanged += (s, e) => { predTarget = box; UpdatePredictions(); };
            box.KeyUp += (s, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) { predTarget = box; UpdatePredictions(); } };
            box.MouseUp += (s, e) => { predTarget = box; UpdatePredictions(); };
            box.Enter += (s, e) => { predTarget = box; UpdatePredictions(); };
            // Tab takes the first suggestion in Say now (it normally moves focus)
            box.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Tab && !e.Shift && box == txtQuick && predWords.Count > 0 && predBar.Visible) e.IsInputKey = true; };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Tab && !e.Shift && box == txtQuick && predWords.Count > 0 && predBar.Visible) { e.SuppressKeyPress = true; e.Handled = true; AcceptPrediction(0); }
            };
        }

        void UpdatePredictions()
        {
            if (predBar == null) return;
            var box = predTarget;
            if (chkPredict == null || !chkPredict.Checked || box == null || !box.Focused || box.SelectionLength > 0)
            {
                predBar.Visible = false; predWords.Clear();
                return;
            }
            string before = box.Text.Substring(0, Math.Min(box.SelectionStart, box.Text.Length));
            string prev, partial;
            WordPredictor.Context(before, out prev, out partial);
            // only predict a next word right after a space, not in the middle of punctuation
            bool afterSpace = before.Length > 0 && before[before.Length - 1] == ' ';
            predWords = (partial.Length > 0 || afterSpace) ? predictor.Suggest(prev, partial, 5) : new List<string>();
            predBar.SuspendLayout();
            predBar.Controls.Clear();
            for (int i = 0; i < predWords.Count; i++)
            {
                int k = i;
                var b = new Button { Text = predWords[i], AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                    Padding = new Padding(6, 1, 6, 1), Margin = new Padding(2), TabStop = false, Font = new Font("Tahoma", 11f), Cursor = Cursors.Hand,
                    BackColor = Color.FromArgb(245, 245, 232), AccessibleName = Lang.T("Suggestion") + " " + (i + 1) + ": " + predWords[i] };
                b.FlatAppearance.BorderColor = Color.FromArgb(200, 190, 140);
                if (TextTools.StartsRtl(predWords[i])) b.RightToLeft = RightToLeft.Yes;
                phraseTips.SetToolTip(b, (i == 0 ? "Tab / " : "") + "Alt+" + (i + 1));
                b.Click += (s, e) => AcceptPrediction(k);
                predBar.Controls.Add(b);
            }
            predBar.Visible = predWords.Count > 0;
            predBar.ResumeLayout();
            ApplyAccessTo(predBar);
        }

        void AcceptPrediction(int i)
        {
            var box = predTarget;
            if (box == null || i < 0 || i >= predWords.Count) return;
            int caret = Math.Min(box.SelectionStart, box.Text.Length);
            string before = box.Text.Substring(0, caret);
            int start = caret;
            while (start > 0 && WordPredictor.IsWordChar(before[start - 1])) start--;
            string after = box.Text.Substring(caret);
            // also replace the rest of the word if the caret is in the middle of one
            int end = 0;
            while (end < after.Length && WordPredictor.IsWordChar(after[end])) end++;
            string word = predWords[i] + " ";
            box.Text = box.Text.Substring(0, start) + word + after.Substring(end).TrimStart(' ');
            box.SelectionStart = start + word.Length;
            box.SelectionLength = 0;
            box.Focus();
            UpdatePredictions();
        }

        // ---------------------------------------------------------------- keyboard
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (chkFKeys != null && chkFKeys.Checked && mods == Keys.None && key >= Keys.F1 && key <= Keys.F12)
            {
                SpeakPhrase(key - Keys.F1);
                return true;
            }
            if (key == Keys.F6 && (mods == Keys.None || mods == Keys.Shift)) { CycleRegion(mods == Keys.Shift ? -1 : 1); return true; }
            if (mods == (Keys.Control | Keys.Shift) && key >= Keys.D1 && key <= Keys.D9) { ApplyPresetNumber(key - Keys.D1); return true; }
            if (key == Keys.Enter && mods == Keys.Control) { Play(); return true; }
            if (mods == Keys.Alt && key >= Keys.D1 && key <= Keys.D5 && predBar != null && predBar.Visible && predTarget != null && predTarget.Focused)
            {
                AcceptPrediction(key - Keys.D1);
                return true;
            }
            if ((key == Keys.Apps || (key == Keys.F10 && mods == Keys.Shift)) && ActiveControl != null)
            {
                var b = FindFocused(this) as Button;
                if (b != null && phraseButtons != null && phraseButtons.Contains(b))
                {
                    int i = phraseButtons.Controls.IndexOf(b);
                    if (i >= 0 && CurrentCat != null && i < CurrentCat.Items.Count) { ShowPhraseMenu(i, b); return true; }
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static Control FindFocused(Control c)
        {
            foreach (Control ch in c.Controls) { if (ch.Focused) return ch; var f = FindFocused(ch); if (f != null) return f; }
            return null;
        }

        void CycleRegion(int dir)
        {
            var regions = new List<Control> { txtQuick };
            if (phraseButtons != null && phraseButtons.Visible && phraseButtons.Controls.Count > 0) regions.Add(phraseButtons.Controls[0]);
            regions.Add(txt);
            if (tabsControl != null) regions.Add(tabsControl);
            var focused = FindFocused(this);
            int cur = -1;
            for (int i = 0; i < regions.Count; i++)
                if (regions[i] == focused || (i == 1 && phraseButtons != null && focused != null && phraseButtons.Contains(focused))) cur = i;
            int next = ((cur < 0 ? (dir > 0 ? -1 : 0) : cur) + dir + regions.Count) % regions.Count;
            regions[next].Focus();
            SetStatus(new[] { Lang.T("Say now"), Lang.T("Quick phrases"), Lang.T("Text"), Lang.T("Settings tabs") }[Math.Min(3, regions[next] == txt ? 2 : regions[next] == tabsControl ? 3 : regions[next] == txtQuick ? 0 : 1)]);
        }

        // ---------------------------------------------------------------- big text + high contrast
        readonly Dictionary<Control, Font> origFont = new Dictionary<Control, Font>();
        readonly Dictionary<Control, Tuple<Color, Color, FlatStyle, Color>> origColor = new Dictionary<Control, Tuple<Color, Color, FlatStyle, Color>>();
        bool BigOn { get { return chkBig != null && chkBig.Checked; } }
        bool ContrastOn { get { return chkContrast != null && chkContrast.Checked; } }

        void ApplyAccess()
        {
            if (chkBig == null) return;
            SuspendLayout();
            ApplyAccessTo(this);
            if (phraseButtons != null) phraseButtons.MaximumSize = new Size(0, BigOn ? 330 : 150);
            ResumeLayout(true);
        }

        void ApplyAccessTo(Control root)
        {
            if (chkBig == null) return;
            Walk(root);
        }

        void Walk(Control c)
        {
            var strip = c as ToolStrip;
            if (strip != null) { ThemeStrip(strip); return; }
            if (!origFont.ContainsKey(c)) { origFont[c] = c.Font; c.Disposed += (s, e) => { origFont.Remove(c); origColor.Remove(c); }; }
            var f0 = origFont[c];
            float size = BigOn ? Math.Max(f0.Size * 1.45f, 13f) : f0.Size;
            if (Math.Abs(c.Font.Size - size) > 0.05f || c.Font.FontFamily.Name != f0.FontFamily.Name || c.Font.Style != f0.Style)
                c.Font = new Font(f0.FontFamily, size, f0.Style);
            var btn = c as ButtonBase;
            if (!origColor.ContainsKey(c))
                origColor[c] = Tuple.Create(c.BackColor, c.ForeColor, btn != null ? btn.FlatStyle : FlatStyle.Standard, btn != null ? btn.FlatAppearance.BorderColor : Color.Empty);
            var oc = origColor[c];
            if (ContrastOn)
            {
                if (c is TextBox || c is ListBox || c is ComboBox || c is NumericUpDown) { c.BackColor = Color.Black; c.ForeColor = Color.White; }
                else if (btn != null && !(c is CheckBox) && !(c is RadioButton))
                {
                    c.BackColor = Color.Black; c.ForeColor = Color.Yellow;
                    btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.White; btn.FlatAppearance.BorderSize = 2;
                }
                else
                {
                    c.BackColor = Color.Black;
                    var fc = oc.Item2;
                    c.ForeColor = fc == Color.DarkGreen ? Color.LightGreen : fc == Color.Firebrick ? Color.Salmon : Color.White;
                }
            }
            else if (origColor.ContainsKey(c))
            {
                c.BackColor = oc.Item1; c.ForeColor = oc.Item2;
                if (btn != null) { btn.FlatStyle = oc.Item3; if (oc.Item4 != Color.Empty) btn.FlatAppearance.BorderColor = oc.Item4; btn.FlatAppearance.BorderSize = 1; }
            }
            if (BigOn && btn != null && !(c is CheckBox) && !(c is RadioButton)) c.MinimumSize = new Size(0, 40);
            else if (btn != null) c.MinimumSize = Size.Empty;
            foreach (Control ch in c.Controls) Walk(ch);
        }

        void ThemeStrip(ToolStrip ts)
        {
            ts.RenderMode = ContrastOn ? ToolStripRenderMode.System : ToolStripRenderMode.ManagerRenderMode;
            ts.BackColor = ContrastOn ? Color.Black : SystemColors.Control;
            ts.ForeColor = ContrastOn ? Color.White : SystemColors.ControlText;
            if (!origFont.ContainsKey(ts)) origFont[ts] = ts.Font;
            var f0 = origFont[ts];
            ts.Font = new Font(f0.FontFamily, BigOn ? Math.Max(f0.Size * 1.4f, 13f) : f0.Size, f0.Style);
            foreach (ToolStripItem it in ts.Items) ThemeItem(it);
        }

        void ThemeItem(ToolStripItem it)
        {
            it.ForeColor = ContrastOn ? Color.White : SystemColors.ControlText;
            it.BackColor = ContrastOn ? Color.Black : SystemColors.Control;
            var dd = it as ToolStripDropDownItem;
            if (dd != null) foreach (ToolStripItem s in dd.DropDownItems) ThemeItem(s);
        }
    }
}
