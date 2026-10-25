using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using PiperStudio;

static class T2
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (ok) passes++; else fails++; }

    static int Main()
    {
        // ---------------------------------------------------------------- replacement rules
        var rules = TextTools.ParseReplacements("AI=ای آی\npixune = پیکسیون\n%=درصد\ne.g.=for example\nسلام=درود\n# comment=x\nUS=یو اس");
        Check(rules.Count == 6, "rules parsed, comment skipped, spaces trimmed (" + rules.Count + ")");
        Check(TextTools.ApplyRules("MAIN road", rules) == "MAIN road", "AI does not change MAIN");
        Check(TextTools.ApplyRules("AI is here", rules) == "ای آی is here", "AI replaced as a whole word");
        Check(TextTools.ApplyRules("said", rules) == "said", "lowercase 'ai' inside a word untouched");
        Check(TextTools.ApplyRules("I work at Pixune.", rules) == "I work at پیکسیون.", "lowercase rule matches any case (Pixune)");
        Check(TextTools.ApplyRules("PIXUNE!", rules) == "پیکسیون!", "lowercase rule matches PIXUNE");
        Check(TextTools.ApplyRules("50% off", rules) == "50درصد off", "symbol rule works next to digits");
        Check(TextTools.ApplyRules("tools, e.g. hammers", rules) == "tools, for example hammers", "rule with dots");
        Check(TextTools.ApplyRules("سلامتی", rules) == "سلامتی", "Persian rule doesn't change a longer word");
        Check(TextTools.ApplyRules("سلام دوست", rules) == "درود دوست", "Persian whole word replaced");
        Check(TextTools.ApplyRules("tell us", rules) == "tell us", "capital rule US doesn't match 'us'");
        Check(TextTools.ApplyRules("the US army", rules) == "the یو اس army", "capital rule US matches US");
        Check(TextTools.ApplyRules("costs $5", TextTools.ParseReplacements("costs=price $1")) == "price $1 $5", "replacement text with $ is literal");

        // ---------------------------------------------------------------- secrets
        string enc = Secret.Protect("123456:ABC-secret");
        Check(enc.StartsWith("dpapi:") || enc.StartsWith("b64:"), "token is stored encoded (" + enc.Substring(0, Math.Min(6, enc.Length)) + "…)");
        Check(!enc.Contains("ABC-secret"), "plain token not visible in stored value");
        Check(Secret.Unprotect(enc) == "123456:ABC-secret", "token round-trips");
        Check(Secret.Unprotect("123:plainOld") == "123:plainOld", "old plain-text tokens still read");
        Check(Secret.Protect("") == "" && Secret.Unprotect("") == "", "empty token stays empty");

        // ---------------------------------------------------------------- settings migration
        string tmp = Path.Combine(Path.GetTempPath(), "ps_mig_" + Guid.NewGuid().ToString("N"));
        string from = Path.Combine(tmp, "app"), to = Path.Combine(tmp, "data");
        Directory.CreateDirectory(from);
        File.WriteAllText(Path.Combine(from, "PiperStudio.ini"), "bottoken=123:plain\nvoice=x");
        File.WriteAllText(Path.Combine(from, "replacements.txt"), "AI=ای آی");
        var moved = AppPaths.MigrateOldSettings(from, to);
        Check(moved.Count == 2 && File.Exists(Path.Combine(to, "PiperStudio.ini")) && File.Exists(Path.Combine(to, "replacements.txt")), "old settings moved to the data folder");
        Check(!File.Exists(Path.Combine(from, "PiperStudio.ini")), "old ini (with plain token) removed from the program folder");
        File.WriteAllText(Path.Combine(from, "PiperStudio.ini"), "voice=y");
        Check(AppPaths.MigrateOldSettings(from, to).Count == 0 && File.ReadAllText(Path.Combine(to, "PiperStudio.ini")).Contains("voice=x"), "second migration doesn't overwrite newer settings");
        Check(AppPaths.CanWrite(to), "CanWrite works");

        // ---------------------------------------------------------------- stereo, pan, ducking
        int R = 48000;
        var p = new Project();
        var speech = new Track { Name = "Speech" };
        var music = new Track { Name = "Music", Duck = true, DuckDb = 12 };
        var tone = new short[R * 6]; for (int i = 0; i < tone.Length; i++) tone[i] = 10000;
        var voice = new short[R * 1]; for (int i = 0; i < voice.Length; i++) voice[i] = 8000;
        music.Clips.Add(Clip.FromData(tone, 0, "m", "audio"));
        speech.Clips.Add(new Clip { Data = voice, Start = R * 2, In = 0, Out = voice.Length, Kind = "speech", Text = "Hello there." });
        p.Tracks.Add(speech); p.Tracks.Add(music);
        var mono = p.Mix(0, p.Length);
        double f12 = Project.DuckFactor(12);
        Check(Math.Abs(mono[R / 2] - 10000) < 2, "music at full level far from speech (" + mono[R / 2] + ")");
        Check(Math.Abs(mono[(int)(R * 2.5)] - (8000 + 10000 * f12)) < 3, "music ducked by 12 dB under speech (" + mono[(int)(R * 2.5)] + " vs " + (int)(8000 + 10000 * f12) + ")");
        Check(mono[(int)(R * 1.9)] < 10000 && mono[(int)(R * 1.9)] > 10000 * f12, "duck starts smoothly just before speech (" + mono[(int)(R * 1.9)] + ")");
        Check(Math.Abs(mono[(int)(R * 4.5)] - 10000) < 2, "music back to full after release (" + mono[(int)(R * 4.5)] + ")");
        // bridging: two sentences 0.5 s apart keep music down in between
        var p2 = p.Clone();
        p2.Tracks[0].Clips.Add(new Clip { Data = voice, Start = (long)(R * 3.5), In = 0, Out = voice.Length, Kind = "speech", Text = "Second." });
        var m2 = p2.Mix(0, p2.Length);
        Check(Math.Abs(m2[(int)(R * 3.25)] - 10000 * f12) < 3, "music stays down in a short gap between sentences (" + m2[(int)(R * 3.25)] + ")");
        // muted speech doesn't duck
        var p3 = p.Clone(); p3.Tracks[0].Mute = true;
        Check(Math.Abs(p3.Mix(0, p3.Length)[(int)(R * 2.5)] - 10000) < 2, "muted speech track doesn't duck the music");
        // pan
        var pp = new Project();
        var tp = new Track { Pan = -1 }; tp.Clips.Add(Clip.FromData(voice, 0, "v", "audio")); pp.Tracks.Add(tp);
        var st = pp.MixStereo(0, 100);
        Check(st[0] == 8000 && st[1] == 0, "pan full left: L=8000 R=0 (" + st[0] + "," + st[1] + ")");
        tp.Pan = 0.5f; st = pp.MixStereo(0, 100);
        Check(st[0] == 4000 && st[1] == 8000, "pan half right: L=4000 R=8000 (" + st[0] + "," + st[1] + ")");
        // stereo clip
        var left = new short[1000]; var right = new short[1000];
        for (int i = 0; i < 1000; i++) { left[i] = 1000; right[i] = -2000; }
        var ps = new Project(); var ts = new Track(); ts.Clips.Add(Clip.FromStereo(left, right, 0, "s", "audio")); ps.Tracks.Add(ts);
        st = ps.MixStereo(0, 10);
        Check(st[0] == 1000 && st[1] == -2000, "stereo clip keeps both channels");
        Check(ps.HasStereo() && !p.HasStereo(), "HasStereo detects stereo content");

        // ---------------------------------------------------------------- chunked = whole, streamed WAV export
        var whole = p2.MixStereo(0, (int)p2.Length);
        var parts = new List<short>();
        for (long pos = 0; pos < p2.Length; pos += 2880) parts.AddRange(p2.MixStereo(pos, (int)Math.Min(2880, p2.Length - pos)));
        Check(parts.Count == whole.Length && parts.SequenceEqual(whole), "mixing in 60 ms pieces gives exactly the same audio");
        string wav = Path.Combine(tmp, "mix.wav");
        p2.MixToWav(wav, 0, p2.Length, null, null, CancellationToken.None);
        var wb = File.ReadAllBytes(wav);
        Check(wb.Length == 44 + whole.Length * 2, "streamed WAV has the right size");
        bool same = true; for (int i = 0; i < whole.Length; i++) if (BitConverter.ToInt16(wb, 44 + 2 * i) != whole[i]) { same = false; break; }
        Check(same, "streamed WAV content matches the mix");

        // ---------------------------------------------------------------- save / load v2 + v1 compatibility + compression
        var sp = p2.Clone();
        sp.Tracks.Add(ts.Clone());
        sp.Tracks[1].Pan = -0.25f; sp.Tracks[1].DuckDb = 18;
        string proj = Path.Combine(tmp, "t.pstudio");
        sp.Save(proj);
        var q = Project.Load(proj);
        Check(q.Tracks.Count == 3 && q.Tracks[1].Duck && Math.Abs(q.Tracks[1].DuckDb - 18) < 1e-4 && Math.Abs(q.Tracks[1].Pan + 0.25f) < 1e-6, "v2 project keeps duck, duck amount and pan");
        Check(q.Tracks[2].Clips[0].DataR != null && q.Tracks[2].Clips[0].DataR[5] == -2000, "v2 project keeps stereo audio");
        Check(q.MixStereo(0, (int)q.Length).SequenceEqual(sp.MixStereo(0, (int)sp.Length)), "v2 project sounds identical after loading");
        // realistic-ish audio for compression: speech-like bursts with pauses + quiet noise
        var rnd = new Random(1);
        var speechLike = new short[R * 20];
        for (int i = 0; i < speechLike.Length; i++)
        {
            double t = i / (double)R;
            bool talking = (t % 2.0) < 1.3;
            double v = talking ? Math.Sin(2 * Math.PI * 140 * t) * 6000 * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 * t)) + Math.Sin(2 * Math.PI * 1100 * t) * 900 : 0;
            speechLike[i] = (short)(v + rnd.Next(-40, 41));
        }
        var cp = new Project(); var ct2 = new Track(); ct2.Clips.Add(Clip.FromData(speechLike, 0, "s", "speech")); cp.Tracks.Add(ct2);
        string cpath = Path.Combine(tmp, "c.pstudio");
        cp.Save(cpath);
        long rawBytes = speechLike.Length * 2, packed = new FileInfo(cpath).Length;
        Console.WriteLine("    speech-like audio: raw " + rawBytes / 1024 + " KB -> project " + packed / 1024 + " KB (" + (100 - packed * 100 / rawBytes) + "% smaller)");
        Check(packed < rawBytes * 0.7, "project audio is compressed (lossless)");
        Check(Project.Load(cpath).Tracks[0].Clips[0].Data.SequenceEqual(speechLike), "compression is lossless");
        // v1 file written the old way
        string v1 = Path.Combine(tmp, "old.pstudio");
        using (var fs = File.Create(v1))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("project.json");
            using (var w = new StreamWriter(e.Open())) w.Write("{\"format\":\"piperstudio-tracks\",\"version\":1,\"rate\":48000,\"tracks\":[{\"name\":\"Old\",\"volume\":0.5,\"mute\":false,\"solo\":false,\"clips\":[{\"data\":0,\"start\":10,\"in\":0,\"out\":4,\"gain\":1,\"fadeIn\":0,\"fadeOut\":0,\"name\":\"c\",\"text\":null,\"kind\":\"audio\"}]}]}");
            var a = zip.CreateEntry("audio/0.raw");
            using (var s = a.Open()) { var b = new byte[8]; Buffer.BlockCopy(new short[] { 1, 2, 3, 4 }, 0, b, 0, 8); s.Write(b, 0, 8); }
        }
        var old = Project.Load(v1);
        Check(old.Tracks.Count == 1 && old.Tracks[0].Name == "Old" && old.Tracks[0].Clips[0].Data.SequenceEqual(new short[] { 1, 2, 3, 4 }) && !old.Tracks[0].Duck && old.Tracks[0].Pan == 0, "projects saved by the previous version still open");

        // ---------------------------------------------------------------- subtitles
        Check(Subtitles.Time(3723.456) == "01:02:03,456", "SRT time format");
        var cues = Subtitles.MakeCues(p2.SubtitleEntries().Select(x => new Subtitles.Cue { Start = x.Start / (double)R, End = x.End / (double)R, Text = x.Text }));
        Check(cues.Count == 2 && Math.Abs(cues[0].Start - 2.0) < 1e-6 && Math.Abs(cues[0].End - 3.0) < 1e-6 && cues[1].Text == "Second.", "cues come from speech clips with exact times");
        string srt = Subtitles.ToSrt(cues);
        Check(srt.StartsWith("1\r\n00:00:02,000 --> 00:00:03,000\r\nHello there.\r\n\r\n2\r\n"), "SRT text layout");
        var longCue = Subtitles.MakeCues(new[] { new Subtitles.Cue { Start = 0, End = 10, Text = string.Join(" ", Enumerable.Repeat("word", 40)) } });
        Check(longCue.Count >= 2 && Math.Abs(longCue[longCue.Count - 1].End - 10) < 1e-6 && longCue.All(c => c.Text.Length <= 84), "long sentences split into readable cues, total time kept (" + longCue.Count + " cues)");
        var fa = Subtitles.ToSrt(Subtitles.MakeCues(new[] { new Subtitles.Cue { Start = 0, End = 1, Text = "سلام [pause 2] دنیا!" } }));
        Check(fa.Contains("‏سلام دنیا!‏"), "Persian subtitle: pause marker removed, RTL marks added");
        Check(Subtitles.ToSrt(Subtitles.MakeCues(new[] { new Subtitles.Cue { Start = 0, End = 2, Text = "This is a fairly long subtitle line that should wrap onto two lines" } })).Split('\n').Length >= 5, "long cue wraps onto two lines");

        // ---------------------------------------------------------------- latency detection
        double[] times;
        var beeps = Latency.MakeBeeps(R, out times);
        foreach (var delayMs in new[] { 0, 37, 187, 420 })
        {
            double playStart = 0.153;
            var recd = new short[beeps.Length + R * 2];
            var rr = new Random(delayMs + 7);
            int off = (int)((playStart + delayMs / 1000.0) * R);
            for (int i = 0; i < recd.Length; i++) recd[i] = (short)rr.Next(-150, 151);
            for (int i = 0; i < beeps.Length; i++)
            {
                int j = off + i; if (j >= recd.Length) break;
                int v = recd[j] + beeps[i] / 4;                                  // quieter, like a mic hearing speakers
                if (j + R / 50 < recd.Length) recd[j + R / 50] = (short)Math.Max(-32768, Math.Min(32767, recd[j + R / 50] + beeps[i] / 12));   // a little echo
                recd[j] = (short)Math.Max(-32768, Math.Min(32767, v));
            }
            int matched;
            double ms = Latency.Detect(recd, R, times, playStart, out matched);
            Check(!double.IsNaN(ms) && Math.Abs(ms - delayMs) <= 3 && matched == 6, "latency " + delayMs + " ms detected as " + (double.IsNaN(ms) ? "none" : ms.ToString("0.0") + " ms") + " (" + matched + "/6 beeps)");
        }
        int m0; Check(double.IsNaN(Latency.Detect(new short[R * 5], R, times, 0.15, out m0)), "silence: no false measurement");

        // ---------------------------------------------------------------- phrases & history files
        var ph = MainForm.ParsePhrases("# comment\n## Basics\nYes.\nNo.\n\n## گفتگو\nسلام!\n");
        Check(ph.Count == 2 && ph[0].Items.Count == 2 && ph[1].Name == "گفتگو" && ph[1].Items[0] == "سلام!", "phrases file parsed");
        var ph2 = MainForm.ParsePhrases(MainForm.SerializePhrases(ph));
        Check(ph2.Count == 2 && ph2[1].Items[0] == "سلام!" && ph2[0].Items[1] == "No.", "phrases save/load round trip");
        Check(MainForm.ParsePhrases("Hello\nBye").Count == 1, "phrases without a group header still load");
        var hlines = "2026-10-24T23:50:01\tsay\tline one\\nline two with \\\\ backslash\r\n2026-10-24T23:51:00\tphrase\tYes.\r\nbroken line\r\n";
        var hist = MainForm.ParseHistory(hlines);
        Check(hist.Count == 2 && hist[0].Text == "line one\nline two with \\ backslash" && hist[1].Source == "phrase", "history file parsed (newlines and backslashes kept)");

        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine(fails == 0 ? "ALL " + passes + " PASSED" : fails + " FAILED, " + passes + " passed");
        return fails;
    }
}
