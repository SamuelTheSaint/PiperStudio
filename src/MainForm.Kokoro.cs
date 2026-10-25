using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    // Downloads and unpacks the Kokoro engine (sherpa-onnx) and a Kokoro model. No UI here, so it can be tested.
    public static class KokoroInstaller
    {
        public const string EngineVersion = "v1.13.8";
        public static readonly string EngineUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/" + EngineVersion + "/sherpa-onnx-" + EngineVersion + "-win-x64-shared-MT-Release.tar.bz2";
        public const string SmallModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2";
        public const string BestModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2";
        public static readonly string[] EngineFiles = { "sherpa-onnx-c-api.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll" };

        // progress: (fraction 0..1 or -1 when unknown, status text)
        public static void Download(string url, string file, Action<double, string> progress, CancellationToken ct)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            using (var http = new HttpClient { Timeout = TimeSpan.FromHours(3) })
            using (var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).Result)
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1;
                string part = file + ".part";
                using (var src = resp.Content.ReadAsStreamAsync().Result)
                using (var dst = File.Create(part))
                {
                    var buf = new byte[1 << 16];
                    long done = 0; int n; var sw = Stopwatch.StartNew(); long lastTick = 0;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dst.Write(buf, 0, n); done += n;
                        if (sw.ElapsedMilliseconds - lastTick > 250 || done == total)
                        {
                            lastTick = sw.ElapsedMilliseconds;
                            double mbps = done / 1048576.0 / Math.Max(0.5, sw.Elapsed.TotalSeconds);
                            string s = total > 0 ? string.Format("{0:0} / {1:0} MB  ·  {2:0.0} MB/s", done / 1048576.0, total / 1048576.0, mbps)
                                                 : string.Format("{0:0} MB  ·  {1:0.0} MB/s", done / 1048576.0, mbps);
                            progress(total > 0 ? done / (double)total : -1, s);
                        }
                    }
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(part, file);
            }
        }

        // Windows 10/11 include tar.exe, which can unpack .tar.bz2.
        public static void Extract(string archive, string toDir, CancellationToken ct)
        {
            Directory.CreateDirectory(toDir);
            string tar = Environment.OSVersion.Platform == PlatformID.Win32NT ? Path.Combine(Environment.SystemDirectory, "tar.exe") : "tar";
            if (tar != "tar" && !File.Exists(tar))
                throw new Exception(Lang.T("This Windows has no tar.exe to unpack the download. Unpack the .tar.bz2 file with 7-Zip, then use \"Use files I already have…\" and choose the unpacked folder."));
            var psi = new ProcessStartInfo(tar, "-xf \"" + archive + "\" -C \"" + toDir + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEndAsync();
                while (!p.WaitForExit(200))
                    if (ct.IsCancellationRequested) { try { p.Kill(); } catch { } ct.ThrowIfCancellationRequested(); }
                if (p.ExitCode != 0) throw new Exception(Lang.T("Unpacking failed: ") + err.Result.Trim());
            }
        }

        // Puts the engine DLLs straight into root, from an unpacked sherpa-onnx folder (or any folder that has them).
        public static bool TakeEngineFrom(string dir, string root)
        {
            string found = null;
            try
            {
                foreach (var f in Directory.GetFiles(dir, EngineFiles[0], SearchOption.AllDirectories))
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(f), "onnxruntime.dll"))) { found = Path.GetDirectoryName(f); break; }
            }
            catch { }
            if (found == null) return false;
            Directory.CreateDirectory(root);
            foreach (var n in EngineFiles)
            {
                string s = Path.Combine(found, n);
                if (File.Exists(s) && !string.Equals(Path.GetFullPath(s), Path.GetFullPath(Path.Combine(root, n)), StringComparison.OrdinalIgnoreCase))
                    File.Copy(s, Path.Combine(root, n), true);
            }
            return true;
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        // Downloads whatever is missing. best = full-quality model instead of the smaller int8 one.
        public static void Install(string root, bool needEngine, bool needModel, bool best, Action<double, string> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(root);
            if (needEngine)
            {
                string arc = Path.Combine(root, "engine-download.tar.bz2"), tmp = Path.Combine(root, "_engine_tmp");
                try
                {
                    Download(EngineUrl, arc, (f, s) => progress(f, Lang.T("Downloading the Kokoro engine… ") + s), ct);
                    progress(-1, Lang.T("Unpacking the engine…"));
                    Extract(arc, tmp, ct);
                    if (!TakeEngineFrom(tmp, root)) throw new Exception(Lang.T("The engine download didn't contain sherpa-onnx-c-api.dll."));
                }
                finally { TryDelete(arc); TryDelete(tmp); }
            }
            if (needModel)
            {
                string url = best ? BestModelUrl : SmallModelUrl;
                string arc = Path.Combine(root, "model-download.tar.bz2");
                try
                {
                    Download(url, arc, (f, s) => progress(f, Lang.T("Downloading Kokoro voices… ") + s), ct);
                    progress(-1, Lang.T("Unpacking the voices (a minute or two)…"));
                    Extract(arc, root, ct);
                }
                finally { TryDelete(arc); }
            }
        }

        // "Use files I already have": a downloaded .tar.bz2 (engine or model) or an unpacked folder.
        // Returns a short description of what was added.
        public static string InstallFromLocal(string path, string root, CancellationToken ct, Action<double, string> progress)
        {
            Directory.CreateDirectory(root);
            var added = new List<string>();
            if (File.Exists(path))
            {
                string tmp = Path.Combine(root, "_local_tmp");
                try
                {
                    progress(-1, Lang.T("Unpacking…"));
                    Extract(path, tmp, ct);
                    if (TakeEngineFrom(tmp, root)) added.Add(Lang.T("engine"));
                    foreach (var d in ModelDirsUnder(tmp))
                    {
                        string dest = Path.Combine(root, Path.GetFileName(d));
                        TryDelete(dest);
                        Directory.Move(d, dest);
                        added.Add(Lang.T("voices"));
                    }
                }
                finally { TryDelete(tmp); }
            }
            else if (Directory.Exists(path))
            {
                if (TakeEngineFrom(path, root)) added.Add(Lang.T("engine"));
                if (ModelDirsUnder(path).Count > 0) added.Add(Lang.T("voices (used where they are)"));
            }
            return string.Join(" + ", added.Distinct());
        }

        public static List<string> ModelDirsUnder(string dir)
        {
            var l = new List<string>();
            if (KokoroInstall.IsModelDir(dir)) { l.Add(dir); return l; }
            try { foreach (var d in Directory.GetDirectories(dir)) l.AddRange(ModelDirsUnder(d)); } catch { }
            return l;
        }
    }

    public partial class MainForm
    {
        KokoroInstall kokoro = new KokoroInstall();
        string kokoroPy;            // python.exe of the user's own Kokoro install, if any
        Label lblKokoro, lblKokoroPy;
        Button btnKokoro;

        GroupBox KokoroGroup()
        {
            var grp = new GroupBox { Text = "Kokoro voices (English)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Margin = new Padding(3, 12, 3, 3) };
            var g = Grid(); g.Padding = new Padding(0);
            AddFull(g, Note("Kokoro is a second, very natural-sounding engine for English. It can't read Persian: for mixed text keep a Persian main voice and choose a Kokoro voice as the English voice."));
            lblKokoro = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(3, 2, 3, 6) };
            AddFull(g, lblKokoro);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            btnKokoro = SmallButton("Get Kokoro voices…", (s, e) => ShowKokoroSetup());
            row.Controls.Add(btnKokoro);
            row.Controls.Add(SmallButton("Open Kokoro folder", (s, e) =>
            {
                string d = kokoro.ModelDir != null ? Path.GetDirectoryName(kokoro.ModelDir) : kokoro.EngineDir ?? KokoroInstaller_Root();
                try { Directory.CreateDirectory(d); } catch { }
                OpenFolder(d);
            }));
            AddFull(g, row);
            lblKokoroPy = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(3, 10, 3, 4) };
            AddFull(g, lblKokoroPy);
            AddFull(g, SmallButton("Use my Python Kokoro…", (s, e) => ChooseKokoroPython()));
            grp.Controls.Add(g);
            return grp;
        }

        static string KokoroInstaller_Root() { return KokoroInstall.InstallRoot(); }

        void ChooseKokoroPython()
        {
            using (var d = new OpenFileDialog { Title = Lang.T("Choose python.exe of your Kokoro install (for example C:\\kokoro\\.venv\\Scripts\\python.exe)"),
                                                Filter = "python.exe|python.exe|" + Lang.T("Programs") + "|*.exe" })
            {
                if (kokoroPy != null) try { d.InitialDirectory = Path.GetDirectoryName(kokoroPy); } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!KokoroPython.HasKokoro(d.FileName))
                {
                    MessageBox.Show(this, Lang.T("This Python doesn't have the kokoro package (Lib\\site-packages\\kokoro was not found next to it).\n\nChoose the python.exe inside the venv where you ran \"pip install kokoro\"."), "Piper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ini["kokoropy"] = d.FileName;
                RefreshVoices();
            }
        }

        void UpdateKokoroStatus()
        {
            if (lblKokoroPy != null)
            {
                lblKokoroPy.Text = kokoroPy != null
                    ? string.Format(Lang.T("Your Python Kokoro was found ({0}). Its voices are in the lists too, marked \"(Python)\". It only works on this PC and takes a few seconds to start."), kokoroPy)
                    : Lang.T("Already installed Kokoro with Python (pip install kokoro)? Piper Studio can use it too:");
                lblKokoroPy.ForeColor = kokoroPy != null ? Color.DarkGreen : Color.DimGray;
            }
            if (lblKokoro == null) return;
            if (kokoro.Ready)
            {
                lblKokoro.Text = string.Format(Lang.T("Installed: {0} English voices ({1}). Choose them in the voice lists above — the best ones have a ★."),
                    kokoro.Voices().Count, kokoro.IsInt8 ? Lang.T("smaller model") : Lang.T("best-quality model"));
                lblKokoro.ForeColor = Color.DarkGreen;
                btnKokoro.Text = Lang.T("Kokoro setup…");
            }
            else
            {
                lblKokoro.Text = kokoro.EngineOk ? Lang.T("The engine is here, but no Kokoro voices yet. Click the button to download them (once).")
                    : kokoro.ModelOk ? Lang.T("The voices are here, but the engine files are missing. Click the button to get them.")
                    : Lang.T("Not installed yet. One download (130–350 MB); after that it works offline.");
                lblKokoro.ForeColor = Color.DimGray;
                btnKokoro.Text = Lang.T("Get Kokoro voices…");
            }
        }

        void ShowKokoroSetup()
        {
            using (var f = new KokoroSetupForm(kokoro, Ini("kokorodir", "")))
            {
                f.ShowDialog(this);
                if (f.ExtraDir != null) ini["kokorodir"] = f.ExtraDir;
                if (f.Changed)
                {
                    RefreshVoices();
                    if (kokoro.Ready)
                    {
                        AddLog("Kokoro is ready: " + kokoro.ModelName + " in " + kokoro.ModelDir);
                        SetStatus(Lang.T("Kokoro voices are ready — pick one on the Voice tab."));
                    }
                }
            }
        }
    }

    class KokoroSetupForm : Form
    {
        public bool Changed;
        public string ExtraDir;
        readonly KokoroInstall have;
        readonly RadioButton rbSmall, rbBest;
        readonly ProgressBar bar;
        readonly Label status, where;
        readonly Button btnGo, btnLocal, btnClose;
        CancellationTokenSource cts;
        readonly string root;

        public KokoroSetupForm(KokoroInstall current, string extraDir)
        {
            have = current;
            root = KokoroInstall.InstallRoot();
            Text = "Get Kokoro voices";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            Func<string, Label> note = s => new Label { Text = s, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(3, 2, 3, 8) };
            t.Controls.Add(note("Kokoro is a free, open-source English voice engine (Apache 2.0) with 28 natural voices. Download it once — after that it works without internet."));
            rbSmall = new RadioButton { Text = "Smaller and faster — 130 MB download (best for live talking)", AutoSize = true, Checked = true, MaximumSize = new Size(470, 0) };
            rbBest = new RadioButton { Text = "Best quality — 350 MB download (slower on older PCs)", AutoSize = true, MaximumSize = new Size(470, 0) };
            t.Controls.Add(rbSmall); t.Controls.Add(rbBest);
            string haveText = current.Ready ? string.Format(Lang.T("Now installed: {0}"), current.ModelName)
                : current.EngineOk ? Lang.T("The engine is already here — only the voices will be downloaded.") : "";
            where = note((haveText != "" ? haveText + "\n" : "") + Lang.T("Saved in: ") + root);
            where.ForeColor = Color.DimGray;
            t.Controls.Add(where);
            bar = new ProgressBar { Width = 470, Height = 18, Margin = new Padding(3, 4, 3, 4) };
            t.Controls.Add(bar);
            status = note("");
            t.Controls.Add(status);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            btnGo = new Button { Text = "Download", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            btnLocal = new Button { Text = "Use files I already have…", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
            btnClose = new Button { Text = "Close", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
            row.Controls.Add(btnGo); row.Controls.Add(btnLocal); row.Controls.Add(btnClose);
            t.Controls.Add(row);
            var manual = note(Lang.T("If the download is blocked or slow, download the file in your browser or download manager from:") + "\n" +
                "  " + KokoroInstaller.SmallModelUrl + "\n(" + Lang.T("or") + " " + KokoroInstaller.BestModelUrl.Replace("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/", "…/") + ")\n" +
                Lang.T("then click \"Use files I already have…\" and pick it."));
            manual.ForeColor = Color.DimGray;
            t.Controls.Add(manual);
            Controls.Add(t);
            AcceptButton = btnGo; CancelButton = btnClose;
            btnGo.Click += (s, e) => Go();
            btnLocal.Click += (s, e) => UseLocal();
            btnClose.Click += (s, e) => { if (cts != null) cts.Cancel(); Close(); };
            FormClosing += (s, e) => { if (cts != null) cts.Cancel(); };
            Lang.Apply(this); PersianUi.Apply(this);
            if (Lang.Fa) RightToLeft = RightToLeft.Yes;
        }

        void Busy(bool b)
        {
            btnGo.Enabled = btnLocal.Enabled = rbSmall.Enabled = rbBest.Enabled = !b;
            btnClose.Text = Lang.T(b ? "Cancel" : "Close");
        }

        void Progress(double f, string s)
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                if (f < 0) bar.Style = ProgressBarStyle.Marquee;
                else { bar.Style = ProgressBarStyle.Continuous; bar.Value = Math.Max(0, Math.Min(100, (int)(f * 100))); }
                status.Text = s;
            }));
        }

        void Finish(Exception ex, string okText)
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                Busy(false);
                bar.Style = ProgressBarStyle.Continuous;
                cts = null;
                if (ex is OperationCanceledException || (ex is AggregateException && ex.InnerException is OperationCanceledException)) { status.Text = Lang.T("Cancelled."); bar.Value = 0; return; }
                if (ex != null)
                {
                    var inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    status.Text = Lang.T("It didn't work: ") + inner.Message + "\n" + Lang.T("Check your internet connection (or VPN) and try again, or use \"Use files I already have…\".");
                    status.ForeColor = Color.Firebrick;
                    return;
                }
                Changed = true;
                bar.Value = 100;
                status.ForeColor = Color.DarkGreen;
                status.Text = okText;
                btnGo.Enabled = false;
                btnClose.Text = Lang.T("Done");
            }));
        }

        void Go()
        {
            status.Text = ""; status.ForeColor = SystemColors.ControlText;
            bool best = rbBest.Checked;
            bool needEngine = !have.EngineOk;
            bool needModel = true;
            if (have.ModelOk)
            {
                bool haveBest = !have.IsInt8;
                if (haveBest == best) needModel = false;                      // that one is already here
                else if (haveBest) { needModel = false; status.Text = Lang.T("You already have the best-quality model, which is used first. To use the smaller one instead, delete the best-quality folder (Open Kokoro folder) and download again."); }
            }
            if (!needEngine && !needModel)
            {
                if (status.Text == "") status.Text = Lang.T("This model is already installed.");
                return;
            }
            Busy(true); status.ForeColor = SystemColors.ControlText;
            cts = new CancellationTokenSource(); var ct = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    KokoroInstaller.Install(root, needEngine, needModel, best, Progress, ct);
                    Finish(null, Lang.T("Done! The Kokoro voices are now in the voice lists (look for \"Kokoro\")."));
                }
                catch (Exception ex) { Finish(ex, null); }
            });
        }

        void UseLocal()
        {
            {
                // choose a downloaded archive or an unpacked folder
                string path = null;
                using (var d = new OpenFileDialog { Title = Lang.T("Choose the downloaded Kokoro file (.tar.bz2)"), Filter = Lang.T("Kokoro download") + " (*.tar.bz2)|*.tar.bz2|" + Lang.T("All files") + "|*.*" })
                {
                    var r = d.ShowDialog(this);
                    if (r == DialogResult.OK) path = d.FileName;
                }
                if (path == null)
                {
                    using (var fb = new FolderBrowserDialog { Description = Lang.T("Or choose an unpacked Kokoro folder (with model.onnx and voices.bin), or a folder with sherpa-onnx-c-api.dll:") })
                        if (fb.ShowDialog(this) == DialogResult.OK) path = fb.SelectedPath;
                }
                if (path == null) return;
                if (Directory.Exists(path))
                {
                    var models = KokoroInstaller.ModelDirsUnder(path);
                    if (models.Count > 0) ExtraDir = Path.GetDirectoryName(models[0]) ?? path;
                    if (KokoroInstall.IsModelDir(path)) ExtraDir = Path.GetDirectoryName(path);
                }
                Busy(true); status.ForeColor = SystemColors.ControlText;
                cts = new CancellationTokenSource(); var ct = cts.Token;
                string p = path;
                Task.Run(() =>
                {
                    try
                    {
                        string what = KokoroInstaller.InstallFromLocal(p, root, ct, Progress);
                        if (what == "") throw new Exception(Lang.T("No Kokoro files were found there."));
                        Finish(null, Lang.T("Added: ") + what + ".");
                    }
                    catch (Exception ex) { Finish(ex, null); }
                });
            }
        }
    }
}
