using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PiperStudio
{
    // ================================================================== Kokoro through the user's own Python install
    // If someone already installed the official "kokoro" Python package (pip install kokoro), Piper Studio can use it
    // too: it starts that python.exe with a small helper script that speaks the same line-by-line protocol as Piper.
    // This is the original PyTorch Kokoro with its own pronunciation engine (misaki), so it can sound a little better
    // than the built-in one, but it only works on a PC that has that Python setup.
    public static class KokoroPython
    {
        // Where people usually create it; the user can also pick python.exe on the Voice tab.
        public static List<string> Candidates(string configured)
        {
            var l = new List<string>();
            if (!string.IsNullOrEmpty(configured)) l.Add(configured);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var root in new[] { @"C:\kokoro", Path.Combine(home ?? "", "kokoro"), @"D:\kokoro" })
                foreach (var venv in new[] { ".venv", "venv", "env" })
                    l.Add(Path.Combine(root, venv, "Scripts", "python.exe"));
            return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // python.exe of a venv (…\Scripts\python.exe) with kokoro in its Lib\site-packages
        public static bool HasKokoro(string python)
        {
            try
            {
                if (string.IsNullOrEmpty(python) || !File.Exists(python)) return false;
                string scripts = Path.GetDirectoryName(Path.GetFullPath(python));
                string root = Path.GetDirectoryName(scripts);
                foreach (var sp in new[] { Path.Combine(root, "Lib", "site-packages"), Path.Combine(scripts, "Lib", "site-packages") })
                    if (Directory.Exists(Path.Combine(sp, "kokoro"))) return true;
            }
            catch { }
            return false;
        }

        public static string Find(string configured)
        {
            foreach (var p in Candidates(configured)) if (HasKokoro(p)) return Path.GetFullPath(p);
            return null;
        }

        public static List<VoiceInfo> Voices(string python)
        {
            var list = new List<VoiceInfo>();
            if (python == null) return list;
            foreach (var v in KokoroInstall.EnglishVoices(KokoroInstall.SpeakerTable(54)))
            {
                v.Engine = "kokoro-py";
                v.FileName = "kokoropy-" + v.KokoroId;
                v.KokoroPython = python;
                v.OnnxPath = python;
                list.Add(v);
            }
            return list;
        }

        // The helper script is kept inside the exe and written next to the settings when needed.
        public static string ScriptPath()
        {
            string path = AppPaths.In("kokoro_python_host.py");
            try
            {
                if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != Script)
                    File.WriteAllText(path, Script, new UTF8Encoding(false));
            }
            catch { }
            return path;
        }

        public const string Script =
@"# Piper Studio helper: speaks with the 'kokoro' Python package. Written by Piper Studio - edits are overwritten.
# Protocol: one JSON request per line on stdin: {""text"": ..., ""voice"": ""af_heart"", ""speed"": 1.0, ""output_file"": ...}
# Reply: the output file path on one line (empty line = failed, details on stderr).
import os, sys, json, wave, traceback
os.environ.setdefault('HF_HUB_DISABLE_TELEMETRY', '1')
os.environ.setdefault('HF_HUB_ETAG_TIMEOUT', '3')
os.environ.setdefault('HF_HUB_DISABLE_PROGRESS_BARS', '1')
os.environ.setdefault('TOKENIZERS_PARALLELISM', 'false')
_out = sys.stdout.buffer
sys.stdout = sys.stderr          # libraries print to stdout: keep it clean for our replies

def reply(s):
    _out.write((s + '\n').encode('utf-8'))
    _out.flush()

try:
    import numpy as np
    from kokoro import KPipeline
except Exception as e:
    print('Kokoro (Python) could not start: %r' % (e,), file=sys.stderr)
    sys.exit(2)

REPO = 'hexgrad/Kokoro-82M'
pipes = {}

def pipe(code):
    if code not in pipes:
        model = next(iter(pipes.values())).model if pipes else True
        pipes[code] = KPipeline(lang_code=code, repo_id=REPO, model=model)
    return pipes[code]

try:
    pipe(sys.argv[1] if len(sys.argv) > 1 else 'a')
    print('Kokoro (Python) ready', file=sys.stderr)
except Exception as e:
    print('Kokoro (Python) failed to load: %r' % (e,), file=sys.stderr)
    sys.exit(3)

for raw in sys.stdin.buffer:
    line = raw.decode('utf-8', 'replace').strip()
    if not line:
        continue
    try:
        d = json.loads(line)
        voice = d.get('voice') or 'af_heart'
        code = 'b' if voice.startswith('b') else 'a'
        speed = max(0.3, min(3.0, float(d.get('speed', 1.0))))
        parts = []
        for r in pipe(code)(d['text'], voice=voice, speed=speed, split_pattern=None):
            a = r.audio
            if a is None:
                continue
            a = a.detach().cpu().numpy() if hasattr(a, 'detach') else np.asarray(a)
            parts.append(a.reshape(-1))
        if not parts:
            print('Kokoro (Python) produced no audio', file=sys.stderr)
            reply('')
            continue
        audio = np.clip(np.concatenate(parts), -1.0, 1.0)
        pcm = (audio * 32767.0).astype('<i2').tobytes()
        with wave.open(d['output_file'], 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(24000); w.writeframes(pcm)
        reply(d['output_file'])
    except Exception:
        traceback.print_exc()
        reply('')
";
    }
}
