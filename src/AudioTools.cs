using System;
using System.Collections.Generic;
using System.Linq;

namespace PiperStudio
{
    // ================================================================== loudness (ITU-R BS.1770 / EBU R128)
    // Measures integrated loudness in LUFS, the number YouTube, Instagram, Spotify and podcast apps use.
    public class LoudnessMeter
    {
        class Biquad
        {
            double b0, b1, b2, a1, a2, z1, z2;
            public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            { this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = a1 / a0; this.a2 = a2 / a0; }
            public double Run(double x) { double y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; return y; }
        }

        readonly int channels, step;       // step = 100 ms
        readonly Biquad[] shelf, hp;
        readonly double[] acc;              // sum of squares in the current 100 ms step, per channel
        int inStep;
        readonly List<double> steps = new List<double>();   // summed (over channels) mean square per 100 ms step

        public LoudnessMeter(int rate, int channels)
        {
            this.channels = channels;
            step = Math.Max(1, rate / 10);
            shelf = new Biquad[channels]; hp = new Biquad[channels]; acc = new double[channels];
            for (int c = 0; c < channels; c++)
            {
                // K-weighting: high shelf (+4 dB above ~1.7 kHz) and a high-pass at ~38 Hz
                double G = 3.999843853973347, Q = 0.7071752369554196, fc = 1681.974450955533;
                double A = Math.Pow(10, G / 40), w0 = 2 * Math.PI * fc / rate, alpha = Math.Sin(w0) / (2 * Q), cw = Math.Cos(w0), sa = 2 * Math.Sqrt(A) * alpha;
                shelf[c] = new Biquad(A * ((A + 1) + (A - 1) * cw + sa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sa),
                                      (A + 1) - (A - 1) * cw + sa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sa);
                double Q2 = 0.5003270373238773, fc2 = 38.13547087602444;
                double w2 = 2 * Math.PI * fc2 / rate, al2 = Math.Sin(w2) / (2 * Q2), c2 = Math.Cos(w2);
                hp[c] = new Biquad((1 + c2) / 2, -(1 + c2), (1 + c2) / 2, 1 + al2, -2 * c2, 1 - al2);
            }
        }

