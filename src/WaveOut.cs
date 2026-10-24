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

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS
        {
            public short wMid, wPid; public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public int dwFormats; public short wChannels, wReserved1; public int dwSupport;
        }

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")] static extern int waveOutGetDevCaps(IntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, IntPtr id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

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

        static IntPtr Id(int id)
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
                    int r = waveOutOpen(out h, Id(id), ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
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
}
