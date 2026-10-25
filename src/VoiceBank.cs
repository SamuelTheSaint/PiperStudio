using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PiperStudio
{
    // ================================================================== voice banking
    // A voice bank is a folder:  script.txt (one sentence per line), project.ini, wavs\0001.wav …
    // Recordings are kept as 22050 Hz mono 16-bit WAV, the format Piper is trained on.
    public class VoiceBank
    {
        public const int Rate = 22050;
        public string Folder, Name = "My voice", Lang = "en";
        public List<string> Sentences = new List<string>();

        public string WavDir { get { return Path.Combine(Folder, "wavs"); } }
        public string WavPath(int i) { return Path.Combine(WavDir, (i + 1).ToString("0000") + ".wav"); }
        public bool Has(int i) { return File.Exists(WavPath(i)); }
        public int RecordedCount { get { int n = 0; for (int i = 0; i < Sentences.Count; i++) if (Has(i)) n++; return n; } }
        public string EspeakVoice { get { return Lang == "fa" ? "fa" : "en-us"; } }

        public static string DefaultRoot { get { return Path.Combine(AppPaths.DefaultExportDir, "Voice bank"); } }

        public static VoiceBank Create(string folder, string name, string lang, IEnumerable<string> sentences)
        {
            var vb = new VoiceBank { Folder = folder, Name = name, Lang = lang };
            vb.Sentences = Clean(sentences);
            Directory.CreateDirectory(vb.WavDir);
            vb.Save();
            return vb;
        }

        public static List<string> Clean(IEnumerable<string> lines)
        {
            return lines.Select(l => (l ?? "").Replace("|", " ").Replace("\t", " ").Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
        }

        public static List<string> BuiltIn(string lang)
        {
            return Clean((lang == "fa" ? VoiceBankScripts.Persian : VoiceBankScripts.English).Replace("\r", "").Split('\n'));
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder, "script.txt"), Sentences, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Folder, "project.ini"), new[] { "name=" + Name, "lang=" + Lang }, new UTF8Encoding(false));
        }

        public static VoiceBank Open(string folder)
        {
            var vb = new VoiceBank { Folder = folder };
            string ini = Path.Combine(folder, "project.ini");
            if (File.Exists(ini))
                foreach (var l in File.ReadAllLines(ini, Encoding.UTF8))
                {
                    int eq = l.IndexOf('='); if (eq < 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    if (k == "name") vb.Name = v; else if (k == "lang") vb.Lang = v;
                }
            string script = Path.Combine(folder, "script.txt");
            if (!File.Exists(script)) throw new Exception("This folder is not a voice bank (script.txt is missing).");
            vb.Sentences = Clean(File.ReadAllLines(script, Encoding.UTF8));
            Directory.CreateDirectory(vb.WavDir);
            return vb;
        }

        // Adds sentences at the end (new numbers), so existing recordings keep their sentence.
        public int AddSentences(IEnumerable<string> lines)
        {
            var add = Clean(lines).Where(l => !Sentences.Contains(l)).ToList();
            Sentences.AddRange(add);
            Save();
            return add.Count;
        }

        public double RecordedSeconds()
        {
            double s = 0;
            for (int i = 0; i < Sentences.Count; i++)
                if (Has(i)) try { s = s + (new FileInfo(WavPath(i)).Length - 44) / 2.0 / Rate; } catch { }
            return s;
        }

        public void SaveTake(int i, short[] pcm)
        {
            Directory.CreateDirectory(WavDir);
            Audio.WriteWav(WavPath(i), pcm, Rate);
        }

        // ---------------------------------------------------------------- export for training
        // Creates <name>-voicebank.zip with wavs\, metadata.csv and step-by-step training instructions.
        public string Export(string zipPath)
        {
            var rows = new List<string>();
            for (int i = 0; i < Sentences.Count; i++)
                if (Has(i)) rows.Add(Path.GetFileName(WavPath(i)) + "|" + Sentences[i]);
            if (rows.Count == 0) throw new Exception("Nothing recorded yet.");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (int i = 0; i < Sentences.Count; i++)
                    if (Has(i))
                    {
                        var e = zip.CreateEntry("wavs/" + Path.GetFileName(WavPath(i)), CompressionLevel.Optimal);
                        using (var o = e.Open()) { var b = File.ReadAllBytes(WavPath(i)); o.Write(b, 0, b.Length); }
                    }
                Text(zip, "metadata.csv", string.Join("\n", rows) + "\n");
                Text(zip, "TRAINING.txt", TrainingGuide(rows.Count));
                Text(zip, "train.sh", TrainScript());
            }
            return zipPath;
        }

        static void Text(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text.Replace("\r\n", "\n"));
        }

        public string SafeName { get { var s = new string((Name ?? "voice").ToLowerInvariant().Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).ToArray()); return s.Length == 0 ? "myvoice" : s; } }
        public string VoiceFileName { get { return (Lang == "fa" ? "fa_IR" : "en_US") + "-" + SafeName + "-medium"; } }

        public string TrainScript()
        {
            return "#!/bin/bash\n" +
"# Trains a Piper voice from this voice bank. Run it on a Linux PC with an NVIDIA GPU, or in Google Colab\n" +
"# (see TRAINING.txt). Put this file next to metadata.csv and the wavs folder.\n" +
"set -e\n" +
"HERE=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n" +
"CKPT=\"${1:?Give the checkpoint to start from, e.g.  bash train.sh epoch=2164-step=1355540.ckpt  (see TRAINING.txt)}\"\n" +
"EPOCHS=\"${2:-}\"\n" +
"if [ ! -d piper1-gpl ]; then git clone https://github.com/OHF-voice/piper1-gpl.git; fi\n" +
"cd piper1-gpl\n" +
"python3 -m pip install -e '.[train]'\n" +
"./build_monotonic_align.sh\n" +
"python3 setup.py build_ext --inplace\n" +
"# the checkpoint's own epoch number + 1000 more, unless given\n" +
"if [ -z \"$EPOCHS\" ]; then START=$(basename \"$CKPT\" | sed -n 's/.*epoch=\\([0-9]*\\).*/\\1/p'); EPOCHS=$(( ${START:-0} + 1000 )); fi\n" +
"python3 -m piper.train fit \\\n" +
"  --data.voice_name \"" + SafeName + "\" \\\n" +
"  --data.csv_path \"$HERE/metadata.csv\" \\\n" +
"  --data.audio_dir \"$HERE/wavs\" \\\n" +
"  --model.sample_rate 22050 \\\n" +
"  --data.espeak_voice \"" + EspeakVoice + "\" \\\n" +
"  --data.cache_dir \"$HERE/cache\" \\\n" +
"  --data.config_path \"$HERE/" + VoiceFileName + ".onnx.json\" \\\n" +
"  --data.batch_size 16 \\\n" +
"  --trainer.max_epochs \"$EPOCHS\" \\\n" +
"  --ckpt_path \"$CKPT\"\n" +
"LAST=$(ls -t lightning_logs/version_*/checkpoints/*.ckpt | head -1)\n" +
"python3 -m piper.train.export_onnx --checkpoint \"$LAST\" --output-file \"$HERE/" + VoiceFileName + ".onnx\"\n" +
"echo \"Done: $HERE/" + VoiceFileName + ".onnx and " + VoiceFileName + ".onnx.json\"\n";
        }

        public string TrainingGuide(int clips)
        {
            double min = RecordedSeconds() / 60.0;
            return
"YOUR VOICE BANK — " + Name + "\n" +
"======================================\n" +
clips + " recordings, " + min.ToString("0.0", CultureInfo.InvariantCulture) + " minutes, language: " + (Lang == "fa" ? "Persian" : "English") + "\n\n" +
"Keep this zip safe: it is your voice. Even if you never train it, you can train it later\n" +
"(also with better tools that come out in the future).\n\n" +
"WHAT'S INSIDE\n" +
"  wavs\\        your recordings (22050 Hz, mono, 16-bit WAV)\n" +
"  metadata.csv  file name | sentence — the format Piper training uses\n" +
"  train.sh      a script that does all the steps below\n\n" +
"HOW MUCH TO RECORD\n" +
"  Fine-tuning works from about 15–20 minutes of clean speech; 1 hour or more sounds much better.\n" +
"  Same microphone, same room, same distance for every recording.\n\n" +
"TRAINING (needs an NVIDIA graphics card — or use Google Colab's free GPU)\n" +
"  Training makes a Piper voice from your recordings by fine-tuning an existing voice.\n" +
"  It takes from a few hours to a day.\n\n" +
"  1. Get a checkpoint to start from (a .ckpt file, \"medium\" quality):\n" +
"       https://huggingface.co/datasets/rhasspy/piper-checkpoints\n" +
"     A voice of the same language is best" + (Lang == "fa" ? " (look under fa/fa_IR/). If there is none, use\n     en/en_US/lessac/medium — it works for other languages too.\n" : ", e.g. en/en_US/lessac/medium.\n") +
"\n" +
"  2a. On your own Linux PC with an NVIDIA GPU (8 GB or more):\n" +
"        sudo apt-get install build-essential cmake ninja-build python3-venv espeak-ng\n" +
"        unzip this zip into a folder, put the .ckpt file in it, then:\n" +
"        bash train.sh the-checkpoint-file.ckpt\n\n" +
"  2b. In Google Colab (colab.research.google.com — free, in the browser):\n" +
"        Runtime → Change runtime type → GPU. Upload this zip and the .ckpt file, then run:\n" +
"          !apt-get -q install -y espeak-ng ninja-build\n" +
"          !unzip -q -o /content/" + SafeName + "-voicebank.zip -d /content/voicebank\n" +
"          !bash /content/voicebank/train.sh /content/the-checkpoint-file.ckpt\n" +
"        Colab stops after a few hours; keep the newest .ckpt from piper1-gpl/lightning_logs and\n" +
"        continue later with:  !bash /content/voicebank/train.sh <that .ckpt>\n\n" +
"  The script trains about 1000 more epochs than the checkpoint already has. You can stop earlier,\n" +
"  export, and listen — more training usually means a more natural voice.\n\n" +
"  3. When it finishes you get two files:\n" +
"        " + VoiceFileName + ".onnx\n" +
"        " + VoiceFileName + ".onnx.json\n" +
"     Put both in Piper Studio's \"voices\" folder (Voices menu → Open voices folder), then\n" +
"     Voices → Refresh voice list. Your own voice is now in the list.\n\n" +
"Piper's training guide: https://github.com/OHF-voice/piper1-gpl/blob/main/docs/TRAINING.md\n";
        }
    }

    // ---------------------------------------------------------------- recording quality
    public class TakeInfo
    {
        public double Seconds, Peak, NoiseDb, SpeechDb;
        public bool Clipped, TooQuiet, TooShort, Noisy, Cut;
        public string Problems { get { var l = new List<string>(); if (TooShort) l.Add("too short"); if (TooQuiet) l.Add("too quiet"); if (Clipped) l.Add("too loud (clipping)"); if (Noisy) l.Add("background noise"); if (Cut) l.Add("speech cut off at the end"); return string.Join(", ", l); } }
        public bool Ok { get { return !TooShort && !TooQuiet && !Clipped; } }
    }

    public static class TakeTools
    {
        // Trims silence at both ends (keeps 0.2 s), and reports quality problems.
        public static short[] TrimAndCheck(short[] s, int rate, out TakeInfo info)
        {
            info = new TakeInfo();
            int frame = rate / 50;   // 20 ms
            int n = s.Length / Math.Max(1, frame);
            var db = new double[n];
            int clip = 0, peak = 0;
            for (int f = 0; f < n; f++)
            {
                double sum = 0;
                for (int i = f * frame; i < (f + 1) * frame; i++) { int a = Math.Abs((int)s[i]); sum += (double)s[i] * s[i]; if (a > peak) peak = a; if (a >= 32000) clip++; }
                db[f] = 10 * Math.Log10(sum / frame / (32768.0 * 32768.0) + 1e-12);
            }
            info.Peak = peak / 32768.0;
            if (n < 10) { info.TooShort = true; info.Seconds = s.Length / (double)rate; return s; }
            var sorted = db.OrderBy(x => x).ToArray();
            info.NoiseDb = sorted[(int)(n * 0.1)];
            info.SpeechDb = sorted[(int)(n * 0.95)];
            double gate = Math.Max(info.NoiseDb + 10, info.SpeechDb - 35);
            int first = Array.FindIndex(db, x => x > gate), last = Array.FindLastIndex(db, x => x > gate);
            if (first < 0) { info.TooQuiet = true; info.Seconds = 0; return s; }
            info.Cut = last >= n - 2;    // still talking in the last 40 ms
            int pad = (int)(0.2 * rate);
            int a0 = Math.Max(0, first * frame - pad), a1 = Math.Min(s.Length, (last + 1) * frame + pad);
            var o = new short[a1 - a0];
            Array.Copy(s, a0, o, 0, o.Length);
            info.Seconds = o.Length / (double)rate;
            info.TooShort = info.Seconds < 0.6;
            info.TooQuiet = info.SpeechDb < -38 || info.Peak < 0.06;
            info.Clipped = clip > 3;
            info.Noisy = info.NoiseDb > -45;
            return o;
        }
    }
}
