using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace PiperStudio
{
    // Plays 16-bit mono PCM on a chosen Windows output device (e.g. "CABLE Input" of a virtual audio cable).
    public static class WaveOut
    {
        const int WHDR_DONE = 1;
        internal const int WAVE_MAPPED = 4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS
        {
            public short wMid, wPid; public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public int dwFormats; public short wChannels, wReserved1; public int dwSupport;
        }

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")] static extern int waveOutGetDevCaps(IntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutOpen(out IntPtr hwo, IntPtr id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] internal static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] internal static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] internal static extern int waveOutClose(IntPtr hwo);

        public static bool Available
        {
            get { try { return Environment.OSVersion.Platform == PlatformID.Win32NT && waveOutGetNumDevs() >= 0; } catch { return false; } }
        }

        // Index in the list = device id. Id -1 = Windows default device.
        public static List<string> Devices()
        {
            var l = new List<string>();
            try
            {
                int n = waveOutGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var c = new WAVEOUTCAPS();
                    l.Add(waveOutGetDevCaps(new IntPtr(i), ref c, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0 ? c.szPname : "Device " + i);
                }
            }
            catch { }
            return l;
        }

        internal static IntPtr Id(int id)
        {
            if (id >= 0) return new IntPtr(id);
            return IntPtr.Size == 8 ? new IntPtr(0xFFFFFFFFL) : new IntPtr(-1); // WAVE_MAPPER
        }

        class Out { public IntPtr H, Hdr, Data; public int HdrSize; }

        // Plays on all given devices at once and returns when finished or cancelled.
        public static void PlaySync(short[] pcm, int rate, IList<int> deviceIds, CancellationToken ct)
        {
            if (pcm.Length == 0) return;
            var outs = new List<Out>();
            int bytes = pcm.Length * 2;
            try
            {
                foreach (int id in deviceIds)
                {
                    var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = rate, nAvgBytesPerSec = rate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0 };
                    IntPtr h;
                    int r = waveOutOpen(out h, Id(id), ref fmt, IntPtr.Zero, IntPtr.Zero, id >= 0 ? WAVE_MAPPED : 0);
                    if (r != 0) throw new Exception("Couldn't open the audio device (error " + r + "). It may be in use or unplugged.");
                    var o = new Out { H = h, HdrSize = Marshal.SizeOf(typeof(WAVEHDR)) };
                    outs.Add(o);
                    o.Data = Marshal.AllocHGlobal(bytes);
                    Marshal.Copy(pcm, 0, o.Data, pcm.Length);
                    var hdr = new WAVEHDR { lpData = o.Data, dwBufferLength = bytes };
                    o.Hdr = Marshal.AllocHGlobal(o.HdrSize);
                    Marshal.StructureToPtr(hdr, o.Hdr, false);
                    waveOutPrepareHeader(h, o.Hdr, o.HdrSize);
                }
                foreach (var o in outs) waveOutWrite(o.H, o.Hdr, o.HdrSize);
                while (true)
                {
                    bool allDone = true;
                    foreach (var o in outs)
                    {
                        var hdr = (WAVEHDR)Marshal.PtrToStructure(o.Hdr, typeof(WAVEHDR));
                        if ((hdr.dwFlags & WHDR_DONE) == 0) { allDone = false; break; }
                    }
                    if (allDone || ct.IsCancellationRequested) break;
                    Thread.Sleep(15);
                }
            }
            finally
            {
                foreach (var o in outs)
                {
                    try
                    {
                        waveOutReset(o.H);
                        if (o.Hdr != IntPtr.Zero) waveOutUnprepareHeader(o.H, o.Hdr, o.HdrSize);
                        waveOutClose(o.H);
                    }
                    catch { }
                    if (o.Hdr != IntPtr.Zero) Marshal.FreeHGlobal(o.Hdr);
                    if (o.Data != IntPtr.Zero) Marshal.FreeHGlobal(o.Data);
                }
            }
        }
    }

    // Streams audio to an output device in small buffers: playback starts at once, memory stays small,
    // and the position comes from the sound card itself (so the playhead matches what you hear).
    public class WaveStreamPlayer
    {
        [StructLayout(LayoutKind.Explicit)]
        struct MMTIME { [FieldOffset(0)] public int wType; [FieldOffset(4)] public int value; [FieldOffset(8)] public int pad; }
        [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr h, ref MMTIME t, int size);
        const int TIME_MS = 1, TIME_SAMPLES = 2, TIME_BYTES = 4, WHDR_DONE = 1;

        readonly int device, rate, channels;
        IntPtr h;
        long writtenFrames, lastPlayed;
        public long FirstWriteTicks;     // Stopwatch timestamp of the first buffer sent to the card
        public bool Started { get { return FirstWriteTicks != 0; } }

        public WaveStreamPlayer(int device, int rate, int channels) { this.device = device; this.rate = rate; this.channels = channels; }

        // Frames the listener has actually heard so far.
        public long PlayedFrames
        {
            get
            {
                var hh = h;
                if (hh == IntPtr.Zero) return lastPlayed;
                try
                {
                    var t = new MMTIME { wType = TIME_SAMPLES };
                    if (waveOutGetPosition(hh, ref t, 12) != 0) return lastPlayed;
                    long v = (uint)t.value;
                    if (t.wType == TIME_BYTES) v /= 2 * channels;
                    else if (t.wType == TIME_MS) v = v * rate / 1000;
                    v = Math.Min(v, writtenFrames);
                    if (v > lastPlayed) lastPlayed = v;
                    return lastPlayed;
                }
                catch { return lastPlayed; }
            }
        }

        // fill(frames) returns interleaved samples (frames*channels) or null/empty when finished. Blocks until done or cancelled.
        public void Run(Func<int, short[]> fill, System.Threading.CancellationToken ct, int bufferMs = 60, int bufferCount = 5)
        {
            var fmt = new WaveOut.WAVEFORMATEX { wFormatTag = 1, nChannels = (short)channels, nSamplesPerSec = rate, nAvgBytesPerSec = rate * 2 * channels, nBlockAlign = (short)(2 * channels), wBitsPerSample = 16 };
            int r = WaveOut.waveOutOpen(out h, WaveOut.Id(device), ref fmt, IntPtr.Zero, IntPtr.Zero, device >= 0 ? WaveOut.WAVE_MAPPED : 0);
            if (r != 0) { h = IntPtr.Zero; throw new Exception("Couldn't open the audio device (error " + r + "). It may be in use or unplugged."); }
            int frames = Math.Max(256, rate * bufferMs / 1000);
            int bytes = frames * 2 * channels;
            int hdrSize = Marshal.SizeOf(typeof(WaveOut.WAVEHDR));
            var hdrs = new IntPtr[bufferCount];
            var datas = new IntPtr[bufferCount];
            var used = new bool[bufferCount];
            try
            {
                for (int i = 0; i < bufferCount; i++) { datas[i] = Marshal.AllocHGlobal(bytes); hdrs[i] = Marshal.AllocHGlobal(hdrSize); }
                bool finished = false;
                while (!ct.IsCancellationRequested)
                {
                    bool anyBusy = false, wrote = false;
                    for (int i = 0; i < bufferCount && !ct.IsCancellationRequested; i++)
                    {
                        if (used[i])
                        {
                            var hd = (WaveOut.WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WaveOut.WAVEHDR));
                            if ((hd.dwFlags & WHDR_DONE) == 0) { anyBusy = true; continue; }
                            WaveOut.waveOutUnprepareHeader(h, hdrs[i], hdrSize);
                            used[i] = false;
                        }
                        if (finished) continue;
                        var chunk = fill(frames);
                        if (chunk == null || chunk.Length == 0) { finished = true; continue; }
                        int n = Math.Min(chunk.Length, frames * channels);
                        Marshal.Copy(chunk, 0, datas[i], n);
                        Marshal.StructureToPtr(new WaveOut.WAVEHDR { lpData = datas[i], dwBufferLength = n * 2 }, hdrs[i], false);
                        WaveOut.waveOutPrepareHeader(h, hdrs[i], hdrSize);
                        WaveOut.waveOutWrite(h, hdrs[i], hdrSize);
                        if (FirstWriteTicks == 0) FirstWriteTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        writtenFrames += n / channels;
                        used[i] = true; anyBusy = true; wrote = true;
                    }
                    if (finished && !anyBusy) break;
                    if (!wrote) System.Threading.Thread.Sleep(5);
                }
            }
            finally
            {
                if (!ct.IsCancellationRequested) lastPlayed = writtenFrames;   // finished: everything was heard
                try { WaveOut.waveOutReset(h); } catch { }
                for (int i = 0; i < bufferCount; i++)
                {
                    if (hdrs[i] != IntPtr.Zero) { try { if (used[i]) WaveOut.waveOutUnprepareHeader(h, hdrs[i], hdrSize); } catch { } Marshal.FreeHGlobal(hdrs[i]); }
                    if (datas[i] != IntPtr.Zero) Marshal.FreeHGlobal(datas[i]);
                }
                try { WaveOut.waveOutClose(h); } catch { }
                h = IntPtr.Zero;
            }
        }
    }
}
