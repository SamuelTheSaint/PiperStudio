using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PiperStudio
{
    // Small reusable dialogs.
    public static class Dialogs
    {
        public static string Prompt(IWin32Window owner, string title, string label, string value, bool multiline)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 9f), ShowInTaskbar = false })
            {
                int h = multiline ? 260 : 26;
                var lf = new Font("Segoe UI", 9f);
                int lh = TextRenderer.MeasureText(label, lf, new Size(456, 0), TextFormatFlags.WordBreak).Height;
                int top = 10 + lh + 8;
                f.ClientSize = new Size(480, top + h + 58);
                var l = new Label { Text = label, Location = new Point(12, 10), AutoSize = true, MaximumSize = new Size(456, 0), Font = lf };
                var tb = new TextBox { Text = value ?? "", Location = new Point(12, top), Width = 456, Font = new Font("Tahoma", 10.5f) };
                if (multiline) { tb.Multiline = true; tb.Height = h; tb.ScrollBars = ScrollBars.Vertical; tb.AcceptsReturn = true; }
                if (TextTools.StartsRtl(tb.Text)) tb.RightToLeft = RightToLeft.Yes;
                tb.TextChanged += (s, e) => { var want = TextTools.StartsRtl(tb.Text) ? RightToLeft.Yes : RightToLeft.No; if (tb.RightToLeft != want) tb.RightToLeft = want; };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(312, top + h + 16), Width = 75 };
                var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(393, top + h + 16), Width = 75 };
                f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
                if (!multiline) f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (s, e) => { tb.Focus(); if (!multiline) tb.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }
    }

    public partial class MainForm
    {
        // ================================================================== quick phrases
        public class PhraseCat { public string Name; public List<string> Items = new List<string>(); }

        List<PhraseCat> phrases = new List<PhraseCat>();
        int phraseCat;
        FlowLayoutPanel phraseHead, phraseCats, phraseButtons;
        Button phraseToggle;
        bool phrasesOpen = true;
        readonly ToolTip phraseTips = new ToolTip();
        string PhrasesPath { get { return AppPaths.In("phrases.txt"); } }

        public static List<PhraseCat> ParsePhrases(string text)
        {
            var list = new List<PhraseCat>();
            PhraseCat cur = null;
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("##"))
                {
                    cur = new PhraseCat { Name = line.Substring(2).Trim() };
                    if (cur.Name.Length == 0) cur.Name = "…";
                    list.Add(cur);
                    continue;
                }
                if (line.StartsWith("#")) continue;
                if (cur == null) { cur = new PhraseCat { Name = Lang.T("Phrases") }; list.Add(cur); }
                cur.Items.Add(line);
            }
            return list;
        }

        public static string SerializePhrases(List<PhraseCat> list)
        {
            var sb = new StringBuilder();
            sb.Append("# Piper Studio quick phrases. A line starting with ## begins a group; every other line is one phrase.\r\n");
            foreach (var c in list)
            {
                sb.Append("\r\n## ").Append(c.Name).Append("\r\n");
                foreach (var i in c.Items) sb.Append(i.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
            }
            return sb.ToString();
        }

        static string DefaultPhrases(bool fa)
        {
            if (fa) return
                "## پایه\nبله.\nنه.\nممنون.\nلطفاً صبر کنید، دارم تایپ می‌کنم.\nمیشه لطفاً تکرار کنید؟\nمتوجه نشدم.\nیک لحظه.\nببخشید.\n" +
                "## گفتگو\nسلام!\nحالت چطوره؟\nخوبم، ممنون.\nعالیه!\nالان برمی‌گردم.\nخداحافظ، بعداً صحبت می‌کنیم.\n" +
                "## کمک\nلطفاً کمکم کنید.\nلطفاً آهسته‌تر صحبت کنید.\nالان نمی‌توانم حرف بزنم، اما می‌توانم بنویسم.\nلطفاً بعداً تماس بگیرید.\n";
            return
                "## Basics\nYes.\nNo.\nThank you.\nPlease wait, I'm typing.\nCan you repeat that, please?\nI don't understand.\nJust a moment.\nSorry.\n" +
                "## Chat\nHello!\nHow are you?\nI'm fine, thanks.\nThat's great!\nI'll be right back.\nGoodbye, talk to you later.\n" +
                "## Help\nI need help, please.\nPlease speak more slowly.\nI can't talk right now, but I can type.\nPlease call me back later.\n";
        }

        void LoadPhrases()
        {
            try
            {
                if (File.Exists(PhrasesPath)) phrases = ParsePhrases(File.ReadAllText(PhrasesPath, Encoding.UTF8));
                else { phrases = ParsePhrases(DefaultPhrases(Lang.Fa)); SavePhrases(); }
            }
            catch { phrases = ParsePhrases(DefaultPhrases(Lang.Fa)); }
            if (phrases.Count == 0) phrases.Add(new PhraseCat { Name = Lang.T("Phrases") });
            phraseCat = Math.Max(0, Math.Min(phrases.Count - 1, IniInt("phrasecat", 0)));
            SetPhrasesOpen(IniBool("phrasesopen", true));
            RebuildPhrases();
        }

        void SavePhrases()
        {
            try { File.WriteAllText(PhrasesPath, SerializePhrases(phrases), new UTF8Encoding(false)); } catch (Exception ex) { AddLog("Couldn't save phrases: " + ex.Message); }
        }

        void BuildPhrasePanels(out Control head, out Control buttons)
        {
            phraseHead = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
            phraseToggle = SmallButton("▾", (s, e) => SetPhrasesOpen(!phrasesOpen));
            phraseTips.SetToolTip(phraseToggle, Lang.T("Show / hide the quick phrases"));
            phraseHead.Controls.Add(phraseToggle);
            phraseHead.Controls.Add(new Label { Text = "Quick phrases:", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 7, 6, 0) });
            phraseCats = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            phraseHead.Controls.Add(phraseCats);
            var edit = SmallButton("✎", (s, e) => EditPhrasesAsText());
            phraseTips.SetToolTip(edit, Lang.T("Edit all phrases and groups as text"));
            phraseHead.Controls.Add(edit);

            phraseButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, MaximumSize = new Size(0, 150), AutoScroll = true, Padding = new Padding(0, 2, 0, 2) };
            head = phraseHead; buttons = phraseButtons;
        }

        void SetPhrasesOpen(bool open)
        {
            if (phraseButtons == null) return;
            phrasesOpen = open;
            phraseButtons.Visible = open;
            phraseToggle.Text = open ? "▾" : "▸";
            ini["phrasesopen"] = open ? "1" : "0";
        }

        PhraseCat CurrentCat { get { return phrases.Count == 0 ? null : phrases[Math.Max(0, Math.Min(phrases.Count - 1, phraseCat))]; } }

        void RebuildPhrases()
        {
            if (phraseCats == null) return;
            phraseCats.SuspendLayout(); phraseButtons.SuspendLayout();
            phraseCats.Controls.Clear(); phraseButtons.Controls.Clear();
            for (int i = 0; i < phrases.Count; i++)
            {
                int ci = i;
                var b = new Button { Text = phrases[i].Name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 1, 4, 1), Margin = new Padding(2, 3, 2, 3), FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9.5f, i == phraseCat ? FontStyle.Bold : FontStyle.Regular) };
                b.BackColor = i == phraseCat ? Color.FromArgb(70, 120, 200) : SystemColors.Control;
                b.ForeColor = i == phraseCat ? Color.White : SystemColors.ControlText;
                b.FlatAppearance.BorderColor = Color.FromArgb(70, 120, 200);
                b.Click += (s, e) => { phraseCat = ci; ini["phrasecat"] = ci.ToString(); RebuildPhrases(); };
                b.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowCategoryMenu(ci, b); };
                phraseCats.Controls.Add(b);
            }
            var addCat = SmallButton("＋", (s, e) => NewCategory());
            phraseTips.SetToolTip(addCat, Lang.T("New group of phrases"));
            phraseCats.Controls.Add(addCat);

            var cat = CurrentCat;
            if (cat != null)
            {
                for (int i = 0; i < cat.Items.Count; i++)
                {
                    int pi = i;
                    string text = cat.Items[i];
                    string label = (i < 9 ? (i + 1) + "  " : "") + (text.Length > 34 ? text.Substring(0, 33) + "…" : text);
                    var b = new Button { Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 3, 6, 3), Margin = new Padding(3), Font = new Font("Tahoma", 10.5f), BackColor = Color.FromArgb(235, 242, 252), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                    b.FlatAppearance.BorderColor = Color.FromArgb(150, 180, 220);
                    if (TextTools.StartsRtl(text)) b.RightToLeft = RightToLeft.Yes;
                    phraseTips.SetToolTip(b, text + (i < 9 ? "\n" + Lang.T("Shortcut: Ctrl+") + (i + 1) + (chkHkPhrases != null && chkHkPhrases.Checked ? "   ·   " + Lang.T("from any app: Ctrl+Alt+") + (i + 1) : "") : "") + "\n" + Lang.T("Right-click to edit"));
                    b.Click += (s, e) => SpeakPhrase(pi);
                    b.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowPhraseMenu(pi, b); };
                    phraseButtons.Controls.Add(b);
                }
            }
            var add = new Button { Text = "＋ " + Lang.T("Add phrase"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 3, 6, 3), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, ForeColor = Color.DimGray };
            add.FlatAppearance.BorderColor = Color.Silver;
            add.Click += (s, e) => AddPhrase(null);
            phraseButtons.Controls.Add(add);
            phraseCats.ResumeLayout(); phraseButtons.ResumeLayout();
            if (tray != null) RebuildTrayPhrases();
        }

        void SpeakPhrase(int index)
        {
            var cat = CurrentCat;
            if (cat == null || index < 0 || index >= cat.Items.Count) return;
            SpeakNow(cat.Items[index], "phrase", null);
        }

        void AddPhrase(string initial)
        {
            var cat = CurrentCat;
            if (cat == null) { phrases.Add(cat = new PhraseCat { Name = Lang.T("Phrases") }); phraseCat = 0; }
            string t = Dialogs.Prompt(this, Lang.T("Add phrase"), string.Format(Lang.T("New phrase for “{0}”:"), cat.Name), initial ?? "", false);
            if (t == null || t.Trim().Length == 0) return;
            cat.Items.Add(t.Trim());
            SavePhrases(); RebuildPhrases();
            SetStatus(Lang.T("Phrase added."));
        }

        void ShowPhraseMenu(int i, Control at)
        {
            var cat = CurrentCat; if (cat == null) return;
            var m = new ContextMenuStrip();
            m.Items.Add(Lang.T("Speak"), null, (s, e) => SpeakPhrase(i));
            m.Items.Add(Lang.T("Put in the Say now box"), null, (s, e) => { txtQuick.Text = cat.Items[i]; txtQuick.Focus(); txtQuick.SelectionStart = txtQuick.Text.Length; });
            m.Items.Add(Lang.T("Edit…"), null, (s, e) =>
            {
                string t = Dialogs.Prompt(this, Lang.T("Edit phrase"), Lang.T("Phrase:"), cat.Items[i], false);
                if (t != null && t.Trim().Length > 0) { cat.Items[i] = t.Trim(); SavePhrases(); RebuildPhrases(); }
            });
            m.Items.Add(Lang.T("Move left"), null, (s, e) => { if (i > 0) { var x = cat.Items[i]; cat.Items.RemoveAt(i); cat.Items.Insert(i - 1, x); SavePhrases(); RebuildPhrases(); } });
            m.Items.Add(Lang.T("Move right"), null, (s, e) => { if (i < cat.Items.Count - 1) { var x = cat.Items[i]; cat.Items.RemoveAt(i); cat.Items.Insert(i + 1, x); SavePhrases(); RebuildPhrases(); } });
            var move = new ToolStripMenuItem(Lang.T("Move to group"));
            for (int k = 0; k < phrases.Count; k++)
            {
                if (k == phraseCat) continue;
                int kk = k;
                move.DropDownItems.Add(phrases[k].Name, null, (s, e) => { var x = cat.Items[i]; cat.Items.RemoveAt(i); phrases[kk].Items.Add(x); SavePhrases(); RebuildPhrases(); });
            }
            if (move.DropDownItems.Count > 0) m.Items.Add(move);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Delete"), null, (s, e) => { cat.Items.RemoveAt(i); SavePhrases(); RebuildPhrases(); });
            m.Show(at, new Point(0, at.Height));
        }

        void NewCategory()
        {
            string n = Dialogs.Prompt(this, Lang.T("New group"), Lang.T("Name of the new group (for example: Family, Work, Doctor):"), "", false);
            if (n == null || n.Trim().Length == 0) return;
            phrases.Add(new PhraseCat { Name = n.Trim() });
            phraseCat = phrases.Count - 1; ini["phrasecat"] = phraseCat.ToString();
            SavePhrases(); RebuildPhrases();
        }

        void ShowCategoryMenu(int ci, Control at)
        {
            var m = new ContextMenuStrip();
            m.Items.Add(Lang.T("Rename group…"), null, (s, e) =>
            {
                string n = Dialogs.Prompt(this, Lang.T("Rename group"), Lang.T("Group name:"), phrases[ci].Name, false);
                if (n != null && n.Trim().Length > 0) { phrases[ci].Name = n.Trim(); SavePhrases(); RebuildPhrases(); }
            });
            m.Items.Add(Lang.T("New group…"), null, (s, e) => NewCategory());
            m.Items.Add(Lang.T("Move left"), null, (s, e) => { if (ci > 0) { var x = phrases[ci]; phrases.RemoveAt(ci); phrases.Insert(ci - 1, x); phraseCat = ci - 1; SavePhrases(); RebuildPhrases(); } });
            m.Items.Add(Lang.T("Move right"), null, (s, e) => { if (ci < phrases.Count - 1) { var x = phrases[ci]; phrases.RemoveAt(ci); phrases.Insert(ci + 1, x); phraseCat = ci + 1; SavePhrases(); RebuildPhrases(); } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Lang.T("Delete group"), null, (s, e) =>
            {
                if (phrases[ci].Items.Count > 0 && MessageBox.Show(this, string.Format(Lang.T("Delete the group “{0}” and its {1} phrases?"), phrases[ci].Name, phrases[ci].Items.Count), "Piper Studio", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                phrases.RemoveAt(ci);
                if (phrases.Count == 0) phrases.Add(new PhraseCat { Name = Lang.T("Phrases") });
                phraseCat = Math.Min(phraseCat, phrases.Count - 1);
                SavePhrases(); RebuildPhrases();
            });
            m.Show(at, new Point(0, at.Height));
        }

        void EditPhrasesAsText()
        {
            string help = Lang.T("One phrase per line. A line starting with ## starts a new group, for example:\n## Family\nI love you.\nWhat's for dinner?");
            string t = Dialogs.Prompt(this, Lang.T("Edit quick phrases"), help, SerializePhrases(phrases), true);
            if (t == null) return;
            var parsed = ParsePhrases(t);
            if (parsed.Count == 0) parsed.Add(new PhraseCat { Name = Lang.T("Phrases") });
            phrases = parsed;
            phraseCat = Math.Min(phraseCat, phrases.Count - 1);
            SavePhrases(); RebuildPhrases();
        }
    }
}
