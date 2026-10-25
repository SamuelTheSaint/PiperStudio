using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PiperStudio
{
    public partial class MainForm
    {
        // ================================================================== conversation history
        public class HistItem { public DateTime Time; public string Source; public string Text; }

        readonly List<HistItem> history = new List<HistItem>();   // oldest first
        ListBox lstHistory;
        CheckBox chkKeepHistory;
        Label lblHistCount;
        int histNav = -1;          // position while browsing history with ↑ / ↓ in the Say now box
        bool settingQuickFromHistory;
        const int MaxHistory = 1000;
        string HistoryPath { get { return AppPaths.In("history.txt"); } }

        TabPage HistoryTab()
        {
            var page = new TabPage("History") { Padding = new Padding(6) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            top.Controls.Add(SmallButton("▶ Say again", (s, e) => SpeakSelectedHistory()));
            top.Controls.Add(SmallButton("☆ Save as phrase", (s, e) => { var h = SelectedHistory(); if (h != null) AddPhrase(h.Text); }));
            top.Controls.Add(SmallButton("Edit in Say now", (s, e) => { var h = SelectedHistory(); if (h != null) { txtQuick.Text = h.Text; txtQuick.Focus(); txtQuick.SelectionStart = txtQuick.Text.Length; } }));
            top.Controls.Add(SmallButton("Copy", (s, e) => { var h = SelectedHistory(); if (h != null) try { Clipboard.SetText(h.Text); } catch { } }));
            lblHistCount = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 8, 3, 3) };
            top.Controls.Add(lblHistCount);
            lstHistory = new ListBox { Dock = DockStyle.Fill, Font = new Font("Tahoma", 10f), IntegralHeight = false, HorizontalScrollbar = true };
            lstHistory.DoubleClick += (s, e) => SpeakSelectedHistory();
            lstHistory.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SpeakSelectedHistory(); } };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
            chkKeepHistory = new CheckBox { Text = "Remember history after closing", AutoSize = true, Checked = true, Margin = new Padding(3, 6, 12, 3) };
            chkKeepHistory.CheckedChanged += (s, e) => { if (!chkKeepHistory.Checked) try { File.Delete(HistoryPath); } catch { } else SaveHistory(); };
            bottom.Controls.Add(chkKeepHistory);
            bottom.Controls.Add(SmallButton("Clear history", (s, e) =>
            {
                if (history.Count == 0) return;
                if (MessageBox.Show(this, Lang.T("Delete the whole history?"), "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                history.Clear(); RefreshHistoryList(); SaveHistory();
            }));
            var note = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 52, ForeColor = Color.DimGray, Padding = new Padding(2, 4, 2, 2),
                Text = "Everything you said with Say now, quick phrases, the read-aloud shortcut and the bot. Double-click to say it again. In the Say now box, ↑ and ↓ go through it." };
            page.Controls.Add(lstHistory);
            page.Controls.Add(top);
            page.Controls.Add(note);
            page.Controls.Add(bottom);
            return page;
        }

        HistItem SelectedHistory()
        {
            if (lstHistory == null || lstHistory.SelectedIndex < 0) return null;
            int i = history.Count - 1 - lstHistory.SelectedIndex;   // the list shows newest first
            return i >= 0 && i < history.Count ? history[i] : null;
        }

        void SpeakSelectedHistory()
        {
            var h = SelectedHistory();
            if (h != null) SpeakNow(h.Text, h.Source == "selection" ? "selection" : "say", h.Source == "selection" ? SelectionOutputIds() : null);
        }

        static string SourceMark(string src)
        {
            switch (src) { case "phrase": return "★ "; case "selection": return "⧉ "; case "bot": return "@ "; default: return ""; }
        }

        string HistLine(HistItem h)
        {
            string t = h.Text.Replace("\r", " ").Replace("\n", " ");
            if (t.Length > 200) t = t.Substring(0, 199) + "…";
            string when = h.Time.Date == DateTime.Today ? h.Time.ToString("HH:mm") : h.Time.ToString("MM-dd HH:mm");
            return when + "   " + SourceMark(h.Source) + t;
        }

        void RefreshHistoryList()
        {
            if (lstHistory == null) return;
            lstHistory.BeginUpdate();
            lstHistory.Items.Clear();
            for (int i = history.Count - 1; i >= 0; i--) lstHistory.Items.Add(HistLine(history[i]));
            lstHistory.EndUpdate();
            lblHistCount.Text = string.Format(Lang.T("{0} items"), history.Count);
        }

        void AddHistory(string text, string source)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (history.Count > 0)
            {
                var last = history[history.Count - 1];
                if (last.Text == text && (DateTime.Now - last.Time).TotalSeconds < 3) return;
            }
            history.Add(new HistItem { Time = DateTime.Now, Source = source, Text = text.Length > 5000 ? text.Substring(0, 5000) : text });
            if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);
            histNav = -1;
            if (lstHistory != null)
            {
                lstHistory.Items.Insert(0, HistLine(history[history.Count - 1]));
                while (lstHistory.Items.Count > history.Count) lstHistory.Items.RemoveAt(lstHistory.Items.Count - 1);
                lblHistCount.Text = string.Format(Lang.T("{0} items"), history.Count);
            }
            SaveHistory();
        }

        // history.txt: one item per line — time <tab> source <tab> text (newlines stored as \n)
        void SaveHistory()
        {
            if (chkKeepHistory == null || !chkKeepHistory.Checked) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var h in history)
                    sb.Append(h.Time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append('\t').Append(h.Source).Append('\t')
                      .Append(h.Text.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace("\t", " ")).Append("\r\n");
                File.WriteAllText(HistoryPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static List<HistItem> ParseHistory(string text)
        {
            var list = new List<HistItem>();
            foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 3) continue;
                DateTime t;
                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                var sb = new StringBuilder();
                string s = parts[2];
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { char n = s[++i]; sb.Append(n == 'n' ? '\n' : n); }
                    else sb.Append(s[i]);
                }
                list.Add(new HistItem { Time = t, Source = parts[1], Text = sb.ToString() });
            }
            return list;
        }

        void LoadHistory()
        {
            chkKeepHistory.Checked = IniBool("keephistory", true);
            try { if (chkKeepHistory.Checked && File.Exists(HistoryPath)) { history.Clear(); history.AddRange(ParseHistory(File.ReadAllText(HistoryPath, Encoding.UTF8))); } } catch { }
            if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);
            RefreshHistoryList();
        }

        // ↑ / ↓ in the Say now box: walk through what you said before.
        void HistoryNavigate(int dir)
        {
            if (history.Count == 0) return;
            if (histNav < 0 && dir > 0 && txtQuick.Text.Trim().Length > 0) return;   // don't overwrite something you're typing
            int next = histNav + dir;
            if (next < 0) { histNav = -1; settingQuickFromHistory = true; txtQuick.Clear(); settingQuickFromHistory = false; return; }
            if (next >= history.Count) next = history.Count - 1;
            histNav = next;
            settingQuickFromHistory = true;
            txtQuick.Text = history[history.Count - 1 - histNav].Text;
            txtQuick.SelectionStart = txtQuick.Text.Length;
            settingQuickFromHistory = false;
        }
    }
}
