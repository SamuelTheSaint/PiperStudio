using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    public partial class MainForm
    {
        // ================================================================== global shortcuts + tray
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        const int HK_SPEAK = 1, HK_SHOW = 2, HK_PHRASE = 10;   // phrases: 11..19
        readonly List<int> registered = new List<int>();

        CheckBox chkHkSpeak, chkHkShow, chkHkPhrases, chkTray, chkSelToSpeakers;
        Label lblHkStatus;
        NotifyIcon tray;
        ToolStripMenuItem trayPhrases;
        bool exiting, trayTipShown;

        TabPage ShortcutsTab()
        {
            var g = Grid();
            AddFull(g, new Label { Text = "Shortcuts that work in any program", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 4, 3, 4) });
            chkHkSpeak = new CheckBox { Text = "Ctrl+Alt+Space: read selected text aloud", AutoSize = true, Checked = true };
            AddFull(g, chkHkSpeak);
            AddFull(g, Note("Select text in any app (browser, Word, Telegram…) and press the shortcut; press it again to stop. If nothing is selected, the text on the clipboard is read."));
            chkSelToSpeakers = new CheckBox { Text = "Read on my speakers (not the Mic tab device)", AutoSize = true, Checked = true, Margin = new Padding(20, 0, 3, 8) };
            AddFull(g, chkSelToSpeakers);
            chkHkShow = new CheckBox { Text = "Ctrl+Alt+P: show Piper Studio", AutoSize = true, Checked = true };
            AddFull(g, chkHkShow);
            AddFull(g, Note("Brings the window to the front, ready to type in Say now."));
            chkHkPhrases = new CheckBox { Text = "Ctrl+Alt+1 … 9: speak quick phrases", AutoSize = true, Checked = false };
            AddFull(g, chkHkPhrases);
            AddFull(g, Note("Speaks phrase 1–9 of the selected group — handy during calls: say “Yes”, “Please wait” etc. without leaving Telegram. Off by default because some keyboard layouts use Ctrl+Alt (AltGr) + numbers for typing characters."));
            lblHkStatus = Note("");
            AddFull(g, lblHkStatus);
            AddFull(g, Note("Inside Piper Studio, Ctrl+1 … 9 always speak the quick phrases."));
            AddFull(g, new Label { Text = "Tray", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            chkTray = new CheckBox { Text = "Keep running in the tray when closed", AutoSize = true };
            AddFull(g, chkTray);
            AddFull(g, Note("Keeps the shortcuts and the Telegram bot working. Right-click the tray icon (near the clock) → Exit to quit completely."));
            foreach (var c in new[] { chkHkSpeak, chkHkShow, chkHkPhrases }) c.CheckedChanged += (s, e) => { RegisterHotkeys(); RebuildPhrases(); };
            return Page("Shortcuts", g);
        }

        void RegisterHotkeys()
        {
            UnregisterHotkeys();
            if (!IsHandleCreated || chkHkSpeak == null) return;
            var failed = new List<string>();
            try
            {
                Func<int, uint, uint, string, bool> reg = (id, mods, vk, name) =>
                {
                    if (RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk)) { registered.Add(id); return true; }
                    failed.Add(name); return false;
                };
                if (chkHkSpeak.Checked) reg(HK_SPEAK, MOD_CONTROL | MOD_ALT, (uint)Keys.Space, "Ctrl+Alt+Space");
                if (chkHkShow.Checked) reg(HK_SHOW, MOD_CONTROL | MOD_ALT, (uint)Keys.P, "Ctrl+Alt+P");
                if (chkHkPhrases.Checked)
                    for (int i = 1; i <= 9; i++) reg(HK_PHRASE + i, MOD_CONTROL | MOD_ALT, (uint)(Keys.D0 + i), "Ctrl+Alt+" + i);
                lblHkStatus.Text = failed.Count == 0 ? (registered.Count > 0 ? Lang.T("Shortcuts are active.") : "")
                    : Lang.T("Already used by another program: ") + string.Join(", ", failed);
                lblHkStatus.ForeColor = failed.Count == 0 ? Color.DarkGreen : Color.Firebrick;
            }
            catch (Exception)
            {
                lblHkStatus.Text = Lang.T("Global shortcuts need Windows.");
                lblHkStatus.ForeColor = Color.DimGray;
            }
        }

        void UnregisterHotkeys()
        {
            try { foreach (var id in registered) UnregisterHotKey(Handle, id); } catch { }
            registered.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) { OnHotkey(m.WParam.ToInt32()); return; }
            base.WndProc(ref m);
        }

        void OnHotkey(int id)
        {
            if (id == HK_SPEAK) SpeakSelection();
            else if (id == HK_SHOW) { ShowFromTray(); txtQuick.Focus(); }
            else if (id > HK_PHRASE && id <= HK_PHRASE + 9) SpeakPhrase(id - HK_PHRASE - 1);
        }

        List<int> SelectionOutputIds()
        {
            return chkSelToSpeakers != null && chkSelToSpeakers.Checked ? new List<int> { -1 } : null;
        }

        static bool KeyHeld(int vk) { try { return (GetAsyncKeyState(vk) & 0x8000) != 0; } catch { return false; } }
        static uint ClipSeq() { try { return GetClipboardSequenceNumber(); } catch { return 0; } }

        // Copies the selection from the program you're in (by sending Ctrl+C), reads it aloud,
        // and puts back whatever text was on the clipboard before.
        void SpeakSelection()
        {
            if (busy && currentQuick) { StopAll(); SetStatus(Lang.T("Stopped.")); return; }
            string oldText = null;
            try { if (Clipboard.ContainsText()) oldText = Clipboard.GetText(); } catch { }
            uint seq0 = ClipSeq();
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                // wait until Ctrl/Alt/Space are released so the other program sees a clean Ctrl+C
                while (sw.ElapsedMilliseconds < 1200 && (KeyHeld(0x11) || KeyHeld(0x12) || KeyHeld(0x20) || KeyHeld(0x10))) Thread.Sleep(15);
                try
                {
                    if (KeyHeld(0x12)) keybd_event(0x12, 0, 2, UIntPtr.Zero);
                    keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    keybd_event(0x43, 0, 0, UIntPtr.Zero);
                    keybd_event(0x43, 0, 2, UIntPtr.Zero);
                    keybd_event(0x11, 0, 2, UIntPtr.Zero);
                }
                catch { }
                var sw2 = Stopwatch.StartNew();
                while (sw2.ElapsedMilliseconds < 600 && ClipSeq() == seq0) Thread.Sleep(15);
                bool copied = ClipSeq() != seq0;
                Thread.Sleep(40);
                UI(() =>
                {
                    string text = null;
                    try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
                    if (copied && oldText != null) try { Clipboard.SetText(oldText); } catch { }
                    if (string.IsNullOrWhiteSpace(text)) { SetStatus(Lang.T("Nothing selected, and no text on the clipboard.")); return; }
                    if (!copied) SetStatus(Lang.T("Nothing selected — reading the clipboard instead."));
                    if (text.Length > 20000) text = text.Substring(0, 20000);
                    SpeakNow(text, "selection", SelectionOutputIds());
                });
            });
        }

        void SpeakClipboard()
        {
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(text)) { SetStatus(Lang.T("Nothing selected, and no text on the clipboard.")); return; }
            SpeakNow(text.Length > 20000 ? text.Substring(0, 20000) : text, "selection", SelectionOutputIds());
        }

        void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void SetupTray()
        {
            try
            {
                tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Piper Studio", Visible = true };
                var m = new ContextMenuStrip();
                m.Items.Add(Lang.T("Show Piper Studio") + "   (Ctrl+Alt+P)", null, (s, e) => ShowFromTray());
                m.Items.Add(Lang.T("Read the clipboard aloud"), null, (s, e) => SpeakClipboard());
                m.Items.Add(Lang.T("Stop speaking"), null, (s, e) => StopAll());
                trayPhrases = new ToolStripMenuItem(Lang.T("Quick phrases"));
                m.Items.Add(trayPhrases);
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Lang.T("Exit Piper Studio"), null, (s, e) => { exiting = true; Close(); });
                tray.ContextMenuStrip = m;
                tray.DoubleClick += (s, e) => ShowFromTray();
                RebuildTrayPhrases();
            }
            catch { tray = null; }
        }

        void RebuildTrayPhrases()
        {
            if (trayPhrases == null) return;
            trayPhrases.DropDownItems.Clear();
            foreach (var cat in phrases)
            {
                var sub = new ToolStripMenuItem(cat.Name);
                foreach (var p in cat.Items)
                {
                    string text = p;
                    sub.DropDownItems.Add(text.Length > 50 ? text.Substring(0, 49) + "…" : text, null, (s, e) => SpeakNow(text, "phrase", null));
                }
                trayPhrases.DropDownItems.Add(sub);
            }
        }

        // Called from FormClosing: hide to the tray instead of quitting, if the user asked for that.
        bool HideToTrayInsteadOfClosing(FormClosingEventArgs e)
        {
            if (exiting || restarting || tray == null || chkTray == null || !chkTray.Checked || e.CloseReason != CloseReason.UserClosing) return false;
            e.Cancel = true;
            Hide();
            if (!trayTipShown)
            {
                trayTipShown = true;
                try { tray.ShowBalloonTip(4000, "Piper Studio", Lang.T("Still running here, so your shortcuts and the bot keep working. Right-click this icon → Exit to quit."), ToolTipIcon.Info); } catch { }
            }
            return true;
        }

        void DisposeTray()
        {
            UnregisterHotkeys();
            if (tray != null) { try { tray.Visible = false; tray.Dispose(); } catch { } tray = null; }
        }
    }
}