        // samples: one float array per channel (full scale = 32768)
        public void Add(float[][] ch, int n)
        {
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    double y = hp[c].Run(shelf[c].Run(ch[c][i] / 32768.0));
                    acc[c] += y * y;
                }
                if (++inStep == step)
                {
                    double s = 0;
                    for (int c = 0; c < channels; c++) { s += acc[c] / step; acc[c] = 0; }
                    steps.Add(s); inStep = 0;
                }
            }
        }

        public void AddMono(short[] s, int offset, int n)
        {
            var f = new float[n];
            for (int i = 0; i < n; i++) f[i] = s[offset + i];
            Add(new[] { f }, n);
        }

        static double Lufs(double ms) { return -0.691 + 10 * Math.Log10(Math.Max(ms, 1e-12)); }

        // Integrated loudness with the absolute (-70 LUFS) and relative (-10 LU) gates. NaN if (almost) silent.
        public double Integrated()
        {
            var blocks = new List<double>();
            for (int i = 0; i + 4 <= steps.Count; i++) blocks.Add((steps[i] + steps[i + 1] + steps[i + 2] + steps[i + 3]) / 4);
            if (blocks.Count == 0 && steps.Count > 0) blocks.Add(steps.Average());   // shorter than 400 ms
            var abs = blocks.Where(b => Lufs(b) > -70).ToList();
            if (abs.Count == 0) return double.NaN;
            double rel = Lufs(abs.Average()) - 10;
            var gated = abs.Where(b => Lufs(b) > rel).ToList();
            return gated.Count == 0 ? double.NaN : Lufs(gated.Average());
        }
    }

    // Peak limiter with a short look-ahead, so normalized audio never clips (ceiling in dBFS, e.g. -1).
    public class Limiter
    {
        readonly int look, channels;
        readonly float ceiling;
        readonly float[][] delay;
        readonly float[] peakWin;
        int pos, filled;
        double gain = 1;
        readonly double release;

        public Limiter(int rate, int channels, double ceilingDb)
        {
            this.channels = channels;
            look = Math.Max(1, rate / 200);              // 5 ms
            ceiling = (float)(32767 * Math.Pow(10, ceilingDb / 20));
            delay = new float[channels][];
            for (int c = 0; c < channels; c++) delay[c] = new float[look];
            peakWin = new float[look];
            release = Math.Exp(-1.0 / (0.08 * rate));    // 80 ms
        }

        public int Latency { get { return look; } }

        // In place: output is the input delayed by Latency samples.
        public void Process(float[][] ch, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float pk = 0;
                for (int c = 0; c < channels; c++) pk = Math.Max(pk, Math.Abs(ch[c][i]));
                peakWin[pos] = pk;
                float winMax = 0;
                for (int k = 0; k < look; k++) if (peakWin[k] > winMax) winMax = peakWin[k];
                double want = winMax > ceiling ? ceiling / winMax : 1;
                gain = want < gain ? want : want - (want - gain) * release;   // instant attack (look-ahead hides it), smooth release
                for (int c = 0; c < channels; c++)
                {
                    float o = delay[c][pos];
                    delay[c][pos] = ch[c][i];
                    ch[c][i] = filled >= look ? (float)(o * gain) : 0;
                }
                pos = (pos + 1) % look;
                if (filled < look) filled++;
            }
        }
    }

    public static class Loudness
    {
        public static readonly double[] Targets = { double.NaN, -14, -16, -19, -23 };
        public static readonly string[] TargetNames = { "Off", "−14 LUFS — YouTube, Instagram, TikTok, Spotify", "−16 LUFS — podcasts (Apple, Google)", "−19 LUFS — quieter speech", "−23 LUFS — TV / radio (EBU R128)" };

        public static double Measure(short[] mono, int rate)
        {
            var m = new LoudnessMeter(rate, 1);
            for (int i = 0; i < mono.Length; i += 65536) m.AddMono(mono, i, Math.Min(65536, mono.Length - i));
            return m.Integrated();
        }

        public static double GainFor(double measured, double target)
        {
            if (double.IsNaN(measured) || double.IsNaN(target)) return 1;
            return Math.Pow(10, Math.Max(-30, Math.Min(30, target - measured)) / 20);   // at most ±30 dB
        }

        // Normalizes a mono clip to the target and limits peaks to -1 dBFS.
        public static short[] NormalizeMono(short[] s, int rate, double target)
        {
            if (double.IsNaN(target) || s.Length == 0) return s;
            double g = GainFor(Measure(s, rate), target);
            var o = Apply(s, rate, g);
            // the limiter can pull loud speech a little under the target: correct once
            double after = Measure(o, rate);
            if (!double.IsNaN(after) && Math.Abs(after - target) > 0.3) o = Apply(s, rate, g * Math.Pow(10, Math.Min(6, target - after) / 20));
            return o;
        }

        static short[] Apply(short[] s, int rate, double g)
        {
            var lim = new Limiter(rate, 1, -1);
            int L = lim.Latency;
            var f = new float[s.Length + L];
            for (int i = 0; i < s.Length; i++) f[i] = (float)(s[i] * g);
            var ch = new[] { f };
            lim.Process(ch, f.Length);
            var o = new short[s.Length];
            for (int i = 0; i < o.Length; i++) { float v = f[i + L]; o[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v); }
            return o;
        }
    }

    // ================================================================== noise reduction
    // Spectral gating (like Audacity's Noise Reduction): learns the noise from the quietest parts
    // of the recording (or a given noise sample) and turns down the frequencies that are only noise.
    public static class NoiseReducer
    {
        const int N = 2048, Hop = 512;

        public static short[] Process(short[] x, int rate, double reductionDb, short[] noiseSample = null)
        {
            if (x.Length < N * 2) return x;
            int bins = N / 2 + 1;
            var win = new double[N];
            for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
            // ---- noise profile: mean magnitude per frequency of the quietest 15% of frames
            var src = noiseSample != null && noiseSample.Length >= N ? noiseSample : x;
            int frames = (src.Length - N) / Hop + 1;
            var energy = new double[frames];
            for (int f = 0; f < frames; f++) { double e = 0; for (int i = 0; i < N; i += 4) { double v = src[f * Hop + i]; e += v * v; } energy[f] = e; }
            var order = Enumerable.Range(0, frames).OrderBy(f => energy[f]).ToArray();
            int take = noiseSample != null ? frames : Math.Max(4, frames * 15 / 100);
            var noise = new double[bins];
            var re = new double[N]; var im = new double[N];
            for (int k = 0; k < take; k++)
            {
                int f = order[k];
                for (int i = 0; i < N; i++) { re[i] = src[f * Hop + i] * win[i]; im[i] = 0; }
                Fft(re, im);
                for (int b = 0; b < bins; b++) noise[b] += Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
            }
            for (int b = 0; b < bins; b++) noise[b] /= take;

            // ---- gate every frame
            double floor = Math.Pow(10, -Math.Abs(reductionDb) / 20);
            var outBuf = new double[x.Length + N];
            var norm = new double[x.Length + N];
            var prev = new double[bins];
            for (int b = 0; b < bins; b++) prev[b] = 1;
            var g = new double[bins]; var gs = new double[bins];
            int total = (x.Length - 1) / Hop + 1;
            for (int f = 0; f < total; f++)
            {
                int p0 = f * Hop;
                for (int i = 0; i < N; i++) { int j = p0 + i; re[i] = (j < x.Length ? x[j] : 0) * win[i]; im[i] = 0; }
                Fft(re, im);
                for (int b = 0; b < bins; b++)
                {
                    double mag = Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                    double t = noise[b] * 2.0;                       // ~6 dB above the noise counts as signal
                    double v = mag >= t ? 1 : floor;
                    g[b] = Math.Max(v, prev[b] * 0.5);               // short hold: fewer "musical noise" chirps
                }
                for (int b = 0; b < bins; b++)
                {
                    double s = 0; int c = 0;
                    for (int k = Math.Max(0, b - 2); k <= Math.Min(bins - 1, b + 2); k++) { s += g[k]; c++; }
                    gs[b] = s / c; prev[b] = g[b];
                }
                for (int b = 0; b < bins; b++)
                {
                    re[b] *= gs[b]; im[b] *= gs[b];
                    if (b > 0 && b < N / 2) { re[N - b] = re[b]; im[N - b] = -im[b]; }
                }
                Ifft(re, im);
                for (int i = 0; i < N; i++) { outBuf[p0 + i] += re[i] * win[i]; norm[p0 + i] += win[i] * win[i]; }
            }
            var o = new short[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double v = norm[i] > 1e-6 ? outBuf[i] / norm[i] : 0;
                o[i] = (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
            }
            return o;
        }

        // in-place radix-2 FFT
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                        double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }

        public static void Ifft(double[] re, double[] im)
        {
            for (int i = 0; i < im.Length; i++) im[i] = -im[i];
            Fft(re, im);
            int n = re.Length;
            for (int i = 0; i < n; i++) { re[i] /= n; im[i] = -im[i] / n; }
        }
    }
}
