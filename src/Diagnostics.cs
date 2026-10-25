using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    // What the diagnostics window may look at. It never sees the user's text, and never prints the bot token.
    public class DiagContext
    {
        public PiperRunner Runner;
        public Func<Job> GetJob;
        public Func<List<VoiceInfo>> GetVoices;
        public Func<string> GetFfmpeg, GetOutDir, GetToken, GetProxy, GetApiBase, GetLogTail;
        public Func<List<int>> GetOutputIds;
        public string Version;
    }

    public class DiagnosticsForm : Form
    {
        readonly DiagContext ctx;
        readonly TextBox box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), BackColor = Color.White };
        readonly Label summary = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
        Button bRun;
        int problems;
        bool running;

        public DiagnosticsForm(DiagContext ctx)
        {
            this.ctx = ctx;
            Text = Lang.T("Diagnostics") + " — Piper Studio";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(860, 640);
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
            bRun = Btn(Lang.T("Run again"), (s, e) => Run());
            bar.Controls.Add(bRun);
            bar.Controls.Add(Btn(Lang.T("Play test sound"), (s, e) => TestSound()));
            bar.Controls.Add(Btn(Lang.T("Test microphone (3 s)"), (s, e) => TestMic()));
            bar.Controls.Add(Btn(Lang.T("Copy report"), (s, e) => { try { Clipboard.SetText(box.Text); Flash(Lang.T("Copied — paste it into a message to whoever is helping you.")); } catch { } }));
            bar.Controls.Add(Btn(Lang.T("Save report…"), (s, e) => SaveReport()));
            bar.Controls.Add(Btn(Lang.T("Close"), (s, e) => Close()));
            var note = new Label { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 2, 8, 2), MaximumSize = new Size(840, 0),
                Text = Lang.T("The report contains no text you typed and never shows your bot token, so it's safe to send to someone who is helping you.") };
            Controls.Add(box);
            Controls.Add(summary);
            PersianUi.Apply(bar); PersianUi.Apply(summary);
            Controls.Add(note);
            Controls.Add(bar);
            Shown += (s, e) => Run();
        }

        static Button Btn(string t, EventHandler click)
        {
            var b = new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };
            b.Click += click; return b;
        }

        void UI(Action a) { if (IsDisposed) return; try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { } }
        void Line(string s) { UI(() => box.AppendText(s + "\r\n")); }
        void Ok(string s) { Line("  [ OK ]  " + s); }
        void Bad(string s) { problems++; Line("  [FAIL]  " + s); }
        void Warn(string s) { Line("  [ !! ]  " + s); }
        void Info(string s) { Line("          " + s); }
        void Head(string s) { Line(""); Line("== " + s); }
        void Flash(string s) { summary.Text = s; summary.ForeColor = Color.DimGray; }

        void Run()
        {
            if (running) return;
            running = true; problems = 0;
            box.Clear(); bRun.Enabled = false;
            summary.Text = Lang.T("Checking…"); summary.ForeColor = Color.DimGray;
            Task.Run(() =>
            {
                try { RunChecks(); }
                catch (Exception ex) { Bad("Diagnostics stopped: " + ex.Message); }
                UI(() =>
                {
                    running = false; bRun.Enabled = true;
                    summary.Text = problems == 0 ? Lang.T("Everything looks fine.") : string.Format(Lang.T("{0} problem(s) found — see the lines marked FAIL."), problems);
                    summary.ForeColor = problems == 0 ? Color.DarkGreen : Color.Firebrick;
                });
            });
        }

        static string Mb(long bytes) { return (bytes / 1048576.0).ToString("0.0") + " MB"; }

        void RunChecks()
        {
            Line("Piper Studio diagnostics — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            Head("System");
            Info("Piper Studio " + ctx.Version + "  ·  " + Application.ExecutablePath);
            Info("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)") + "  ·  process " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            Info(".NET runtime " + Environment.Version + "  ·  interface language: " + (Lang.Fa ? "Persian" : "English"));
            Info("Settings folder: " + AppPaths.DataDir + (AppPaths.Portable ? "  (portable mode)" : ""));
            if (AppPaths.CanWrite(AppPaths.DataDir)) Ok("Settings folder is writable"); else Bad("Can't write to the settings folder — settings won't be saved");

            Head("Speech engine");
            string piper = ctx.Runner.PiperExe;
            if (string.IsNullOrEmpty(piper) || !File.Exists(piper)) Bad("piper.exe not found — set it on the Voice tab");
            else
            {
                Ok("piper.exe: " + piper);
                string dir = Path.GetDirectoryName(Path.GetFullPath(piper));
                foreach (var f in new[] { "espeak-ng.dll", "onnxruntime.dll", "piper_phonemize.dll" })
                    if (File.Exists(Path.Combine(dir, f))) Ok(f); else Bad(f + " is missing next to piper.exe");
                if (Directory.Exists(Path.Combine(dir, "espeak-ng-data"))) Ok("espeak-ng-data folder"); else Bad("espeak-ng-data folder is missing next to piper.exe (no pronunciation without it)");
            }
            Job job = null;
            try { job = ctx.GetJob(); } catch { }
            if (job == null) Bad("No voice selected (or piper.exe missing)");
            else
            {
                string sample = job.Main.IsPersian ? (job.Finglish ? "salaam, in yek test ast." : "سلام، این یک آزمایش است.") : "Hello, this is a test.";
                try
                {
                    var sw = Stopwatch.StartNew();
                    var pcm = ctx.Runner.Synthesize(job.Main, job.MainSpeaker, job.Finglish, job.Params, sample, CancellationToken.None);
                    double secs = pcm.Length / (double)job.Main.SampleRate;
                    if (pcm.Length == 0) Bad("Test speech with " + job.Main.FileName + " produced no audio");
                    else Ok(string.Format("Test speech with {0}: {1:0.0} s of audio in {2:0.0} s", job.Main.FileName, secs, sw.Elapsed.TotalSeconds));
                    var sw2 = Stopwatch.StartNew();
                    ctx.Runner.Synthesize(job.Main, job.MainSpeaker, job.Finglish, job.Params, sample + " " + sample, CancellationToken.None);
                    Info(string.Format("Second sentence (voice already loaded): {0:0.00} s", sw2.Elapsed.TotalSeconds));
                    if (sw2.Elapsed.TotalSeconds > secs * 2) Warn(job.Main.IsKokoro ? "This PC is slow with Kokoro — the smaller Kokoro model or a Piper voice will respond faster"
                                                                                    : "This PC is slow with this voice — a \"medium\" voice will respond faster");
                }
                catch (Exception ex) { Bad("Test speech failed: " + ex.Message.Replace("\n", " | ")); }
            }

            var voices = ctx.GetVoices() ?? new List<VoiceInfo>();
            Head("Kokoro (optional English engine)");
            var kv0 = voices.FirstOrDefault(v => v.IsKokoro && !v.IsKokoroPython);
            var kp0 = voices.FirstOrDefault(v => v.IsKokoroPython);
            if (kv0 == null)
            {
                var k = KokoroInstall.Find(null);
                if (!k.EngineOk && !k.ModelOk) Info("Built-in Kokoro: not installed (optional). Voice tab → Get Kokoro voices.");
                else if (!k.EngineOk) Bad("Kokoro voices found in " + k.ModelDir + " but the engine (" + KokoroInstall.EngineLibName + ") is missing — Voice tab → Kokoro setup");
                else Bad("Kokoro engine found in " + k.EngineDir + " but no voices — Voice tab → Get Kokoro voices");
            }
            else
            {
                Ok("Engine: " + kv0.KokoroEngineDir);
                long msize = 0; try { msize = new FileInfo(kv0.OnnxPath).Length; } catch { }
                Ok("Model: " + kv0.KokoroModelDir + "  (" + kv0.KokoroModelFile + ", " + Mb(msize) + ")");
            }
            if (kp0 != null) Ok("Your Python Kokoro: " + kp0.KokoroPython);
            else Info("Python Kokoro: not found (optional — only if you installed it with pip yourself).");
            foreach (var first in new[] { kv0, kp0 })
            {
                if (first == null) continue;
                var kv = (job != null && job.Main.Engine == first.Engine) ? job.Main : (job != null && job.Second != null && job.Second.Engine == first.Engine) ? job.Second : first;
                string name = first.IsKokoroPython ? "Python Kokoro" : "Kokoro";
                try
                {
                    var p = new SynthParams();
                    var sw = Stopwatch.StartNew();
                    var pcm = ctx.Runner.Synthesize(kv, 0, false, p, "Hello, this is a test.", CancellationToken.None);
                    double secs = pcm.Length / (double)kv.SampleRate;
                    if (pcm.Length == 0) Bad(name + " test speech produced no audio");
                    else Ok(string.Format("{0} test speech with {1}: {2:0.0} s of audio in {3:0.0} s (includes loading)", name, kv.KokoroId, secs, sw.Elapsed.TotalSeconds));
                    var sw2 = Stopwatch.StartNew();
                    ctx.Runner.Synthesize(kv, 0, false, p, "Hello, this is a test. Hello, this is a test.", CancellationToken.None);
                    Info(string.Format("Second sentence ({0} already loaded): {1:0.00} s", name, sw2.Elapsed.TotalSeconds));
                    if (sw2.Elapsed.TotalSeconds > secs * 2.5) Warn(name + " is slow on this PC — try the smaller Kokoro model, or Piper voices for live talking");
                }
                catch (Exception ex) { Bad(name + " test speech failed: " + ex.Message.Replace("\n", " | ")); }
            }

            Head("Voices");
            if (voices.Count == 0) Bad("No voices found");
            foreach (var v in voices)
            {
                if (v.IsKokoro) continue;
                long size = 0; try { size = new FileInfo(v.OnnxPath).Length; } catch { }
                string line = string.Format("{0}  ·  {1}  ·  {2} Hz  ·  {3}{4}", v.FileName, v.LangCode, v.SampleRate, Mb(size), v.NumSpeakers > 1 ? "  ·  " + v.NumSpeakers + " speakers" : "");
                if (v.LoadError != null) Bad(line + "  — settings file problem: " + v.LoadError); else Ok(line);
            }

            Head("FFmpeg (MP3/OGG export, music import, Telegram bot)");
            string ff = ctx.GetFfmpeg();
            if (ff == null) Warn("FFmpeg not found — WAV export works; MP3/OGG export, importing MP3 music and the Telegram bot need it");
            else
            {
                Ok("ffmpeg: " + ff);
                string ver = RunTool(ff, "-hide_banner -version");
                if (ver != null) Info(ver.Split('\n')[0].Trim());
                string enc = RunTool(ff, "-hide_banner -encoders") ?? "";
                foreach (var kv in new[] { new[] { "libmp3lame", "MP3 export" }, new[] { "libopus", "Telegram voice messages (bot)" }, new[] { "libvorbis", "OGG export" }, new[] { "aac", "M4A export" }, new[] { "flac", "FLAC export" } })
                    if (enc.Contains(" " + kv[0] + " ")) Ok(kv[0] + " — " + kv[1]); else Bad(kv[0] + " missing — " + kv[1] + " won't work with this FFmpeg build");
            }

            Head("Sound devices");
            if (!WaveOut.Available) Warn("Windows audio not available");
            else
            {
                var outs = WaveOut.Devices();
                Info("Speakers / outputs (" + outs.Count + "): " + (outs.Count == 0 ? "none!" : string.Join("  |  ", outs)));
                if (outs.Count == 0) Bad("No output device found");
                if (outs.Any(o => o.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0)) Ok("VB-CABLE is installed (needed for speaking into Telegram/Discord)");
                else Info("VB-CABLE not installed (only needed for speaking into Telegram/Discord calls)");
                var mics = Recorder.Devices();
                Info("Microphones (" + mics.Count + "): " + (mics.Count == 0 ? "none" : string.Join("  |  ", mics)));
                var ids = ctx.GetOutputIds();
                Info("Say now plays to device(s): " + string.Join(", ", ids.Select(i => i < 0 ? "default" : (i < outs.Count ? outs[i] : "#" + i))));
                if (ids.Any(i => i >= outs.Count)) Bad("The device chosen on the Mic tab is gone — choose it again");
            }

            Head("Folders");
            string od = ctx.GetOutDir();
            if (AppPaths.CanWrite(od)) Ok("Export folder is writable: " + od); else Bad("Can't write to the export folder: " + od);
            if (AppPaths.CanWrite(Path.GetTempPath())) Ok("Temporary folder is writable"); else Bad("Can't write to the temporary folder " + Path.GetTempPath());

            Head("Telegram bot");
            string token = ctx.GetToken();
            if (string.IsNullOrWhiteSpace(token)) Info("No bot token set (only needed for the Telegram bot)");
            else
            {
                Ok("Bot token is set (" + token.Length + " characters, stored encrypted)");
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    var h = new HttpClientHandler();
                    string proxy = ctx.GetProxy();
                    if (!string.IsNullOrWhiteSpace(proxy)) { h.Proxy = new WebProxy(proxy.Trim()); h.UseProxy = true; Info("Using proxy " + proxy.Trim()); }
                    using (var http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        var resp = http.GetAsync(ctx.GetApiBase().TrimEnd('/') + "/bot" + token.Trim() + "/getMe").Result;
                        string body = resp.Content.ReadAsStringAsync().Result;
                        var root = MiniJson.Parse(body) as Dictionary<string, object>;
                        object okv; root.TryGetValue("ok", out okv);
                        if (okv is bool && (bool)okv)
                        {
                            var res = root["result"] as Dictionary<string, object>;
                            Ok("Telegram is reachable — bot @" + (res != null && res.ContainsKey("username") ? res["username"] : "?"));
                            object inline; if (res != null && res.TryGetValue("supports_inline_queries", out inline) && inline is bool && !(bool)inline)
                                Bad("Inline mode is off — in @BotFather send /setinline and choose your bot");
                        }
                        else Bad("Telegram refused the token (" + (int)resp.StatusCode + ") — check it, or get a new one from @BotFather");
                    }
                }
                catch (Exception ex)
                {
                    var e = ex; while (e.InnerException != null) e = e.InnerException;
                    Bad("Can't reach Telegram: " + e.Message + " — is your VPN on? If your VPN only gives a proxy, enter it on the Bot tab.");
                }
            }

            Head("Recent log");
            string log = ctx.GetLogTail() ?? "";
            foreach (var raw in log.Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0).Reverse().Take(40).Reverse())
            {
                string l = Redact(raw);
                Info(l.Length > 160 ? l.Substring(0, 160) + "…" : l);
            }
        }

        // The log can contain what the user said (and file names made from it). Hide that before it goes into the report.
        public static string Redact(string line)
        {
            line = System.Text.RegularExpressions.Regex.Replace(line, "«[^»]*»", "«…»");
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^(\s*\d\d:\d\d:\d\d\s+)(Say|Phrase|Read aloud|\[tracks\] Say): .*$", "$1$2: (text hidden)");
            line = System.Text.RegularExpressions.Regex.Replace(line, @"(Saved|Exported|Opened|Saved:)\s*:?\s*(.+?)([^\\/]+)(\.\w{2,5})(\s|$)", m => m.Groups[1].Value + " " + m.Groups[2].Value + "…" + m.Groups[4].Value + m.Groups[5].Value);
            return line;
        }

        static string RunTool(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return o + err.Result;
                }
            }
            catch { return null; }
        }

        void TestSound()
        {
            if (!WaveOut.Available) { Flash(Lang.T("Windows audio not available.")); return; }
            var ids = ctx.GetOutputIds();
            Flash(Lang.T("Playing a test sound on the Say now device(s)…"));
            Task.Run(() =>
            {
                try
                {
                    int rate = 48000; var s = new short[rate];
                    for (int i = 0; i < s.Length; i++) s[i] = (short)(Math.Sin(2 * Math.PI * (440 + (i * 220.0 / rate)) * i / rate) * 9000 * Math.Min(1, Math.Min(i, s.Length - i) / 2000.0));
                    WaveOut.PlaySync(s, rate, ids, CancellationToken.None);
                    UI(() => Flash(Lang.T("Did you hear a rising tone? If not, check the device on the Mic tab and the Windows volume.")));
                }
                catch (Exception ex) { UI(() => Flash(ex.Message)); }
            });
        }

        void TestMic()
        {
            if (!WaveOut.Available) { Flash(Lang.T("Windows audio not available.")); return; }
            Flash(Lang.T("Recording 3 seconds — say something…"));
            Task.Run(() =>
            {
                try
                {
                    var r = new Recorder();
                    r.Start(-1, 48000);
                    Thread.Sleep(3000);
                    var data = r.Stop();
                    int peak = 0; foreach (var v in data) { int a = Math.Abs((int)v); if (a > peak) peak = a; }
                    double pct = peak / 327.67;
                    string verdict = pct < 2 ? Lang.T("almost silent — the microphone may be muted, or blocked in Windows privacy settings (Settings → Privacy → Microphone)")
                        : pct < 15 ? Lang.T("quiet — speak closer or raise the microphone level in Windows sound settings")
                        : pct > 98 ? Lang.T("very loud (clipping) — lower the microphone level")
                        : Lang.T("good");
                    UI(() => { Flash(string.Format(Lang.T("Default microphone: loudest point {0:0}% — {1}"), pct, verdict)); box.AppendText("\r\n== Microphone test\r\n          default microphone peak " + pct.ToString("0") + "% (" + data.Length / 48000.0 + " s)\r\n"); });
                }
                catch (Exception ex) { UI(() => Flash(ex.Message)); }
            });
        }

        void SaveReport()
        {
            using (var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "PiperStudio-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt" })
                if (d.ShowDialog(this) == DialogResult.OK)
                    try { File.WriteAllText(d.FileName, box.Text, new UTF8Encoding(true)); Flash(Lang.T("Saved.")); } catch (Exception ex) { Flash(ex.Message); }
        }
    }
}
