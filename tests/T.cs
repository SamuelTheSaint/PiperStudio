using System; using System.Linq; using PiperStudio;
static class T {
  static int fails = 0;
  static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
  static string FindFfmpeg() {
    foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
      foreach (var n in new[] { "ffmpeg.exe", "ffmpeg" })
        try { var f = System.IO.Path.Combine(d.Trim('"'), n); if (System.IO.File.Exists(f)) return f; } catch { }
    return null;
  }
  static int Main() {
    var p = new Project();
    var a = new short[48000]; for (int i = 0; i < a.Length; i++) a[i] = 10000;
    var b = new short[24000]; for (int i = 0; i < b.Length; i++) b[i] = 5000;
    var t1 = new Track { Name = "Speech" }; var t2 = new Track { Name = "Music", Volume = 0.5f };
    t1.Clips.Add(Clip.FromData(a, 0, "a", "speech")); t2.Clips.Add(Clip.FromData(b, 12000, "b", "audio"));
    p.Tracks.Add(t1); p.Tracks.Add(t2);
    Check(p.Length == 48000, "project length = 1 s");
    var m = p.Mix(0, p.Length);
    Check(m[0] == 10000, "track 1 alone = 10000");
    Check(m[20000] == 12500, "overlap mixes 10000 + 5000*0.5 = 12500 (got " + m[20000] + ")");
    t2.Mute = true; Check(p.Mix(0, 48000)[20000] == 10000, "mute works");
    t2.Mute = false; t2.Solo = true; Check(p.Mix(0, 48000)[0] == 0 && p.Mix(0, 48000)[20000] == 2500, "solo works");
    t2.Solo = false;
    t1.Clips[0].FadeIn = 0.5; var f = p.Mix(0, 48000); Check(f[0] == 0 && Math.Abs(f[12000] - (5000 + 2500)) < 5, "fade in (got " + f[12000] + ")");
    t1.Clips[0].FadeIn = 0;
    // split
    var hist = new History(); hist.Push(p);
    var c = t1.Clips[0]; long ph = 30000; long cut = c.In + (ph - c.Start);
    var c2 = c.Clone(); c2.In = cut; c2.Start = ph; c.Out = cut; t1.Clips.Add(c2);
    Check(t1.Clips.Count == 2 && c.Length + c2.Length == 48000 && c2.Data == a, "split shares audio, lengths add up");
    var m2 = p.Mix(0, 48000); Check(m2.SequenceEqual(m) , "split doesn't change the sound");
    c2.Start += 24000; Check(p.Length == 72000, "moving clip extends project");
    var restored = hist.Undo(p); Check(restored.Tracks[0].Clips.Count == 1 && restored.Length == 48000, "undo restores state");
    var redone = hist.Redo(restored); Check(redone.Tracks[0].Clips.Count == 2, "redo works");
    // gain clip clamp
    c.Gain = 4f; Check(p.Mix(0, 1)[0] == 32767, "clipping is clamped");
    c.Gain = 1f;
    // save/load
    string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "t.pstudio");
    p.Tracks[0].Clips[0].Text = "سلام \"quoted\" \\ text"; p.Tracks[0].Name = "گفتار";
    p.Save(path);
    var q = Project.Load(path);
    Check(q.Tracks.Count == 2 && q.Tracks[0].Clips.Count == 2, "load: tracks and clips");
    Check(q.Tracks[0].Clips[0].Data == q.Tracks[0].Clips[1].Data, "load: shared audio stays shared");
    Check(q.Tracks[0].Clips[0].Text == "سلام \"quoted\" \\ text" && q.Tracks[0].Name == "گفتار", "load: Persian text and quotes survive");
    Check(q.Mix(0, q.Length).SequenceEqual(p.Mix(0, p.Length)), "load: identical mix");
    Check(Math.Abs(q.Tracks[1].Volume - 0.5f) < 1e-6, "load: volume");
    Console.WriteLine("zip size: " + new System.IO.FileInfo(path).Length + " bytes");
    // import via ffmpeg + wav reader
    var w = new short[22050]; for (int i = 0; i < w.Length; i++) w[i] = (short)(Math.Sin(i * 0.05) * 8000);
    string inWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ps_in.wav");
    Audio.WriteWav(inWav, w, 22050);
    var imp = AudioImport.Load(inWav, 48000, null); Check(Math.Abs(imp.Length - 48000) < 5, "WAV import resamples 22050 -> 48000 (" + imp.Length + ")");
    string ff = FindFfmpeg();
    if (ff == null) Console.WriteLine("SKIP FFmpeg import (ffmpeg not installed)");
    else { var imp2 = AudioImport.Load(inWav, 48000, ff); Check(Math.Abs(imp2.Length - 48000) < 50, "FFmpeg import (" + imp2.Length + ")"); }
    Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
    return fails;
  }
}
