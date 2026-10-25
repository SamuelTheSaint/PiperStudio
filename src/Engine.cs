using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PiperStudio
{
    // ---------------------------------------------------------------- voices
    public class VoiceInfo
    {
        public string OnnxPath, JsonPath, FileName, Dataset = "", Quality = "", LangCode = "", LangFamily = "", LangName = "", EspeakVoice = "";
        public int SampleRate = 22050, NumSpeakers = 1;
        public string LoadError;
        public double NoiseScale = 0.667, LengthScale = 1.0, NoiseW = 0.8;
        public List<KeyValuePair<string, int>> Speakers = new List<KeyValuePair<string, int>>();
        public bool IsPersian { get { return LangFamily == "fa"; } }

        public override string ToString()
        {
            string lang = LangName != "" ? LangName : LangCode;
            string s = lang + " · " + (Dataset != "" ? Dataset : FileName);
            if (Quality != "") s += " (" + Quality + ")";
            if (NumSpeakers > 1) s += " · " + NumSpeakers + " speakers";
            return s;
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object o; return d != null && d.TryGetValue(key, out o) ? o : null;
        }

        public static VoiceInfo Load(string onnx)
        {
            string json = onnx + ".json";
            if (!File.Exists(json))
            {
                string alt = Path.ChangeExtension(onnx, ".json");
                if (File.Exists(alt)) json = alt; else return null;
            }
            onnx = Path.GetFullPath(onnx); json = Path.GetFullPath(json);
            var v = new VoiceInfo { OnnxPath = onnx, JsonPath = json, FileName = Path.GetFileNameWithoutExtension(onnx) };
            try
            {
                var root = MiniJson.Parse(File.ReadAllText(json, Encoding.UTF8)) as Dictionary<string, object>;
                v.Dataset = Convert.ToString(Get(root, "dataset") ?? "");
                var audio = Get(root, "audio") as Dictionary<string, object>;
                if (Get(audio, "sample_rate") != null) v.SampleRate = Convert.ToInt32(Get(audio, "sample_rate"));
                v.Quality = Convert.ToString(Get(audio, "quality") ?? "");
                var es = Get(root, "espeak") as Dictionary<string, object>;
                v.EspeakVoice = Convert.ToString(Get(es, "voice") ?? "");
                var lang = Get(root, "language") as Dictionary<string, object>;
                v.LangCode = Convert.ToString(Get(lang, "code") ?? "");
                v.LangFamily = Convert.ToString(Get(lang, "family") ?? "");
                v.LangName = Convert.ToString(Get(lang, "name_english") ?? "");
                if (v.LangFamily == "" && v.EspeakVoice != "") v.LangFamily = v.EspeakVoice.Split('-')[0].ToLowerInvariant();
                var inf = Get(root, "inference") as Dictionary<string, object>;
                if (Get(inf, "noise_scale") != null) v.NoiseScale = Convert.ToDouble(Get(inf, "noise_scale"), CultureInfo.InvariantCulture);
                if (Get(inf, "length_scale") != null) v.LengthScale = Convert.ToDouble(Get(inf, "length_scale"), CultureInfo.InvariantCulture);
                if (Get(inf, "noise_w") != null) v.NoiseW = Convert.ToDouble(Get(inf, "noise_w"), CultureInfo.InvariantCulture);
                if (Get(root, "num_speakers") != null) v.NumSpeakers = Convert.ToInt32(Get(root, "num_speakers"));
                var map = Get(root, "speaker_id_map") as Dictionary<string, object>;
                if (map != null)
                    foreach (var kv in map) v.Speakers.Add(new KeyValuePair<string, int>(kv.Key, Convert.ToInt32(kv.Value)));
                v.Speakers.Sort((a, b) => a.Value.CompareTo(b.Value));
                if (v.Speakers.Count == 0 && v.NumSpeakers > 1)
                    for (int i = 0; i < v.NumSpeakers; i++) v.Speakers.Add(new KeyValuePair<string, int>("speaker " + i, i));
            }
            catch (Exception ex) { v.LoadError = ex.Message; }
            return v;
        }

        public static List<VoiceInfo> Scan(IEnumerable<string> folders)
        {
            var list = new List<VoiceInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in folders)
            {
                if (!Directory.Exists(f)) continue;
                IEnumerable<string> files;
                try { files = Directory.GetFiles(f, "*.onnx", SearchOption.AllDirectories); } catch { continue; }
                foreach (var onnx in files)
                {
                    if (!seen.Add(Path.GetFullPath(onnx))) continue;
                    var v = Load(onnx);
                    if (v != null) list.Add(v);
                }
            }
            list.Sort((a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }

    public class SynthParams
    {
        public double LengthScale = 1, NoiseScale = 0.667, NoiseW = 0.8, SentenceSilence = 0.2;
    }

    // ---------------------------------------------------------------- piper process
    // Keeps piper.exe running with the voice already loaded ("warm"), so each sentence
    // only costs synthesis time instead of starting piper and loading the model again.
    class LiveProc
    {
        public string Key;
        public Process Proc;
        public readonly object Lock = new object();
        public bool InUse;
        public StringBuilder Err = new StringBuilder();
        public int Counter;
        public bool Alive { get { try { return Proc != null && !Proc.HasExited; } catch { return false; } } }
    }

    public class PiperRunner
    {
        public string PiperExe;
        public Action<string> Log;
        readonly object poolLock = new object();
        readonly Dictionary<string, LiveProc> pool = new Dictionary<string, LiveProc>(); // one per voice file

        static string Inv(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        // Stop button: kill only processes that are busy right now (idle warm ones stay loaded).
        public void Kill()
        {
            lock (poolLock)
                foreach (var lp in pool.Values)
                    if (lp.InUse) try { lp.Proc.Kill(); } catch { }
        }

        public void Shutdown()
        {
            lock (poolLock)
            {
                foreach (var lp in pool.Values) try { lp.Proc.Kill(); } catch { }
                pool.Clear();
            }
        }

        string FinglishConfig(VoiceInfo v)
        {
            string dir = Path.Combine(Path.GetTempPath(), "PiperStudio");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, v.FileName + ".latin.onnx.json");
            string src = File.ReadAllText(v.JsonPath, Encoding.UTF8);
            var rx = new Regex("\"voice\"\\s*:\\s*\"[^\"]*\"");
            string fixedJson = rx.Replace(src, "\"voice\": \"" + v.LangFamily + "-Latn\"", 1);
            if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != fixedJson)
                File.WriteAllText(path, fixedJson, new UTF8Encoding(false));
            return path;
        }

        string Args(VoiceInfo v, int speaker, bool latin, SynthParams p)
        {
            string cfg = latin ? FinglishConfig(v) : v.JsonPath;
            var a = new StringBuilder();
            a.AppendFormat("--model \"{0}\" --config \"{1}\" --json-input -q", v.OnnxPath, cfg);
            a.Append(" --length_scale " + Inv(p.LengthScale));
            a.Append(" --noise_scale " + Inv(p.NoiseScale));
            a.Append(" --noise_w " + Inv(p.NoiseW));
            a.Append(" --sentence_silence " + Inv(p.SentenceSilence));
            if (v.NumSpeakers > 1) a.Append(" --speaker " + speaker);
            return a.ToString();
        }

        LiveProc Start(string args, string key)
        {
            if (string.IsNullOrEmpty(PiperExe) || !File.Exists(PiperExe))
                throw new Exception("piper.exe was not found. Set its location on the Voice tab.");
            var psi = new ProcessStartInfo(PiperExe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(PiperExe))
            };
            var lp = new LiveProc { Key = key, Proc = new Process { StartInfo = psi } };
            lp.Proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (lp.Err) { if (lp.Err.Length > 8000) lp.Err.Clear(); lp.Err.AppendLine(e.Data); } };
            lp.Proc.Start();
            lp.Proc.BeginErrorReadLine();
            if (Log != null) Log("Started piper (voice loading in background)");
            return lp;
        }

        LiveProc Get(VoiceInfo v, int speaker, bool latin, SynthParams p)
        {
            string args = Args(v, speaker, latin, p);
            string slot = v.OnnxPath + (latin ? "|latin" : "");
            lock (poolLock)
            {
                LiveProc lp;
                if (pool.TryGetValue(slot, out lp))
                {
                    if (lp.Key == args && lp.Alive) return lp;
                    if (!lp.InUse) try { lp.Proc.Kill(); } catch { }
                    pool.Remove(slot);
                }
                // keep memory in check: at most 3 voices loaded
                if (pool.Count >= 3)
                {
                    var old = pool.FirstOrDefault(kv => !kv.Value.InUse);
                    if (old.Value != null) { try { old.Value.Proc.Kill(); } catch { } pool.Remove(old.Key); }
                }
                lp = Start(args, args);
                pool[slot] = lp;
                return lp;
            }
        }

        // Load a voice ahead of time so the first sentence is fast.
        public void Warm(VoiceInfo v, int speaker, bool latin, SynthParams p)
        {
            try { if (v != null) Get(v, speaker, latin, p); } catch { }
        }

        static string JsonEscape(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // Returns 16-bit mono PCM at the voice's sample rate.
        public short[] Synthesize(VoiceInfo v, int speaker, bool latinScript, SynthParams p, string text, CancellationToken ct)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return new short[0];
            ct.ThrowIfCancellationRequested();
            for (int attempt = 0; ; attempt++)
            {
                var lp = Get(v, speaker, latinScript, p);
                lock (lp.Lock)
                {
                    try
                    {
                        lp.InUse = true;
                        ct.ThrowIfCancellationRequested();
                        string dir = Path.Combine(Path.GetTempPath(), "PiperStudio");
                        Directory.CreateDirectory(dir);
                        string wav = Path.Combine(dir, "live_" + lp.Proc.Id + "_" + (lp.Counter++) + ".wav");
                        string line = "{\"text\": " + JsonEscape(text) + ", \"output_file\": " + JsonEscape(wav) + "}\n";
                        // UTF-8 bytes straight to stdin, so Persian survives.
                        byte[] bytes = new UTF8Encoding(false).GetBytes(line);
                        var stdin = lp.Proc.StandardInput.BaseStream;
                        stdin.Write(bytes, 0, bytes.Length);
                        stdin.Flush();
                        // piper prints the file path when the file is finished
                        var stdout = lp.Proc.StandardOutput.BaseStream;
                        int b;
                        while ((b = stdout.ReadByte()) != -1 && b != '\n') { }
                        ct.ThrowIfCancellationRequested();
                        if (b == -1 || !File.Exists(wav))
                        {
                            string e; lock (lp.Err) e = lp.Err.ToString();
                            lock (poolLock) { foreach (var k in pool.Where(kv => kv.Value == lp).Select(kv => kv.Key).ToList()) pool.Remove(k); }
                            if (attempt == 0 && e.Trim().Length == 0) continue; // process died quietly: try once more
                            var lines = e.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            throw new Exception("Piper failed.\n" + string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 6))));
                        }
                        var samples = Audio.ReadWavPcm(wav);
                        try { File.Delete(wav); } catch { }
                        string tag = (v.NumSpeakers > 1 ? " · speaker " + speaker : "") + (latinScript ? " · Finglish" : "");
                        if (Log != null) Log(string.Format("{0}{1}  {2:0.0}s  «{3}»", v.FileName, tag,
                            samples.Length / (double)v.SampleRate, text.Length > 60 ? text.Substring(0, 60) + "…" : text));
                        return samples;
                    }
                    catch (IOException)
                    {
                        ct.ThrowIfCancellationRequested();
                        lock (poolLock) { foreach (var k in pool.Where(kv => kv.Value == lp).Select(kv => kv.Key).ToList()) pool.Remove(k); }
                        if (attempt == 0) continue;
                        throw;
                    }
                    finally { lp.InUse = false; }
                }
            }
        }
    }

    // ---------------------------------------------------------------- audio helpers
    public static class Audio
    {
        public static short[] Silence(double seconds, int rate)
        {
            return new short[Math.Max(0, (int)(seconds * rate))];
        }

        public static short[] Concat(List<short[]> parts)
        {
            int n = 0; foreach (var p in parts) n += p.Length;
            var r = new short[n]; int o = 0;
            foreach (var p in parts) { Array.Copy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }

        public static short[] Resample(short[] s, int from, int to)
        {
            if (from == to || s.Length == 0) return s;
            long n = (long)s.Length * to / from;
            var r = new short[n];
            double step = (double)from / to;
            for (long i = 0; i < n; i++)
            {
                double pos = i * step; int a = (int)pos; double f = pos - a;
                int b = Math.Min(a + 1, s.Length - 1);
                r[i] = (short)(s[a] + (s[b] - s[a]) * f);
            }
            return r;
        }

        public static void ApplyGain(short[] s, double g)
        {
            if (Math.Abs(g - 1) < 0.001) return;
            for (int i = 0; i < s.Length; i++)
            {
                double x = s[i] * g;
                s[i] = (short)(x > 32767 ? 32767 : x < -32768 ? -32768 : x);
            }
        }

        public static short[] Trim(short[] s, int threshold = 250)
        {
            int a = 0, b = s.Length - 1;
            while (a < s.Length && Math.Abs((int)s[a]) < threshold) a++;
            while (b > a && Math.Abs((int)s[b]) < threshold) b--;
            if (a >= s.Length) return new short[0];
            int pad = 200;
            a = Math.Max(0, a - pad); b = Math.Min(s.Length - 1, b + pad);
            var r = new short[b - a + 1]; Array.Copy(s, a, r, 0, r.Length); return r;
        }

        public static short[] ReadWavPcm(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "data")
                {
                    len = Math.Min(len < 0 ? int.MaxValue : len, b.Length - pos - 8);
                    var r = new short[len / 2];
                    Buffer.BlockCopy(b, pos + 8, r, 0, r.Length * 2);
                    return r;
                }
                pos += 8 + len + (len & 1);
            }
            return new short[0];
        }

        public static byte[] ToWav(short[] s, int rate)
        {
            var ms = new MemoryStream();
            WriteWav(ms, s, rate);
            return ms.ToArray();
        }

        public static void WriteWav(Stream st, short[] s, int rate)
        {
            var w = new BinaryWriter(st);
            int dataLen = s.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
            var buf = new byte[dataLen]; Buffer.BlockCopy(s, 0, buf, 0, dataLen); w.Write(buf);
            w.Flush();
        }

        public static void WriteWav(string path, short[] s, int rate)
        {
            using (var f = File.Create(path)) WriteWav(f, s, rate);
        }
    }

    // ---------------------------------------------------------------- text
    public class Chunk { public int Start, Length; public string Text; }

    public static class TextTools
    {
        public static bool IsPersianLetter(char c)
        {
            return (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) || (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);
        }
        public static bool IsLatinLetter(char c) { return c < 0x0250 && char.IsLetter(c); }

        public static bool StartsRtl(string s)
        {
            foreach (char c in s)
            {
                if (IsPersianLetter(c) && char.IsLetter(c)) return true;
                if (IsLatinLetter(c)) return false;
            }
            return false;
        }

        // Paragraphs = lines. Very long lines get cut at sentence ends so preview starts quickly.
        public static List<Chunk> MakeChunks(string text, int offset)
        {
            var list = new List<Chunk>();
            int i = 0;
            while (i < text.Length)
            {
                int nl = text.IndexOf('\n', i);
                int end = nl < 0 ? text.Length : nl;
                AddLine(list, text, i, end, offset);
                i = end + 1;
            }
            return list;
        }

        static void AddLine(List<Chunk> list, string text, int a, int b, int offset)
        {
            const int maxLen = 450;
            while (a < b)
            {
                int end = b;
                if (b - a > maxLen)
                {
                    int cut = -1;
                    for (int k = a + maxLen; k > a + 80; k--)
                        if (".!?؟…;؛".IndexOf(text[k]) >= 0) { cut = k + 1; break; }
                    if (cut < 0) { cut = text.LastIndexOf(' ', a + maxLen, maxLen - 80); if (cut <= a) cut = a + maxLen; }
                    end = cut;
                }
                string t = text.Substring(a, end - a);
                if (t.Trim().Length > 0) list.Add(new Chunk { Start = offset + a, Length = end - a, Text = t });
                a = end;
            }
        }

        // Sentence-sized pieces (for "Say now", so playback starts after the first sentence).
        public static List<Chunk> MakeSentences(string text)
        {
            var list = new List<Chunk>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                bool end = text[i] == '\n' || (".!?؟…".IndexOf(text[i]) >= 0 && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])));
                if (end || i == text.Length - 1)
                {
                    string t = text.Substring(start, i - start + 1);
                    if (t.Trim().Length > 0) list.Add(new Chunk { Start = start, Length = i - start + 1, Text = t });
                    start = i + 1;
                }
            }
            return list;
        }

        public static string NormalizePersian(string s)
        {
            return s.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace("ـ", "");
        }

        public static List<KeyValuePair<string, string>> ParseReplacements(string rules)
        {
            var r = new List<KeyValuePair<string, string>>();
            foreach (var raw in (rules ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string from = line.Substring(0, eq).Trim(), to = line.Substring(eq + 1).Trim();
                if (from.Length == 0) continue;
                r.Add(new KeyValuePair<string, string>(from, to));
            }
            return r;
        }

        // Replacement rules match whole words only ("AI" doesn't touch "MAIN"), and use "smart case":
        // a rule written in lowercase matches any case (pixune → Pixune, PIXUNE); a rule with capitals matches exactly.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> ruleCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Regex>();

        public static Regex RuleRegex(string from)
        {
            return ruleCache.GetOrAdd(from, f =>
            {
                string pat = Regex.Escape(f);
                if (char.IsLetterOrDigit(f[0])) pat = @"(?<![\p{L}\p{N}\p{M}])" + pat;
                if (char.IsLetterOrDigit(f[f.Length - 1])) pat = pat + @"(?![\p{L}\p{N}\p{M}])";
                bool hasUpper = false;
                foreach (char c in f) if (char.IsUpper(c)) { hasUpper = true; break; }
                var opts = RegexOptions.CultureInvariant | (hasUpper ? RegexOptions.None : RegexOptions.IgnoreCase);
                return new Regex(pat, opts);
            });
        }

        public static string ApplyRules(string text, List<KeyValuePair<string, string>> rules)
        {
            if (rules == null) return text;
            foreach (var kv in rules)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                string to = kv.Value ?? "";
                text = RuleRegex(kv.Key).Replace(text, m => to);
            }
            return text;
        }

        // [pause] = 1 s, [pause 2.5] = 2.5 s
        public static readonly Regex PauseRx = new Regex(@"\[\s*(?:pause|مکث)\s*([0-9]+(?:[.,][0-9]+)?)?\s*s?\s*\]", RegexOptions.IgnoreCase);

        public class Seg { public string Text; public bool Latin; }

        // Splits mixed text into runs of Latin-script and Persian-script words.
        public static List<Seg> SplitByScript(string s)
        {
            var segs = new List<Seg>();
            var sb = new StringBuilder();
            int cur = -1; // -1 unknown, 0 persian/other, 1 latin
            foreach (char c in s)
            {
                int cls = IsLatinLetter(c) ? 1 : (char.IsLetter(c) ? 0 : -1);
                if (cls >= 0 && cur >= 0 && cls != cur)
                {
                    // move trailing spaces/punctuation that belong between the two runs
                    segs.Add(new Seg { Text = sb.ToString(), Latin = cur == 1 });
                    sb.Clear();
                }
                if (cls >= 0) cur = cls;
                sb.Append(c);
            }
            if (sb.Length > 0) segs.Add(new Seg { Text = sb.ToString(), Latin = cur == 1 });
            return segs;
        }
    }

    // ---------------------------------------------------------------- one job (a snapshot of the UI settings)
    public class Job
    {
        public VoiceInfo Main, Second;
        public int MainSpeaker, SecondSpeaker;
        public bool Finglish, Mixed, Normalize;
        public SynthParams Params = new SynthParams();
        public double ParagraphPause = 0.5, Gain = 1.0;
        public List<KeyValuePair<string, string>> Replacements = new List<KeyValuePair<string, string>>();
        public int Rate { get { return Main.SampleRate; } }
    }

    public static class Synth
    {
        static bool EndsSentence(string s)
        {
            s = s.TrimEnd();
            return s.Length > 0 && ".!?؟…;؛:".IndexOf(s[s.Length - 1]) >= 0;
        }

        public static string Prepare(string text, Job job)
        {
            if (job.Normalize) text = TextTools.NormalizePersian(text);
            text = TextTools.ApplyRules(text, job.Replacements);
            return text;
        }

        // Synthesizes one chunk (a paragraph) including pause markers and voice switching.
        public static short[] Chunk(PiperRunner runner, string text, Job job, bool addParagraphPause, CancellationToken ct)
        {
            text = Prepare(text, job);
            var parts = new List<short[]>();
            int pos = 0;
            foreach (Match m in TextTools.PauseRx.Matches(text))
            {
                Speak(runner, text.Substring(pos, m.Index - pos), job, parts, ct);
                double sec = 1;
                if (m.Groups[1].Success) double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out sec);
                parts.Add(Audio.Silence(Math.Min(sec, 60), job.Rate));
                pos = m.Index + m.Length;
            }
            Speak(runner, text.Substring(pos), job, parts, ct);
            if (addParagraphPause) parts.Add(Audio.Silence(job.ParagraphPause, job.Rate));
            var all = Audio.Concat(parts);
            Audio.ApplyGain(all, job.Gain);
            return all;
        }

        static bool HasWords(string s) { foreach (char c in s) if (char.IsLetterOrDigit(c)) return true; return false; }

        static void Speak(PiperRunner runner, string text, Job job, List<short[]> parts, CancellationToken ct)
        {
            if (!HasWords(text)) return;
            bool persianMain = job.Main.IsPersian;
            if (job.Mixed && job.Second != null && !job.Finglish)
            {
                var segs = TextTools.SplitByScript(text);
                // merge segments with no words into neighbours
                for (int i = 0; i < segs.Count; i++)
                {
                    if (!HasWords(segs[i].Text)) continue;
                    var seg = segs[i];
                    var v = seg.Latin ? job.Second : job.Main;
                    int spk = seg.Latin ? job.SecondSpeaker : job.MainSpeaker;
                    var pcm = runner.Synthesize(v, spk, false, job.Params, seg.Text, ct);
                    pcm = Audio.Resample(pcm, v.SampleRate, job.Rate);
                    if (segs.Count > 1) pcm = Audio.Trim(pcm);
                    parts.Add(pcm);
                    if (segs.Count > 1)
                        parts.Add(Audio.Silence(EndsSentence(seg.Text) ? job.Params.SentenceSilence : 0.07, job.Rate));
                }
            }
            else
            {
                bool latin = job.Finglish && persianMain;
                parts.Add(runner.Synthesize(job.Main, job.MainSpeaker, latin, job.Params, text, ct));
            }
        }
    }
}
