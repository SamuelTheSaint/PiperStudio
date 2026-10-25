using System; using System.IO; using System.Linq; using System.Threading; using PiperStudio;
static class T3 {
  static int fails = 0, passes = 0;
  static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (ok) passes++; else fails++; }
  static short[] Const(int n, short v) { var a = new short[n]; for (int i=0;i<n;i++) a[i]=v; return a; }
  static Project P2() {
    var p = new Project { Rate = 1000 };
    var t1 = new Track { Name = "a" }; var t2 = new Track { Name = "b" };
    t1.Clips.Add(Clip.FromData(Const(1000, 1000), 0, "c1", "audio"));      // 0..1000
    t1.Clips.Add(Clip.FromData(Const(1000, 2000), 2000, "c2", "audio"));   // 2000..3000
    t2.Clips.Add(Clip.FromData(Const(3000, 3000), 500, "c3", "audio"));    // 500..3500
    p.Tracks.Add(t1); p.Tracks.Add(t2); return p;
  }
  static int Main() {
    // ---- ripple delete
    var p = P2(); p.DeleteRange(800, 2200);
    var a = p.Tracks[0].Clips.OrderBy(c=>c.Start).ToList(); var b = p.Tracks[1].Clips.OrderBy(c=>c.Start).ToList();
    Check(a.Count == 2 && a[0].Start == 0 && a[0].Length == 800 && a[1].Start == 800 && a[1].Length == 800, "ripple delete trims and moves clips on track 1");
    Check(b.Count == 2 && b[0].Start == 500 && b[0].Length == 300 && b[1].Start == 800 && b[1].Length == 1300, "ripple delete splits a clip spanning the range on track 2");
    Check(p.Length == 2100, "project is 1400 shorter (" + p.Length + ")");
    // ---- silence
    p = P2(); p.SilenceRange(800, 2200);
    b = p.Tracks[1].Clips.OrderBy(c=>c.Start).ToList();
    Check(b.Count == 2 && b[1].Start == 2200 && p.Length == 3500, "silence keeps timing");
    var m = p.Mix(0, 3500);
    Check(m[1500] == 0 && m[900] == 0, "silenced range is quiet");
    // ---- split at time
    p = P2(); int n = p.SplitAll(2500);
    Check(n == 2 && p.Tracks.Sum(t => t.Clips.Count) == 5, "split at time cuts every clip that crosses it");
    var before = P2().Mix(0, 3500); var after = p.Mix(0, 3500);
    Check(before.SequenceEqual(after), "splitting doesn't change the sound");
    // ---- crossfade: overlapping clips on one track
    var q = new Project { Rate = 1000 }; var tr = new Track(); q.Tracks.Add(tr);
    tr.Clips.Add(Clip.FromData(Const(1000, 8000), 0, "x", "audio"));       // 0..1000
    tr.Clips.Add(Clip.FromData(Const(1000, 8000), 800, "y", "audio"));     // 800..1800 (200 overlap)
    long fi, fo; q.EffectiveFades(tr, tr.Clips[1], out fi, out fo);
    Check(fi == 200, "second clip fades in over the overlap (" + fi + ")");
    q.EffectiveFades(tr, tr.Clips[0], out fi, out fo);
    Check(fo == 200, "first clip fades out over the overlap (" + fo + ")");
    var mm = q.Mix(0, 1800);
    Check(mm.Skip(810).Take(180).All(v => Math.Abs(v - 8000) <= 60), "crossfade keeps a steady level (" + mm[850] + ", " + mm[900] + ", " + mm[950] + ")");
    Check(mm[500] == 8000 && mm[1500] == 8000, "outside the overlap nothing changes");
    // ---- loudness normalizing of a mix
    var lp = new Project(); var lt = new Track(); lp.Tracks.Add(lt);
    var tone = new short[48000 * 3]; for (int i=0;i<tone.Length;i++) tone[i]=(short)(3000*Math.Sin(2*Math.PI*440*i/48000.0));
    lt.Clips.Add(Clip.FromData(tone, 0, "tone", "audio"));
    double before1 = lp.MeasureLoudness(0, lp.Length, null, null, CancellationToken.None);
    string wav = Path.Combine(Path.GetTempPath(), "t3_norm.wav");
    lp.MixToWav(wav, 0, lp.Length, null, null, CancellationToken.None, -14);
    var bytes = File.ReadAllBytes(wav);
    int frames = (bytes.Length - 44) / 4;
    var L = new float[frames]; var R = new float[frames]; int pk = 0;
    for (int i=0;i<frames;i++){ short l=BitConverter.ToInt16(bytes,44+4*i), r=BitConverter.ToInt16(bytes,46+4*i); L[i]=l; R[i]=r; pk=Math.Max(pk,Math.Abs((int)l)); }
    var meter = new LoudnessMeter(48000, 2); meter.Add(new[]{L,R}, frames);
    double got = meter.Integrated();
    Check(frames == tone.Length, "normalized export keeps the exact length (" + frames + ")");
    Check(Math.Abs(got + 14) < 0.5, "mix normalized from " + before1.ToString("0.0") + " to " + got.ToString("0.0") + " LUFS (target -14)");
    Check(pk <= 29300, "peaks stay under -1 dBFS (" + pk + ")");
    var plain = Path.Combine(Path.GetTempPath(), "t3_plain.wav");
    lp.MixToWav(plain, 0, lp.Length, null, null, CancellationToken.None);
    Check(new FileInfo(plain).Length == bytes.Length, "export without normalizing still works");
    // ---- noise reduction
    var rnd = new Random(5); var sig = new short[48000 * 3];
    for (int i=0;i<sig.Length;i++){ double s = (i > 48000 && i < 96000) ? 6000*Math.Sin(2*Math.PI*300*i/48000.0) : 0; sig[i]=(short)(s + (rnd.NextDouble()*2-1)*800); }
    var den = NoiseReducer.Process(sig, 48000, 12);
    Func<short[],int,int,double> rms = (x,a0,a1) => { double s=0; for(int i=a0;i<a1;i++) s+=(double)x[i]*x[i]; return Math.Sqrt(s/(a1-a0)); };
    double nBefore = rms(sig, 5000, 40000), nAfter = rms(den, 5000, 40000), tBefore = rms(sig, 60000, 90000), tAfter = rms(den, 60000, 90000);
    Check(20*Math.Log10(nBefore/nAfter) > 7, "noise reduced by " + (20*Math.Log10(nBefore/nAfter)).ToString("0.0") + " dB");
    Check(Math.Abs(20*Math.Log10(tBefore/tAfter)) < 1.0, "the tone keeps its level (" + (20*Math.Log10(tAfter/tBefore)).ToString("0.00") + " dB)");
    Check(den.Length == sig.Length, "same length after noise reduction");
    Console.WriteLine(fails == 0 ? "ALL PASSED (" + passes + ")" : fails + " FAILED");
    return fails;
  }
}
