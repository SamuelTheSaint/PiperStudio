using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PiperStudio
{
    // Records 16-bit mono from a Windows input device (microphone).
    public class Recorder
    {
        const int WHDR_DONE = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEINCAPS
        {
            public short wMid, wPid; public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public int dwFormats; public short wChannels, wReserved1;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] static extern int waveInGetDevCaps(IntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, IntPtr id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        public static List<string> Devices()
        {
            var l = new List<string>();
            try
            {
                int n = waveInGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var c = new WAVEINCAPS();
                    l.Add(waveInGetDevCaps(new IntPtr(i), ref c, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0 ? c.szPname : "Microphone " + i);
                }
            }
            catch { }
            return l;
        }

        static IntPtr Id(int id) { return id >= 0 ? new IntPtr(id) : (IntPtr.Size == 8 ? new IntPtr(0xFFFFFFFFL) : new IntPtr(-1)); }

        IntPtr h;
        readonly List<IntPtr> hdrs = new List<IntPtr>();
        readonly List<IntPtr> datas = new List<IntPtr>();
        MemoryStream captured;
        Thread thread;
        volatile bool running;
        int hdrSize;
        public volatile float Level;   // 0..1 peak of the latest buffer, for a level meter
        public int Rate { get; private set; }
        public long StartTicks;   // Stopwatch timestamp when capture started

        public void Start(int deviceId, int rate)
        {
            Rate = rate;
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = rate, nAvgBytesPerSec = rate * 2, nBlockAlign = 2, wBitsPerSample = 16 };
            int r = waveInOpen(out h, Id(deviceId), ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new Exception("Couldn't open the microphone (error " + r + "). Check Windows privacy settings: Settings → Privacy → Microphone → allow desktop apps.");
            hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
            int bytes = rate / 10 * 2; // 100 ms buffers
            for (int i = 0; i < 8; i++)
            {
                var d = Marshal.AllocHGlobal(bytes);
                var hp = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(new WAVEHDR { lpData = d, dwBufferLength = bytes }, hp, false);
                waveInPrepareHeader(h, hp, hdrSize);
                waveInAddBuffer(h, hp, hdrSize);
                datas.Add(d); hdrs.Add(hp);
            }
            captured = new MemoryStream();
            running = true;
            waveInStart(h);
            StartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            thread = new Thread(Loop) { IsBackground = true, Name = "Recorder" };
            thread.Start();
        }

        void Loop()
        {
            int next = 0;
            while (running)
            {
                var hp = hdrs[next];
                var hdr = (WAVEHDR)Marshal.PtrToStructure(hp, typeof(WAVEHDR));
                if ((hdr.dwFlags & WHDR_DONE) == 0) { Thread.Sleep(10); continue; }
                Collect(hp, hdr);
                if (!running) break;
                waveInUnprepareHeader(h, hp, hdrSize);
                Marshal.StructureToPtr(new WAVEHDR { lpData = hdr.lpData, dwBufferLength = hdr.dwBufferLength }, hp, false);
                waveInPrepareHeader(h, hp, hdrSize);
                waveInAddBuffer(h, hp, hdrSize);
                next = (next + 1) % hdrs.Count;
            }
        }

        void Collect(IntPtr hp, WAVEHDR hdr)
        {
            if (hdr.dwBytesRecorded <= 0) return;
            var buf = new byte[hdr.dwBytesRecorded];
            Marshal.Copy(hdr.lpData, buf, 0, buf.Length);
            lock (captured) captured.Write(buf, 0, buf.Length);
            int peak = 0;
            for (int i = 0; i + 1 < buf.Length; i += 2) { int v = Math.Abs((int)BitConverter.ToInt16(buf, i)); if (v > peak) peak = v; }
            Level = peak / 32768f;
        }

        public short[] Stop()
        {
            running = false;
            if (thread != null) thread.Join(1000);
            try
            {
                waveInReset(h);
                // grab whatever the last partial buffers hold, in order
                foreach (var hp in hdrs) waveInUnprepareHeader(h, hp, hdrSize);
                waveInClose(h);
            }
            catch { }
            foreach (var p in hdrs) Marshal.FreeHGlobal(p);
            foreach (var p in datas) Marshal.FreeHGlobal(p);
            hdrs.Clear(); datas.Clear();
            byte[] b; lock (captured) b = captured.ToArray();
            var r = new short[b.Length / 2];
            Buffer.BlockCopy(b, 0, r, 0, r.Length * 2);
            return r;
        }
    }
}
