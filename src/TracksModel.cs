using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PiperStudio
{
    // A piece of audio placed on a track. Audio data is never modified; trims and splits only
    // change In/Out, so clips can share the same data (cheap undo, cheap splits).
    public class Clip
    {
        public short[] Data;        // mono, or the left channel
        public short[] DataR;       // right channel; null = mono
        public long Start;          // position on the timeline, in samples
        public long In, Out;        // used part of Data: [In, Out)
        public float Gain = 1f;
        public double FadeIn, FadeOut;   // seconds
        public string Name = "";
        public string Text;         // for speech clips: the text it was made from
        public string Kind = "audio";    // "speech", "audio", "recording"

        public long Length { get { return Out - In; } }
        public long End { get { return Start + Length; } }
        public bool Stereo { get { return DataR != null; } }
        public Clip Clone() { return (Clip)MemberwiseClone(); }

        public static Clip FromData(short[] data, long start, string name, string kind)
        {
            return new Clip { Data = data, Start = start, In = 0, Out = data.Length, Name = name, Kind = kind };
        }

        public static Clip FromStereo(short[] left, short[] right, long start, string name, string kind)
        {
            var c = FromData(left, start, name, kind);
            if (right != null && right.Length == left.Length) c.DataR = right;
            return c;
        }
    }

    public class Track
    {
        public string Name = "Track";
        public float Volume = 1f;
        public float Pan;            // -1 = left, 0 = centre, 1 = right
        public bool Mute, Solo;
        public bool Duck;            // get quieter automatically while other tracks play
        public float DuckDb = 12f;   // how much quieter, in dB
        public List<Clip> Clips = new List<Clip>();

        public Track Clone()
        {
            var t = (Track)MemberwiseClone();
            t.Clips = Clips.Select(c => c.Clone()).ToList();
            return t;
        }
    }

    public class Project
    {
        public int Rate = 48000;
        public List<Track> Tracks = new List<Track>();
        public double DuckAttack = 0.25, DuckRelease = 0.6, DuckBridge = 0.8;   // seconds

        public long Length
        {
            get
            {
                long m = 0;
                foreach (var t in Tracks) foreach (var c in t.Clips) if (c.End > m) m = c.End;
                return m;
            }
        }

        public Project Clone()
        {
            return new Project { Rate = Rate, DuckAttack = DuckAttack, DuckRelease = DuckRelease, DuckBridge = DuckBridge, Tracks = Tracks.Select(t => t.Clone()).ToList() };
        }

        public bool Audible(Track t)
        {
            bool anySolo = false;
            foreach (var k in Tracks) if (k.Solo) { anySolo = true; break; }
            return !t.Mute && (!anySolo || t.Solo);
        }

        // ------------------------------------------------ ducking
        // Times (in samples) where something plays on a track that is NOT ducked; short gaps are bridged
        // so music doesn't pump up and down between sentences.
        public List<long[]> DuckRanges()
        {
            var r = new List<long[]>();
            foreach (var t in Tracks)
            {
                if (t.Duck || !Audible(t)) continue;
                foreach (var c in t.Clips) if (c.Length > 0) r.Add(new[] { c.Start, c.End });
            }
            r.Sort((a, b) => a[0].CompareTo(b[0]));
            var merged = new List<long[]>();
            long bridge = (long)(DuckBridge * Rate);
            foreach (var x in r)
            {
                if (merged.Count > 0 && x[0] <= merged[merged.Count - 1][1] + bridge)
                    merged[merged.Count - 1][1] = Math.Max(merged[merged.Count - 1][1], x[1]);
                else merged.Add(new[] { x[0], x[1] });
            }
            return merged;
        }

        // 0 = nothing else playing, 1 = fully ducked; smooth ramps before and after each range.
        public double DuckActivity(List<long[]> ranges, long t)
        {
            long att = Math.Max(1, (long)(DuckAttack * Rate)), rel = Math.Max(1, (long)(DuckRelease * Rate));
            double best = 0;
            // binary search for the first range that could matter
            int lo = 0, hi = ranges.Count - 1, first = ranges.Count;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (ranges[mid][1] + rel >= t) { first = mid; hi = mid - 1; } else lo = mid + 1;
            }
            for (int i = first; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (r[0] - att > t) break;
                double a;
                if (t >= r[0] && t <= r[1]) a = 1;
                else if (t < r[0]) a = 1 - (r[0] - t) / (double)att;
                else a = 1 - (t - r[1]) / (double)rel;
                if (a > best) best = a;
                if (best >= 1) break;
            }
            return Math.Max(0, Math.Min(1, best));
        }

        public static double DuckFactor(float db) { return Math.Pow(10, -Math.Abs(db) / 20.0); }

        // ------------------------------------------------ mixing
        // Mixes [from, from+n) into float buffers (left/right). only != null: just that track (for "export this track").
        public void MixInto(long from, int n, float[] L, float[] R, Track only, List<long[]> duckRanges)
        {
            Array.Clear(L, 0, n); Array.Clear(R, 0, n);
            if (duckRanges == null) duckRanges = DuckRanges();
            float[] duckEnv = null;
            foreach (var t in Tracks)
            {
                if (only != null) { if (t != only) continue; }
                else if (!Audible(t)) continue;
                float lg = t.Pan > 0 ? 1 - t.Pan : 1, rg = t.Pan < 0 ? 1 + t.Pan : 1;
                bool duck = t.Duck && duckRanges.Count > 0;
                if (duck)
                {
                    if (duckEnv == null) duckEnv = new float[n];
                    double f = DuckFactor(t.DuckDb);
                    for (int i = 0; i < n; i++) duckEnv[i] = (float)(1 - (1 - f) * DuckActivity(duckRanges, from + i));
                }
                foreach (var c in t.Clips)
                {
                    long a = Math.Max(from, c.Start), b = Math.Min(from + n, c.End);
                    if (b <= a) continue;
                    long len = c.Length;
                    long fi = (long)(c.FadeIn * Rate), fo = (long)(c.FadeOut * Rate);
                    float g = c.Gain * t.Volume;
                    var dl = c.Data; var dr = c.DataR ?? c.Data;
                    for (long x = a; x < b; x++)
                    {
                        long pos = x - c.Start;
                        float k = g;
                        if (fi > 0 && pos < fi) k *= pos / (float)fi;
                        if (fo > 0 && pos > len - fo) k *= Math.Max(0, len - pos) / (float)fo;
                        int o = (int)(x - from);
                        if (duck) k *= duckEnv[o];
                        long si = c.In + pos;
                        L[o] += dl[si] * k * lg;
                        R[o] += dr[si] * k * rg;
                    }
                }
            }
        }

        static short Clip16(float v) { return (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v); }

        // Interleaved stereo 16-bit for [from, from+n).
        public short[] MixStereo(long from, int n, Track only = null, List<long[]> duckRanges = null)
        {
            var L = new float[n]; var R = new float[n];
            MixInto(from, n, L, R, only, duckRanges);
            var r = new short[n * 2];
            for (int i = 0; i < n; i++) { r[2 * i] = Clip16(L[i]); r[2 * i + 1] = Clip16(R[i]); }
            return r;
        }

        // Mono mix (average of left and right) — used for short pieces and tests.
        public short[] Mix(long from, long to, Track only = null)
        {
            if (to <= from) return new short[0];
            int n = (int)Math.Min(int.MaxValue / 4, to - from);
            var L = new float[n]; var R = new float[n];
            MixInto(from, n, L, R, only, null);
            var r = new short[n];
            for (int i = 0; i < n; i++) r[i] = Clip16((L[i] + R[i]) * 0.5f);
            return r;
        }

        public bool HasStereo()
        {
            foreach (var t in Tracks) { if (Math.Abs(t.Pan) > 0.001f) return true; foreach (var c in t.Clips) if (c.DataR != null) return true; }
            return false;
        }

        // Writes [from, to) as a 16-bit stereo WAV, one second at a time (memory stays small for long projects).
        public void MixToWav(string path, long from, long to, Track only, Action<double> progress, CancellationToken ct)
        {
            var ranges = DuckRanges();
            const int block = 48000;
            long total = Math.Max(0, to - from);
            using (var fs = File.Create(path))
            {
                var w = new BinaryWriter(fs);
                long dataBytes = total * 4;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((int)Math.Min(int.MaxValue, 36 + dataBytes)); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
                w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write((int)Math.Min(int.MaxValue, dataBytes));
                var L = new float[block]; var R = new float[block];
                var buf = new byte[block * 4];
                for (long pos = from; pos < to; pos += block)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(block, to - pos);
                    MixInto(pos, n, L, R, only, ranges);
                    for (int i = 0; i < n; i++)
                    {
                        short l = Clip16(L[i]), r = Clip16(R[i]);
                        buf[4 * i] = (byte)l; buf[4 * i + 1] = (byte)(l >> 8);
                        buf[4 * i + 2] = (byte)r; buf[4 * i + 3] = (byte)(r >> 8);
                    }
                    w.Write(buf, 0, n * 4);
                    if (progress != null && total > 0) progress((pos - from + n) / (double)total);
                }
                w.Flush();
            }
        }

        // ------------------------------------------------ subtitles
        public class SubEntry { public long Start, End; public string Text; }

        public List<SubEntry> SubtitleEntries()
        {
            var list = new List<SubEntry>();
            foreach (var t in Tracks)
            {
                if (!Audible(t)) continue;
                foreach (var c in t.Clips)
                    if (c.Kind == "speech" && !string.IsNullOrEmpty(c.Text) && c.Length > 0)
                        list.Add(new SubEntry { Start = c.Start, End = c.End, Text = c.Text });
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        // ------------------------------------------------ save / load (.pstudio = zip with project.json + audio)
        static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
        static string N(double d) { return d.ToString("0.######", CultureInfo.InvariantCulture); }

        // Lossless audio packing: differences between samples, split into low/high byte planes, then zip-compressed.
        public static byte[] PackAudio(short[] s)
        {
            int n = s.Length;
            var b = new byte[n * 2];
            short prev = 0;
            for (int i = 0; i < n; i++)
            {
                short d = unchecked((short)(s[i] - prev));
                prev = s[i];
                b[i] = (byte)d;
                b[n + i] = (byte)(d >> 8);
            }
            return b;
        }

        public static short[] UnpackAudio(byte[] b)
        {
            int n = b.Length / 2;
            var s = new short[n];
            short prev = 0;
            for (int i = 0; i < n; i++)
            {
                short d = (short)(b[i] | (b[n + i] << 8));
                prev = unchecked((short)(prev + d));
                s[i] = prev;
            }
            return s;
        }

        public void Save(string path)
        {
            var ids = new Dictionary<short[], int>();
            Func<short[], int> id = a => { if (a == null) return -1; int v; if (!ids.TryGetValue(a, out v)) { v = ids.Count; ids[a] = v; } return v; };
            var sb = new StringBuilder();
            sb.Append("{\"format\":\"piperstudio-tracks\",\"version\":2,\"rate\":").Append(Rate)
              .Append(",\"duckAttack\":").Append(N(DuckAttack)).Append(",\"duckRelease\":").Append(N(DuckRelease)).Append(",\"duckBridge\":").Append(N(DuckBridge))
              .Append(",\"tracks\":[");
            for (int ti = 0; ti < Tracks.Count; ti++)
            {
                var t = Tracks[ti];
                if (ti > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(J(t.Name)).Append(",\"volume\":").Append(N(t.Volume)).Append(",\"pan\":").Append(N(t.Pan))
                  .Append(",\"mute\":").Append(t.Mute ? "true" : "false").Append(",\"solo\":").Append(t.Solo ? "true" : "false")
                  .Append(",\"duck\":").Append(t.Duck ? "true" : "false").Append(",\"duckDb\":").Append(N(t.DuckDb)).Append(",\"clips\":[");
                for (int ci = 0; ci < t.Clips.Count; ci++)
                {
                    var c = t.Clips[ci];
                    if (ci > 0) sb.Append(',');
                    sb.Append("{\"data\":").Append(id(c.Data)).Append(",\"dataR\":").Append(id(c.DataR)).Append(",\"start\":").Append(c.Start).Append(",\"in\":").Append(c.In).Append(",\"out\":").Append(c.Out)
                      .Append(",\"gain\":").Append(N(c.Gain)).Append(",\"fadeIn\":").Append(N(c.FadeIn)).Append(",\"fadeOut\":").Append(N(c.FadeOut))
                      .Append(",\"name\":").Append(J(c.Name)).Append(",\"text\":").Append(J(c.Text)).Append(",\"kind\":").Append(J(c.Kind)).Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("project.json");
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(sb.ToString());
                foreach (var kv in ids)
                {
                    var ae = zip.CreateEntry("audio/" + kv.Value + ".d16", CompressionLevel.Optimal);
                    using (var s = ae.Open()) { var b = PackAudio(kv.Key); s.Write(b, 0, b.Length); }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static object G(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        static double D(object o, string k, double def) { var v = G(o, k); return v == null ? def : Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        static bool B(object o, string k) { var v = G(o, k); return v is bool && (bool)v; }

        public static Project Load(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var pe = zip.GetEntry("project.json");
                if (pe == null) throw new Exception("This is not a Piper Studio tracks project.");
                string json;
                using (var r = new StreamReader(pe.Open(), Encoding.UTF8)) json = r.ReadToEnd();
                var root = MiniJson.Parse(json);
                var p = new Project
                {
                    Rate = (int)D(root, "rate", 48000),
                    DuckAttack = D(root, "duckAttack", 0.25), DuckRelease = D(root, "duckRelease", 0.6), DuckBridge = D(root, "duckBridge", 0.8)
                };
                var audio = new Dictionary<int, short[]>();
                Func<int, short[]> get = id =>
                {
                    if (id < 0) return null;
                    short[] a;
                    if (audio.TryGetValue(id, out a)) return a;
                    var packed = zip.GetEntry("audio/" + id + ".d16");
                    var raw = zip.GetEntry("audio/" + id + ".raw");   // version 1 projects
                    var e = packed ?? raw;
                    if (e == null) a = new short[0];
                    else
                    {
                        var ms = new MemoryStream();
                        using (var s = e.Open()) s.CopyTo(ms);
                        var b = ms.ToArray();
                        if (e == packed) a = UnpackAudio(b);
                        else { a = new short[b.Length / 2]; Buffer.BlockCopy(b, 0, a, 0, a.Length * 2); }
                    }
                    audio[id] = a;
                    return a;
                };
                var tracks = G(root, "tracks") as List<object> ?? new List<object>();
                foreach (var to in tracks)
                {
                    var t = new Track
                    {
                        Name = Convert.ToString(G(to, "name") ?? "Track"), Volume = (float)D(to, "volume", 1), Pan = (float)D(to, "pan", 0),
                        Mute = B(to, "mute"), Solo = B(to, "solo"), Duck = B(to, "duck"), DuckDb = (float)D(to, "duckDb", 12)
                    };
                    foreach (var co in (G(to, "clips") as List<object> ?? new List<object>()))
                    {
                        var c = new Clip
                        {
                            Data = get((int)D(co, "data", 0)), DataR = get((int)D(co, "dataR", -1)),
                            Start = (long)D(co, "start", 0), In = (long)D(co, "in", 0), Out = (long)D(co, "out", 0),
                            Gain = (float)D(co, "gain", 1), FadeIn = D(co, "fadeIn", 0), FadeOut = D(co, "fadeOut", 0),
                            Name = Convert.ToString(G(co, "name") ?? ""), Text = G(co, "text") as string, Kind = Convert.ToString(G(co, "kind") ?? "audio")
                        };
                        if (c.Data == null) c.Data = new short[0];
                        if (c.DataR != null && c.DataR.Length != c.Data.Length) c.DataR = null;
                        c.Out = Math.Min(c.Out, c.Data.Length); c.In = Math.Max(0, Math.Min(c.In, c.Out));
                        t.Clips.Add(c);
                    }
                    p.Tracks.Add(t);
                }
                return p;
            }
        }
    }

    // Undo / redo by snapshots (cheap: audio arrays are shared, only positions are copied).
    public class History
    {
        readonly List<Project> undo = new List<Project>();
        readonly List<Project> redo = new List<Project>();
        public void Push(Project p) { undo.Add(p.Clone()); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public Project Undo(Project current)
        {
            if (undo.Count == 0) return current;
            redo.Add(current.Clone());
            var p = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); return p;
        }
        public Project Redo(Project current)
        {
            if (redo.Count == 0) return current;
            undo.Add(current.Clone());
            var p = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); return p;
        }
        public void Clear() { undo.Clear(); redo.Clear(); }
    }

    // Loads any audio file as 16-bit at the given rate, keeping stereo when the file is stereo.
    public static class AudioImport
    {
        public class Result { public short[] Left, Right; }   // Right == null for mono

        public static Result LoadStereo(string path, int rate, string ffmpeg)
        {
            if (ffmpeg != null)
            {
                var psi = new ProcessStartInfo(ffmpeg, string.Format("-hide_banner -loglevel error -i \"{0}\" -vn -ac 2 -ar {1} -f s16le -", path, rate))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var ms = new MemoryStream();
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new Exception("FFmpeg couldn't read this file:\n" + err.Result);
                    var b = ms.ToArray();
                    int frames = b.Length / 4;
                    var l = new short[frames]; var r = new short[frames];
                    for (int i = 0; i < frames; i++) { l[i] = BitConverter.ToInt16(b, 4 * i); r[i] = BitConverter.ToInt16(b, 4 * i + 2); }
                    return Collapse(l, r);
                }
            }
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Only WAV files can be imported without FFmpeg. Set ffmpeg.exe on the Export tab to import MP3 and other formats.");
            return ReadWav(path, rate);
        }

        // Kept for older callers: mono.
        public static short[] Load(string path, int rate, string ffmpeg)
        {
            var r = LoadStereo(path, rate, ffmpeg);
            if (r.Right == null) return r.Left;
            var m = new short[r.Left.Length];
            for (int i = 0; i < m.Length; i++) m[i] = (short)((r.Left[i] + r.Right[i]) / 2);
            return m;
        }

        // "Stereo" files whose two channels are identical are stored as mono (half the memory).
        static Result Collapse(short[] l, short[] r)
        {
            bool same = true;
            for (int i = 0; i < l.Length; i++) if (l[i] != r[i]) { same = false; break; }
            return new Result { Left = l, Right = same ? null : r };
        }

        static Result ReadWav(string path, int rate)
        {
            byte[] b = File.ReadAllBytes(path);
            int pos = 12, ch = 1, sr = rate, bits = 16, fmt = 1;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    fmt = BitConverter.ToInt16(b, pos + 8); ch = BitConverter.ToInt16(b, pos + 10);
                    sr = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22);
                    if (fmt == -2 && len >= 40) fmt = BitConverter.ToInt16(b, pos + 32);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    len = Math.Min(len < 0 ? int.MaxValue : len, b.Length - pos - 8);
                    int bps = bits / 8, frames = len / (bps * ch);
                    var chans = new short[Math.Min(2, ch)][];
                    for (int c = 0; c < chans.Length; c++) chans[c] = new short[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        for (int c = 0; c < ch; c++)
                        {
                            int o = pos + 8 + (f * ch + c) * bps;
                            double v;
                            if (fmt == 3 && bits == 32) v = BitConverter.ToSingle(b, o) * 32767.0;
                            else if (bits == 16) v = BitConverter.ToInt16(b, o);
                            else if (bits == 24) v = ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) >> 8);
                            else if (bits == 32) v = BitConverter.ToInt32(b, o) >> 16;
                            else if (bits == 8) v = (b[o] - 128) << 8;
                            else v = 0;
                            short sv = (short)Math.Max(-32768, Math.Min(32767, v));
                            if (c < 2) chans[c][f] = sv;
                        }
                    }
                    var left = Audio.Resample(chans[0], sr, rate);
                    if (chans.Length == 1) return new Result { Left = left };
                    return Collapse(left, Audio.Resample(chans[1], sr, rate));
                }
                pos += 8 + len + (len & 1);
            }
            throw new Exception("Couldn't read this WAV file.");
        }
    }

    // ---------------------------------------------------------------- subtitles (.srt)
    public static class Subtitles
    {
        public class Cue { public double Start, End; public string Text; }

        static string Clean(string t)
        {
            t = TextTools.PauseRx.Replace(t ?? "", " ");
            return Regex.Replace(t, @"\s+", " ").Trim();
        }

        // Long sentences are split into readable cues (max ~2 lines of 42 characters), timed by length.
        public static List<Cue> MakeCues(IEnumerable<Cue> raw, int maxChars = 84)
        {
            var outList = new List<Cue>();
            foreach (var c in raw)
            {
                string text = Clean(c.Text);
                if (text.Length == 0) continue;
                var parts = new List<string>();
                var words = text.Split(' ');
                var cur = new StringBuilder();
                foreach (var w in words)
                {
                    if (cur.Length > 0 && cur.Length + 1 + w.Length > maxChars) { parts.Add(cur.ToString()); cur.Clear(); }
                    if (cur.Length > 0) cur.Append(' ');
                    cur.Append(w);
                }
                if (cur.Length > 0) parts.Add(cur.ToString());
                int totalChars = parts.Sum(p => p.Length);
                double t = c.Start, dur = Math.Max(0.3, c.End - c.Start);
                foreach (var p in parts)
                {
                    double d = dur * p.Length / Math.Max(1, totalChars);
                    outList.Add(new Cue { Start = t, End = t + d, Text = p });
                    t += d;
                }
            }
            return outList;
        }

        static string Wrap2(string s, int line = 42)
        {
            if (s.Length <= line) return s;
            int mid = s.Length / 2, best = -1;
            for (int d = 0; d < mid; d++)
            {
                if (mid - d > 0 && s[mid - d] == ' ') { best = mid - d; break; }
                if (mid + d < s.Length && s[mid + d] == ' ') { best = mid + d; break; }
            }
            return best < 0 ? s : s.Substring(0, best) + "\n" + s.Substring(best + 1);
        }

        public static string Time(double sec)
        {
            if (sec < 0) sec = 0;
            long ms = (long)Math.Round(sec * 1000);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}", ms / 3600000, ms / 60000 % 60, ms / 1000 % 60, ms % 1000);
        }

        public static string ToSrt(List<Cue> cues)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                sb.Append(i + 1).Append("\r\n");
                sb.Append(Time(c.Start)).Append(" --> ").Append(Time(Math.Max(c.End, c.Start + 0.2))).Append("\r\n");
                foreach (var line in Wrap2(c.Text).Split('\n'))
                {
                    // right-to-left lines get RLM marks so punctuation shows on the correct side in video players
                    if (TextTools.StartsRtl(line)) sb.Append('‏').Append(line).Append('‏');
                    else sb.Append(line);
                    sb.Append("\r\n");
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        public static void Write(string path, List<Cue> cues)
        {
            File.WriteAllText(path, ToSrt(cues), new UTF8Encoding(true));
        }
    }

    // ---------------------------------------------------------------- recording latency
    public static class Latency
    {
        // A test signal: short 1 kHz beeps. Returns mono samples and the beep times (seconds).
        public static short[] MakeBeeps(int rate, out double[] times)
        {
            times = new double[6];
            for (int i = 0; i < times.Length; i++) times[i] = 0.6 + i * 0.7;
            var s = new short[(int)(rate * (times[times.Length - 1] + 1.0))];
            int len = rate * 40 / 1000, fade = rate * 4 / 1000;
            foreach (var t in times)
            {
                int start = (int)(t * rate);
                for (int k = 0; k < len; k++)
                {
                    double env = Math.Min(1, Math.Min(k / (double)fade, (len - k) / (double)fade));
                    s[start + k] = (short)(Math.Sin(2 * Math.PI * 1000 * k / rate) * 22000 * env);
                }
            }
            return s;
        }

        // Finds the delay (ms) between the beeps we played and the beeps the microphone heard.
        // playStartSec = when the first sample was sent to the speakers, measured from the recording's start.
        // Returns NaN if the beeps couldn't be found.
        public static double Detect(short[] rec, int rate, double[] beepTimes, double playStartSec, out int matched)
        {
            matched = 0;
            if (rec == null || rec.Length < rate / 2) return double.NaN;
            int win = Math.Max(1, rate / 1000);   // 1 ms envelope
            int nWin = rec.Length / win;
            var env = new double[nWin];
            for (int i = 0; i < nWin; i++)
            {
                int m = 0;
                for (int k = 0; k < win; k++) { int v = Math.Abs((int)rec[i * win + k]); if (v > m) m = v; }
                env[i] = m;
            }
            var sorted = env.OrderBy(x => x).ToArray();
            double noise = sorted[(int)(sorted.Length * 0.5)], peak = sorted[sorted.Length - 1];
            if (peak < 800 || peak < noise * 4) return double.NaN;
            double thr = Math.Max(noise * 4, noise + (peak - noise) * 0.3);
            var onsets = new List<double>();
            double last = -1;
            for (int i = 0; i < nWin; i++)
                if (env[i] >= thr && (last < 0 || i - last > 300)) { onsets.Add(i / 1000.0); last = i; }
            var diffs = new List<double>();
            foreach (var bt in beepTimes)
            {
                double expected = playStartSec + bt;
                double best = double.NaN;
                foreach (var o in onsets)
                {
                    double d = o - expected;
                    if (d >= -0.02 && d < 0.65 && (double.IsNaN(best) || d < best)) best = d;
                }
                if (!double.IsNaN(best)) diffs.Add(best);
            }
            matched = diffs.Count;
            if (diffs.Count < 3) return double.NaN;
            diffs.Sort();
            return Math.Max(0, diffs[diffs.Count / 2] * 1000.0);
        }
    }
}
