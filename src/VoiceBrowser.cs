using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    // ================================================================== voice catalogue
    // The official list of Piper voices (voices.json in rhasspy/piper-voices on Hugging Face).
    public class CatalogVoice
    {
        public string Key, Name, Quality, LangCode, LangFamily, LangEnglish, LangNative, Country, Dir;
        public int Speakers = 1;
        public long Bytes;
        public List<KeyValuePair<string, long>> Files = new List<KeyValuePair<string, long>>();   // repo path, size
        public Dictionary<string, string> Md5 = new Dictionary<string, string>();
        public string SampleUrl { get { return VoiceCatalog.Base + Dir + "/samples/speaker_0.mp3"; } }
        public string LangLabel { get { return LangEnglish + (string.IsNullOrEmpty(Country) ? "" : " (" + Country + ")"); } }
    }

    public static class VoiceCatalog
    {
        public const string Base = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
        public static string ListUrl { get { return Base + "voices.json?download=true"; } }
        static string CachePath { get { return AppPaths.In("voices-catalog.json"); } }

        public static List<CatalogVoice> Parse(string json)
        {
            var list = new List<CatalogVoice>();
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) return list;
            foreach (var kv in root)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d == null) continue;
                var v = new CatalogVoice { Key = kv.Key, Name = S(d, "name"), Quality = S(d, "quality") };
                var lang = d.ContainsKey("language") ? d["language"] as Dictionary<string, object> : null;
                if (lang != null) { v.LangCode = S(lang, "code"); v.LangFamily = S(lang, "family"); v.LangEnglish = S(lang, "name_english"); v.LangNative = S(lang, "name_native"); v.Country = S(lang, "country_english"); }
                try { if (d.ContainsKey("num_speakers")) v.Speakers = Convert.ToInt32(d["num_speakers"]); } catch { }
                var files = d.ContainsKey("files") ? d["files"] as Dictionary<string, object> : null;
                if (files != null)
                    foreach (var f in files)
                    {
                        if (!(f.Key.EndsWith(".onnx") || f.Key.EndsWith(".onnx.json"))) continue;
                        var info = f.Value as Dictionary<string, object>;
                        long size = 0; try { size = Convert.ToInt64(info["size_bytes"]); } catch { }
                        v.Files.Add(new KeyValuePair<string, long>(f.Key, size));
                        if (info != null && info.ContainsKey("md5_digest")) v.Md5[f.Key] = S(info, "md5_digest");
                        v.Bytes += size;
                        if (f.Key.EndsWith(".onnx")) v.Dir = f.Key.Substring(0, f.Key.LastIndexOf('/'));
                    }
                if (v.Dir != null && v.Files.Count >= 2) list.Add(v);
            }
            return list.OrderBy(v => v.LangFamily == "fa" ? 0 : v.LangFamily == "en" ? 1 : 2).ThenBy(v => v.LangEnglish).ThenBy(v => v.Country).ThenBy(v => v.Name).ThenBy(v => QualityRank(v.Quality)).ToList();
        }

        static int QualityRank(string q) { switch (q) { case "x_low": return 0; case "low": return 1; case "medium": return 2; case "high": return 3; default: return 4; } }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : ""; }

        static HttpClient Http(TimeSpan timeout)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var h = new HttpClient { Timeout = timeout };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("PiperStudio/" + Program.Version);
            return h;
        }

        // Downloads the list (and caches it). Falls back to the last saved copy when offline.
        public static List<CatalogVoice> Load(out bool fromCache, string urlOverride = null)
        {
            fromCache = false;
            try
            {
                using (var http = Http(TimeSpan.FromSeconds(25)))
                {
                    string json = http.GetStringAsync(urlOverride ?? ListUrl).Result;
                    var list = Parse(json);
                    if (list.Count > 0) { try { File.WriteAllText(CachePath, json, new UTF8Encoding(false)); } catch { } return list; }
                }
            }
            catch { }
            if (File.Exists(CachePath)) { fromCache = true; return Parse(File.ReadAllText(CachePath, Encoding.UTF8)); }
            throw new Exception(Lang.T("The voice list couldn't be downloaded from Hugging Face. Check your internet connection (in Iran a VPN may be needed) and try again."));
        }

        public static bool Installed(CatalogVoice v, IEnumerable<string> folders)
        {
            foreach (var f in folders)
                try { if (File.Exists(Path.Combine(f, v.Key + ".onnx")) && File.Exists(Path.Combine(f, v.Key + ".onnx.json"))) return true; } catch { }
            return false;
        }

        // Downloads the .onnx and .onnx.json into folder, checking size and MD5.
        public static void Download(CatalogVoice v, string folder, Action<double, string> progress, CancellationToken ct, string baseOverride = null)
        {
            Directory.CreateDirectory(folder);
            long total = Math.Max(1, v.Bytes), done = 0;
            using (var http = Http(TimeSpan.FromHours(2)))
                foreach (var f in v.Files.OrderBy(x => x.Key.EndsWith(".json") ? 0 : 1))
                {
                    string name = Path.GetFileName(f.Key);
                    string dest = Path.Combine(folder, name), part = dest + ".part";
                    using (var resp = http.GetAsync((baseOverride ?? Base) + f.Key + "?download=true", HttpCompletionOption.ResponseHeadersRead, ct).Result)
                    {
                        resp.EnsureSuccessStatusCode();
                        using (var src = resp.Content.ReadAsStreamAsync().Result)
                        using (var dst = File.Create(part))
                        using (var md5 = MD5.Create())
                        {
                            var buf = new byte[1 << 16]; int n; var sw = Stopwatch.StartNew(); long last = 0;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                dst.Write(buf, 0, n); md5.TransformBlock(buf, 0, n, null, 0); done += n;
                                if (sw.ElapsedMilliseconds - last > 250)
                                {
                                    last = sw.ElapsedMilliseconds;
                                    progress(Math.Min(1, done / (double)total), string.Format("{0:0.0} / {1:0.0} MB  ·  {2:0.0} MB/s", done / 1048576.0, total / 1048576.0, done / 1048576.0 / Math.Max(0.5, sw.Elapsed.TotalSeconds)));
                                }
                            }
                            md5.TransformFinalBlock(new byte[0], 0, 0);
                            string got = BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
                            string want;
                            if (v.Md5.TryGetValue(f.Key, out want) && want != "" && want != got)
                            { dst.Close(); try { File.Delete(part); } catch { } throw new Exception(Lang.T("The download of ") + name + Lang.T(" was damaged (checksum mismatch). Please try again.")); }
                        }
                    }
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(part, dest);
                }
        }

        public static byte[] DownloadBytes(string url)
        {
            using (var http = Http(TimeSpan.FromSeconds(30))) return http.GetByteArrayAsync(url).Result;
        }
    }

    // ================================================================== voice browser window
    public class VoiceBrowserForm : Form
    {
        readonly Func<string> getFfmpeg;
        readonly Func<IEnumerable<string>> getFolders;
        readonly string installFolder;
        public bool Changed;
        List<CatalogVoice> all = new List<CatalogVoice>();
        readonly TextBox search = new TextBox { Width = 220 };
        readonly ComboBox lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        readonly ProgressBar bar = new ProgressBar { Width = 260, Height = 16, Margin = new Padding(3, 8, 3, 3) };
        readonly Label status = new Label { AutoSize = true, Margin = new Padding(6, 8, 3, 3), MaximumSize = new Size(700, 0) };
        Button bListen, bDownload, bDelete;
        CancellationTokenSource cts, playCts;
        public string UrlOverride, BaseOverride;   // tests

        public VoiceBrowserForm(Func<string> getFfmpeg, Func<IEnumerable<string>> getFolders, string installFolder)
        {
            this.getFfmpeg = getFfmpeg; this.getFolders = getFolders; this.installFolder = installFolder;
            Text = "Get more voices"; Size = new Size(900, 560); MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
            top.Controls.Add(new Label { Text = "Language:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            top.Controls.Add(lang);
            top.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) });
            top.Controls.Add(search);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
            bListen = Btn("▶ Listen to a sample", (s, e) => Listen());
            bDownload = Btn("⬇ Download", (s, e) => DownloadSelected());
            bDelete = Btn("Delete", (s, e) => DeleteSelected());
            bottom.Controls.AddRange(new Control[] { bListen, bDownload, bDelete, Btn("Open voices folder", (s, e) => { try { Directory.CreateDirectory(installFolder); Process.Start("explorer.exe", "\"" + installFolder + "\""); } catch { } }), bar, status });
            var note = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 40, ForeColor = Color.DimGray, Padding = new Padding(8, 2, 8, 2),
                Text = "Voices come from the official Piper collection on Hugging Face. \"medium\" voices are a good balance; \"high\" sound best but are bigger and slower. Each voice has its own license — see its page on Hugging Face before using it commercially." };
            list.Columns.Add(Lang.T("Language"), 190); list.Columns.Add(Lang.T("Voice"), 150); list.Columns.Add(Lang.T("Quality"), 80); list.Columns.Add(Lang.T("Speakers"), 70); list.Columns.Add(Lang.T("Size"), 80); list.Columns.Add("", 110);
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.DoubleClick += (s, e) => Listen();
            Controls.Add(list); Controls.Add(note); Controls.Add(bottom); Controls.Add(top);
            search.TextChanged += (s, e) => Fill();
            lang.SelectedIndexChanged += (s, e) => Fill();
            FormClosing += (s, e) => { if (cts != null) cts.Cancel(); if (playCts != null) playCts.Cancel(); };
            Shown += (s, e) => LoadList();
            Lang.Apply(this); PersianUi.Apply(this);
            if (Lang.Fa) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            UpdateButtons();
        }

        static Button Btn(string t, EventHandler h) { var b = new Button { Text = t, AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3) }; b.Click += h; return b; }

        void SetStatus(string s, Color c) { status.Text = s; status.ForeColor = c; }

        void LoadList()
        {
            SetStatus(Lang.T("Loading the voice list…"), SystemColors.ControlText);
            bar.Style = ProgressBarStyle.Marquee;
            Task.Run(() =>
            {
                try
                {
                    bool cached;
                    var l = VoiceCatalog.Load(out cached, UrlOverride);
                    BeginInvoke(new Action(() =>
                    {
                        all = l; bar.Style = ProgressBarStyle.Continuous;
                        var langs = all.Select(v => v.LangLabel).Distinct().OrderBy(x => x).ToList();
                        lang.Items.Clear();
                        lang.Items.Add(Lang.T("Persian and English")); lang.Items.Add(Lang.T("All languages"));
                        foreach (var x in langs) lang.Items.Add(x);
                        lang.SelectedIndex = 0;
                        SetStatus(string.Format(Lang.T("{0} voices in {1} languages."), all.Count, all.Select(v => v.LangFamily).Distinct().Count()) + (cached ? " " + Lang.T("(offline — saved list)") : ""), cached ? Color.DarkOrange : Color.DimGray);
                    }));
                }
                catch (Exception ex) { BeginInvoke(new Action(() => { bar.Style = ProgressBarStyle.Continuous; SetStatus(ex.InnerException != null ? ex.InnerException.Message : ex.Message, Color.Firebrick); })); }
            });
        }

        void Fill()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            int li = lang.SelectedIndex;
            string ls = lang.SelectedItem as string;
            var folders = getFolders().ToList();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var v in all)
            {
                if (li == 0 && v.LangFamily != "fa" && v.LangFamily != "en") continue;
                if (li >= 2 && v.LangLabel != ls) continue;
                if (q != "" && !(v.Key + " " + v.LangEnglish + " " + v.LangNative + " " + v.Country).ToLowerInvariant().Contains(q)) continue;
                bool inst = VoiceCatalog.Installed(v, folders);
                var it = new ListViewItem(new[] { v.LangLabel, v.Name, v.Quality, v.Speakers.ToString(), (v.Bytes / 1048576.0).ToString("0") + " MB", inst ? "✔ " + Lang.T("installed") : "" }) { Tag = v };
                if (inst) it.ForeColor = Color.DarkGreen;
                if (v.LangFamily == "fa") it.Font = new Font(list.Font, FontStyle.Bold);
                list.Items.Add(it);
            }
            list.EndUpdate();
            UpdateButtons();
        }

        CatalogVoice Selected { get { return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as CatalogVoice : null; } }

        void UpdateButtons()
        {
            var v = Selected;
            bool busy = cts != null;
            bool inst = v != null && VoiceCatalog.Installed(v, getFolders());
            bListen.Enabled = v != null;
            bDownload.Enabled = v != null && !inst && !busy;
            bDelete.Enabled = v != null && inst && !busy && File.Exists(Path.Combine(installFolder, v.Key + ".onnx"));
            bDownload.Text = busy ? Lang.T("Downloading…") : Lang.T("⬇ Download");
        }

        void Listen()
        {
            var v = Selected; if (v == null) return;
            if (playCts != null) { playCts.Cancel(); playCts = null; }
            string ff = getFfmpeg();
            if (ff == null || !WaveOut.Available) { try { Process.Start(v.SampleUrl); } catch { } return; }   // the browser can play it
            var c = playCts = new CancellationTokenSource();
            SetStatus(Lang.T("Loading the sample…"), SystemColors.ControlText);
            Task.Run(() =>
            {
                string mp3 = Path.Combine(Path.GetTempPath(), "ps_sample_" + Guid.NewGuid().ToString("N") + ".mp3"), wav = Path.ChangeExtension(mp3, ".wav");
                try
                {
                    File.WriteAllBytes(mp3, VoiceCatalog.DownloadBytes(v.SampleUrl));
                    var psi = new ProcessStartInfo(ff, "-y -loglevel error -i \"" + mp3 + "\" -ac 1 -ar 48000 \"" + wav + "\"") { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit(30000);
                    var pcm = Audio.ReadWavPcm(wav);
                    BeginInvoke(new Action(() => SetStatus(Lang.T("Playing the sample of ") + v.Key + "…", SystemColors.ControlText)));
                    WaveOut.PlaySync(pcm, 48000, new List<int> { -1 }, c.Token);
                    BeginInvoke(new Action(() => SetStatus("", SystemColors.ControlText)));
                }
                catch (Exception ex) { BeginInvoke(new Action(() => SetStatus(Lang.T("The sample couldn't be played: ") + (ex.InnerException ?? ex).Message, Color.Firebrick))); }
                finally { try { File.Delete(mp3); File.Delete(wav); } catch { } }
            });
        }

        void DownloadSelected()
        {
            var v = Selected; if (v == null || cts != null) return;
            var c = cts = new CancellationTokenSource();
            UpdateButtons();
            SetStatus(Lang.T("Downloading ") + v.Key + "…", SystemColors.ControlText);
            Task.Run(() =>
            {
                try
                {
                    VoiceCatalog.Download(v, installFolder, (f, s) => BeginInvoke(new Action(() => { bar.Value = (int)(f * 100); SetStatus(v.Key + "  " + s, SystemColors.ControlText); })), c.Token, BaseOverride);
                    BeginInvoke(new Action(() => { cts = null; Changed = true; bar.Value = 100; SetStatus(string.Format(Lang.T("{0} is installed — choose it on the Voice tab."), v.Key), Color.DarkGreen); Fill(); }));
                }
                catch (Exception ex)
                {
                    var inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    BeginInvoke(new Action(() => { cts = null; bar.Value = 0; SetStatus(inner is OperationCanceledException ? Lang.T("Cancelled.") : Lang.T("It didn't work: ") + inner.Message + " " + Lang.T("(in Iran a VPN may be needed for Hugging Face)"), Color.Firebrick); UpdateButtons(); }));
                }
            });
        }

        void DeleteSelected()
        {
            var v = Selected; if (v == null) return;
            if (MessageBox.Show(this, string.Format(Lang.T("Delete the voice {0}?"), v.Key), "Piper Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var ext in new[] { ".onnx", ".onnx.json" }) try { File.Delete(Path.Combine(installFolder, v.Key + ext)); } catch { }
            Changed = true; Fill();
        }
    }
}
