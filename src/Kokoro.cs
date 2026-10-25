using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PiperStudio
{
    // ================================================================== Kokoro (via sherpa-onnx)
    // Kokoro is a second, optional speech engine for English. It runs through sherpa-onnx's C library
    // (sherpa-onnx-c-api.dll + its own onnxruntime.dll) inside a helper process — PiperStudio.exe --kokoro-host —
    // so its onnxruntime never clashes with Piper's, and a crash in the engine can't take the app down.
    //
    // Layout (either next to PiperStudio.exe or in %APPDATA%\PiperStudio):
    //   kokoro\sherpa-onnx-c-api.dll, onnxruntime.dll, onnxruntime_providers_shared.dll
    //   kokoro\kokoro-multi-lang-v1_0\model.onnx, voices.bin, tokens.txt, espeak-ng-data\, lexicon-us-en.txt …
    //   (or kokoro-int8-multi-lang-v1_0 with model.int8.onnx)
    public class KokoroInstall
    {
        public string EngineDir, ModelDir, ModelFile;
        public int VoiceCount;
        public bool HasLexicons;
        public bool EngineOk { get { return EngineDir != null; } }
        public bool ModelOk { get { return ModelDir != null; } }
        public bool Ready { get { return EngineOk && ModelOk; } }
        public string ModelName { get { return ModelDir == null ? "" : Path.GetFileName(ModelDir.TrimEnd('\\', '/')); } }
        public bool IsInt8 { get { return ModelFile != null && ModelFile.IndexOf("int8", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public static string EngineLibName { get { return Environment.OSVersion.Platform == PlatformID.Win32NT ? "sherpa-onnx-c-api.dll" : "libsherpa-onnx-c-api.so"; } }

        // Folders searched for the engine and models, in order.
        public static List<string> Roots(string extra)
        {
            var l = new List<string>();
            if (!string.IsNullOrEmpty(extra)) l.Add(extra);
            l.Add(Path.Combine(AppPaths.AppDir, "kokoro"));
            l.Add(Path.Combine(AppPaths.DataDir, "kokoro"));
            return l.Where(d => !string.IsNullOrEmpty(d)).Select(d => { try { return Path.GetFullPath(d); } catch { return d; } })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Where a download goes: next to the program if we may write there, otherwise the user's data folder.
        public static string InstallRoot()
        {
            string local = Path.Combine(AppPaths.AppDir, "kokoro");
            if (Directory.Exists(local) ? AppPaths.CanWrite(local) : AppPaths.CanWrite(AppPaths.AppDir)) return local;
            return Path.Combine(AppPaths.DataDir, "kokoro");
        }

        static string ModelFileIn(string dir)
        {
            foreach (var n in new[] { "model.onnx", "model.int8.onnx" })
                if (File.Exists(Path.Combine(dir, n))) return n;
            try { var any = Directory.GetFiles(dir, "*.onnx").FirstOrDefault(); if (any != null) return Path.GetFileName(any); } catch { }
            return null;
        }

        public static bool IsModelDir(string dir)
        {
            try
            {
                return File.Exists(Path.Combine(dir, "voices.bin")) && File.Exists(Path.Combine(dir, "tokens.txt"))
                    && Directory.Exists(Path.Combine(dir, "espeak-ng-data")) && ModelFileIn(dir) != null;
            }
            catch { return false; }
        }

        public static KokoroInstall Find(string extra)
        {
            var k = new KokoroInstall();
            var models = new List<string>();
            foreach (var root in Roots(extra))
            {
                if (!Directory.Exists(root)) continue;
                if (k.EngineDir == null && File.Exists(Path.Combine(root, EngineLibName))) k.EngineDir = root;
                if (IsModelDir(root)) models.Add(root);
                try { foreach (var d in Directory.GetDirectories(root)) if (IsModelDir(d)) models.Add(d); } catch { }
            }
            // prefer the full-quality model, then the newest version (v1.0 has more voices)
            var best = models.OrderByDescending(d => ModelFileIn(d) == "model.onnx" ? 1 : 0)
                             .ThenByDescending(d => VoicesIn(d)).FirstOrDefault();
            if (best != null)
            {
                k.ModelDir = best;
                k.ModelFile = ModelFileIn(best);
                k.VoiceCount = VoicesIn(best);
                k.HasLexicons = File.Exists(Path.Combine(best, "lexicon-us-en.txt"));
            }
            return k;
        }

        // voices.bin is a table of 510 x 256 float32 style vectors per voice.
        public static int VoicesIn(string dir)
        {
            try { return (int)(new FileInfo(Path.Combine(dir, "voices.bin")).Length / (510 * 256 * 4)); } catch { return 0; }
        }

        // ---------------------------------------------------------------- voice names
        // Kokoro v1.0 speaker table (sherpa-onnx kokoro-multi-lang-v1_0). ★ = best rated by Kokoro's authors.
        static readonly string[] V10 = {
            "af_alloy","af_aoede","af_bella","af_heart","af_jessica","af_kore","af_nicole","af_nova","af_river","af_sarah","af_sky",
            "am_adam","am_echo","am_eric","am_fenrir","am_liam","am_michael","am_onyx","am_puck","am_santa",
            "bf_alice","bf_emma","bf_isabella","bf_lily","bm_daniel","bm_fable","bm_george","bm_lewis",
            "ef_dora","em_alex","ff_siwis","hf_alpha","hf_beta","hm_omega","hm_psi","if_sara","im_nicola",
            "jf_alpha","jf_gongitsune","jf_nezumi","jf_tebukuro","jm_kumo","pf_dora","pm_alex","pm_santa",
            "zf_xiaobei","zf_xiaoni","zf_xiaoxiao","zf_xiaoyi","zm_yunjian","zm_yunxi","zm_yunxia","zm_yunyang","em_santa" };
        // Kokoro v0.19 (kokoro-en-v0_19): English only.
        static readonly string[] V019 = { "af","af_bella","af_nicole","af_sarah","af_sky","am_adam","am_michael","bf_emma","bf_isabella","bm_george","bm_lewis" };
        static readonly HashSet<string> Starred = new HashSet<string> { "af_heart", "af_bella", "af_nicole", "bf_emma", "am_michael", "am_fenrir", "am_puck" };

        public static string[] SpeakerTable(int count)
        {
            if (count == 11) return V019;
            if (count >= 53 && count <= 54) return V10.Take(count).ToArray();
            var l = new string[count];
            for (int i = 0; i < count; i++) l[i] = "voice_" + i;
            return l;
        }

        public static string Pretty(string id)
        {
            int u = id.IndexOf('_');
            string n = u >= 0 ? id.Substring(u + 1) : id;
            if (n.Length == 0) n = id;
            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        // English voices only: Kokoro can't read Persian, and Piper covers the other languages.
        public List<VoiceInfo> Voices()
        {
            if (!Ready) return new List<VoiceInfo>();
            var list = EnglishVoices(SpeakerTable(VoiceCount));
            foreach (var v in list)
            {
                v.OnnxPath = Path.Combine(ModelDir, ModelFile);
                v.KokoroEngineDir = EngineDir; v.KokoroModelDir = ModelDir; v.KokoroModelFile = ModelFile;
                if (!HasLexicons) v.KokoroLang = "";
            }
            return list;
        }

        // The US and UK voices of a speaker table, best ones first.
        public static List<VoiceInfo> EnglishVoices(string[] table)
        {
            var list = new List<VoiceInfo>();
            for (int sid = 0; sid < table.Length; sid++)
            {
                string id = table[sid];
                bool us = id.StartsWith("a"), gb = id.StartsWith("b");
                if (!us && !gb) continue;
                bool woman = id.Length > 1 && id[1] == 'f';
                list.Add(new VoiceInfo
                {
                    Engine = "kokoro", JsonPath = "",
                    FileName = "kokoro-" + id,
                    Dataset = "Kokoro " + Pretty(id) + (Starred.Contains(id) ? " ★" : ""),
                    Quality = (woman ? "woman" : "man"),
                    LangCode = us ? "en_US" : "en_GB", LangFamily = "en", LangName = us ? "English (US)" : "English (UK)",
                    EspeakVoice = us ? "en-us" : "en-gb",
                    SampleRate = 24000, NumSpeakers = 1,
                    KokoroSid = sid, KokoroId = id,
                    KokoroLang = us ? "en-us" : "en-gb"
                });
            }
            // starred first, then US before UK, then by name
            return list.OrderByDescending(v => v.Dataset.EndsWith("★") ? 1 : 0).ThenBy(v => v.LangCode == "en_US" ? 0 : 1)
                       .ThenBy(v => v.Dataset, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // ---------------------------------------------------------------- native sherpa-onnx API (offline TTS)
    // Struct layout copied from sherpa-onnx v1.13.8 c-api.h (x64). Strings are UTF-8 pointers.
    static class SherpaNative
    {
        [StructLayout(LayoutKind.Sequential)] public struct Vits { public IntPtr model, lexicon, tokens, data_dir; public float noise_scale, noise_scale_w, length_scale; public IntPtr dict_dir; }
        [StructLayout(LayoutKind.Sequential)] public struct Matcha { public IntPtr acoustic_model, vocoder, lexicon, tokens, data_dir; public float noise_scale, length_scale; public IntPtr dict_dir; }
        [StructLayout(LayoutKind.Sequential)] public struct Kokoro { public IntPtr model, voices, tokens, data_dir; public float length_scale; public IntPtr dict_dir, lexicon, lang; }
        [StructLayout(LayoutKind.Sequential)] public struct Kitten { public IntPtr model, voices, tokens, data_dir; public float length_scale; }
        [StructLayout(LayoutKind.Sequential)] public struct Zipvoice { public IntPtr tokens, encoder, decoder, vocoder, data_dir, lexicon; public float feat_scale, t_shift, target_rms, guidance_scale; }
        [StructLayout(LayoutKind.Sequential)] public struct Pocket { public IntPtr lm_flow, lm_main, encoder, decoder, text_conditioner, vocab_json, token_scores_json; public int voice_embedding_cache_capacity; }
        [StructLayout(LayoutKind.Sequential)] public struct Supertonic { public IntPtr duration_predictor, text_encoder, vector_estimator, vocoder, tts_json, unicode_indexer, voice_style; }
        [StructLayout(LayoutKind.Sequential)] public struct ModelConfig { public Vits vits; public int num_threads, debug; public IntPtr provider; public Matcha matcha; public Kokoro kokoro; public Kitten kitten; public Zipvoice zipvoice; public Pocket pocket; public Supertonic supertonic; }
        [StructLayout(LayoutKind.Sequential)] public struct TtsConfig { public ModelConfig model; public IntPtr rule_fsts; public int max_num_sentences; public IntPtr rule_fars; public float silence_scale; }
        [StructLayout(LayoutKind.Sequential)] public struct GeneratedAudio { public IntPtr samples; public int n; public int sample_rate; }

        const string L = "sherpa-onnx-c-api";
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SherpaOnnxCreateOfflineTts(ref TtsConfig c);
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern void SherpaOnnxDestroyOfflineTts(IntPtr tts);
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SherpaOnnxOfflineTtsGenerate(IntPtr tts, IntPtr text, int sid, float speed);
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr audio);
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern int SherpaOnnxOfflineTtsSampleRate(IntPtr tts);
        [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern int SherpaOnnxOfflineTtsNumSpeakers(IntPtr tts);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectoryW(string dir);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetShortPathNameW(string longPath, StringBuilder shortPath, int size);
        const uint LOAD_WITH_ALTERED_SEARCH_PATH = 8;

        static bool Windows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        // Load the engine's own onnxruntime.dll first (Piper has an older one next to PiperStudio.exe).
        public static void LoadEngine(string dir)
        {
            if (!Windows) return;   // Linux test machines: LD_LIBRARY_PATH does the job
            SetDllDirectoryW(dir);
            foreach (var n in new[] { "onnxruntime.dll", "onnxruntime_providers_shared.dll", "sherpa-onnx-c-api.dll" })
            {
                string p = Path.Combine(dir, n);
                if (!File.Exists(p)) { if (n == "onnxruntime_providers_shared.dll") continue; throw new Exception("Missing " + n + " in " + dir); }
                if (LoadLibraryExW(p, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) == IntPtr.Zero)
                    throw new Exception("Could not load " + n + " (Windows error " + Marshal.GetLastWin32Error() + ")");
            }
        }

        // The engine opens files with plain (ANSI) paths on Windows: use the short 8.3 name for paths with Persian letters etc.
        public static string SafePath(string p)
        {
            if (!Windows || p.All(c => c < 128)) return p;
            try
            {
                var sb = new StringBuilder(1024);
                int n = GetShortPathNameW(p, sb, sb.Capacity);
                if (n > 0 && n < sb.Capacity && sb.ToString().All(c => c < 128)) return sb.ToString();
            }
            catch { }
            return p;
        }

        public static IntPtr Utf8(string s, List<IntPtr> keep)
        {
            if (s == null) return IntPtr.Zero;
            var b = Encoding.UTF8.GetBytes(s + "\0");
            var p = Marshal.AllocHGlobal(b.Length);
            Marshal.Copy(b, 0, p, b.Length);
            if (keep != null) keep.Add(p);
            return p;
        }
    }

    // A loaded Kokoro model (one per language variant: en-us / en-gb).
    public class KokoroEngine : IDisposable
    {
        IntPtr tts;
        readonly List<IntPtr> strings = new List<IntPtr>();
        public int SampleRate { get; private set; }
        public int NumSpeakers { get; private set; }

        public KokoroEngine(string modelDir, string modelFile, string lang, int threads)
        {
            Func<string, IntPtr> S = s => SherpaNative.Utf8(s, strings);
            Func<string, IntPtr> P = name => { string f = Path.Combine(modelDir, name); return S(SherpaNative.SafePath(f)); };
            var c = new SherpaNative.TtsConfig();
            c.model.kokoro.model = P(modelFile);
            c.model.kokoro.voices = P("voices.bin");
            c.model.kokoro.tokens = P("tokens.txt");
            c.model.kokoro.data_dir = P("espeak-ng-data");
            c.model.kokoro.length_scale = 1f;
            if (!string.IsNullOrEmpty(lang))
            {
                string lex = lang == "en-gb" ? "lexicon-gb-en.txt" : "lexicon-us-en.txt";
                if (File.Exists(Path.Combine(modelDir, lex))) c.model.kokoro.lexicon = P(lex);
                c.model.kokoro.lang = S(lang == "en-gb" ? "en" : lang);   // eSpeak calls British English just "en"
            }
            c.model.num_threads = Math.Max(1, threads);
            c.model.provider = S("cpu");
            c.max_num_sentences = 1;
            c.silence_scale = 0.2f;
            tts = SherpaNative.SherpaOnnxCreateOfflineTts(ref c);
            if (tts == IntPtr.Zero) throw new Exception("Kokoro could not load the model in " + modelDir);
            SampleRate = SherpaNative.SherpaOnnxOfflineTtsSampleRate(tts);
            NumSpeakers = SherpaNative.SherpaOnnxOfflineTtsNumSpeakers(tts);
        }

        // speed: 1 = normal, 2 = twice as fast. Returns 16-bit mono PCM at SampleRate.
        public short[] Generate(string text, int sid, float speed)
        {
            var t = SherpaNative.Utf8(text, null);
            try
            {
                var ap = SherpaNative.SherpaOnnxOfflineTtsGenerate(tts, t, sid, speed);
                if (ap == IntPtr.Zero) return new short[0];
                try
                {
                    var a = (SherpaNative.GeneratedAudio)Marshal.PtrToStructure(ap, typeof(SherpaNative.GeneratedAudio));
                    if (a.n <= 0 || a.samples == IntPtr.Zero) return new short[0];
                    var f = new float[a.n];
                    Marshal.Copy(a.samples, f, 0, a.n);
                    var s = new short[a.n];
                    for (int i = 0; i < f.Length; i++)
                    {
                        float x = f[i] * 32767f;
                        s[i] = (short)(x > 32767f ? 32767 : x < -32768f ? -32768 : x);
                    }
                    return s;
                }
                finally { SherpaNative.SherpaOnnxDestroyOfflineTtsGeneratedAudio(ap); }
            }
            finally { Marshal.FreeHGlobal(t); }
        }

        public void Dispose()
        {
            if (tts != IntPtr.Zero) { try { SherpaNative.SherpaOnnxDestroyOfflineTts(tts); } catch { } tts = IntPtr.Zero; }
            foreach (var p in strings) Marshal.FreeHGlobal(p);
            strings.Clear();
        }
    }

    // ---------------------------------------------------------------- helper process
    // PiperStudio.exe --kokoro-host <engineDir> <modelDir> <modelFile> <lang|-> <threads>
    // Reads one JSON request per line on stdin: {"text": "...", "sid": 3, "speed": 1.0, "output_file": "x.wav"}
    // writes the WAV, then prints the file path (or an empty line after an error, with details on stderr).
    public static class KokoroHost
    {
        public static int Run(string[] a)
        {
            var stdout = Console.OpenStandardOutput();
            var err = Console.Error;
            KokoroEngine eng;
            try
            {
                SherpaNative.LoadEngine(a[1]);
                eng = new KokoroEngine(a[2], a[3], a[4] == "-" ? "" : a[4], int.Parse(a[5], CultureInfo.InvariantCulture));
                err.WriteLine("Kokoro ready: " + eng.NumSpeakers + " voices, " + eng.SampleRate + " Hz");
            }
            catch (Exception ex)
            {
                err.WriteLine("Kokoro failed to start: " + ex.Message);
                return 2;
            }
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                string reply = "";
                try
                {
                    var d = MiniJson.Parse(line) as Dictionary<string, object>;
                    string text = Convert.ToString(d["text"]);
                    string outFile = Convert.ToString(d["output_file"]);
                    int sid = d.ContainsKey("sid") ? Convert.ToInt32(d["sid"], CultureInfo.InvariantCulture) : 0;
                    float speed = d.ContainsKey("speed") ? (float)Convert.ToDouble(d["speed"], CultureInfo.InvariantCulture) : 1f;
                    var pcm = eng.Generate(text, sid, Math.Max(0.3f, Math.Min(3f, speed)));
                    if (pcm.Length == 0) err.WriteLine("Kokoro produced no audio for: " + (text.Length > 80 ? text.Substring(0, 80) + "…" : text));
                    else { Audio.WriteWav(outFile, pcm, eng.SampleRate); reply = outFile; }
                }
                catch (Exception ex) { err.WriteLine("Kokoro error: " + ex.Message); }
                var b = new UTF8Encoding(false).GetBytes(reply + "\n");
                stdout.Write(b, 0, b.Length);
                stdout.Flush();
            }
            eng.Dispose();
            return 0;
        }
    }
}
