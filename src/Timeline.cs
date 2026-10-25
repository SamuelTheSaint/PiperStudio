using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PiperStudio
{
    // Custom-drawn multitrack timeline: ruler, track headers, clips with waveforms, playhead.
    public class TimelineView : Control
    {
        public Project P = new Project();
        public Clip SelClip;
        public Track SelTrack;
        public long Playhead;
        public bool Playing;
        public long RecStart = -1, RecLength;   // live recording preview
        public Track RecTrack;

        public double Pps = 80;        // pixels per second
        public long ScrollX;           // first visible sample
        public int ScrollY;

        public const int HeaderW = 190, RulerH = 30, TrackH = 100;

        public event Action BeforeChange, Changed, SelectionChanged;
        public event Action<long> Seek;
        public event Action<Clip, Track> ClipDoubleClick;
        public event Action<Clip, Track, Point> ClipMenu;
        public event Action<Track, Point> TrackMenu;
        public event Action<Track> DeleteTrack;
        public event Action AddTrackClicked;
        public event Action<string[], Track, long> FilesDropped;   // track null = new track(s)
        public event Action<string> HoverHint;

        readonly HScrollBar hbar = new HScrollBar { Dock = DockStyle.Bottom };
        readonly VScrollBar vbar = new VScrollBar { Dock = DockStyle.Right };
        readonly Dictionary<short[], short[]> peaks = new Dictionary<short[], short[]>();
        const int PeakBlock = 256;

        enum Drag { None, Move, TrimL, TrimR, Volume, Pan, Scrub }
        Drag drag;
        string lastHint;
        Point dragFrom;
        long origStart, origIn, origOut;
        bool dragChanged;
        Track dragTrack;

        static readonly Font fSmall = new Font("Segoe UI", 8f);
        static readonly Font fName = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font fClip = new Font("Tahoma", 8.5f);

        public TimelineView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AllowDrop = true;
            BackColor = Color.FromArgb(38, 40, 46);
            Controls.Add(hbar); Controls.Add(vbar);
            hbar.Scroll += (s, e) => { ScrollX = (long)hbar.Value * P.Rate / 100; Invalidate(); };
            vbar.Scroll += (s, e) => { ScrollY = vbar.Value; Invalidate(); };
            TabStop = true;
        }

        public int Rate { get { return P.Rate; } }
        double SamplesPerPx { get { return Rate / Pps; } }
        int X(long sample) { return HeaderW + (int)((sample - ScrollX) / SamplesPerPx); }
        long S(int x) { return Math.Max(0, ScrollX + (long)((x - HeaderW) * SamplesPerPx)); }
        int TrackY(int i) { return RulerH + i * TrackH - ScrollY; }
        int ViewW { get { return Width - vbar.Width; } }
        int ViewH { get { return Height - hbar.Height; } }

        int TrackAt(int y)
        {
            if (y < RulerH) return -1;
            int i = (y - RulerH + ScrollY) / TrackH;
            return i >= 0 && i < P.Tracks.Count ? i : -1;
        }

        public void UpdateScroll()
        {
            long len = Math.Max(P.Length, Playhead) + Rate * 10L;
            int maxSec = (int)Math.Min(int.MaxValue / 200, len / Rate);
            int visSec = Math.Max(1, (int)((ViewW - HeaderW) / Pps));
            hbar.Maximum = Math.Max(0, maxSec * 100 + visSec * 100);
            hbar.LargeChange = Math.Max(1, visSec * 100);
            hbar.SmallChange = Math.Max(1, visSec * 10);
            hbar.Value = (int)Math.Max(0, Math.Min(hbar.Maximum, ScrollX * 100 / Rate));
            int contentH = P.Tracks.Count * TrackH + RulerH + 140;
            vbar.Maximum = Math.Max(0, contentH);
            vbar.LargeChange = Math.Max(1, ViewH);
            vbar.Enabled = contentH > ViewH;
            if (!vbar.Enabled) { ScrollY = 0; vbar.Value = 0; }
            else vbar.Value = Math.Min(ScrollY, Math.Max(0, vbar.Maximum - vbar.LargeChange + 1));
        }

        public void Zoom(double factor, int aroundX)
        {
            long anchor = S(aroundX);
            Pps = Math.Max(2, Math.Min(4000, Pps * factor));
            ScrollX = Math.Max(0, anchor - (long)((aroundX - HeaderW) * SamplesPerPx));
            UpdateScroll(); Invalidate();
        }

        public void ZoomToFit()
        {
            long len = Math.Max(P.Length, Rate * 5L);
            Pps = Math.Max(2, Math.Min(4000, (ViewW - HeaderW - 30) / (len / (double)Rate)));
            ScrollX = 0; UpdateScroll(); Invalidate();
        }

        public void EnsureVisible(long sample)
        {
            int x = X(sample);
            if (x < HeaderW || x > ViewW - 20)
            {
                ScrollX = Math.Max(0, sample - (long)((ViewW - HeaderW) * 0.15 * SamplesPerPx));
                UpdateScroll();
            }
        }

        public void ForgetPeaks() { peaks.Clear(); }

        short[] Peaks(short[] d)
        {
            short[] p;
            if (peaks.TryGetValue(d, out p)) return p;
            int n = (d.Length + PeakBlock - 1) / PeakBlock;
            p = new short[n];
            for (int b = 0; b < n; b++)
            {
                int m = 0, end = Math.Min(d.Length, (b + 1) * PeakBlock);
                for (int i = b * PeakBlock; i < end; i++) { int v = d[i] < 0 ? -d[i] : d[i]; if (v > m) m = v; }
                p[b] = (short)Math.Min(32767, m);
            }
            peaks[d] = p;
            return p;
        }

        static Color KindColor(Clip c)
        {
            switch (c.Kind)
            {
                case "speech": return Color.FromArgb(70, 120, 200);
                case "recording": return Color.FromArgb(190, 80, 80);
                default: return Color.FromArgb(70, 160, 110);
            }
        }

        public static string Time(long samples, int rate)
        {
            double s = samples / (double)rate;
            int m = (int)(s / 60);
            return string.Format("{0:00}:{1:00.00}", m, s - m * 60);
        }

        // header hit areas
        Rectangle MuteRect(int y) { return new Rectangle(10, y + 30, 24, 20); }
        Rectangle SoloRect(int y) { return new Rectangle(37, y + 30, 24, 20); }
        Rectangle DuckRect(int y) { return new Rectangle(64, y + 30, 24, 20); }
        Rectangle VolRect(int y) { return new Rectangle(10, y + 58, HeaderW - 24, 10); }
        Rectangle PanRect(int y) { return new Rectangle(22, y + 78, HeaderW - 48, 7); }
        Rectangle CloseRect(int y) { return new Rectangle(HeaderW - 26, y + 6, 18, 18); }
        Rectangle AddRowRect() { int y = TrackY(P.Tracks.Count); return new Rectangle(0, y, HeaderW, 34); }
        int dropY = -1;   // drag-and-drop highlight
        List<long[]> duckRangesPaint;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ViewW, h = ViewH;
            g.SetClip(new Rectangle(0, 0, w, h));

            duckRangesPaint = P.Tracks.Any(k => k.Duck) ? P.DuckRanges() : null;
            // tracks
            for (int i = 0; i < P.Tracks.Count; i++)
            {
                var t = P.Tracks[i];
                int y = TrackY(i);
                if (y > h || y + TrackH < RulerH) continue;
                using (var b = new SolidBrush(t == SelTrack ? Color.FromArgb(52, 56, 66) : Color.FromArgb(44, 46, 54)))
                    g.FillRectangle(b, HeaderW, y, w - HeaderW, TrackH);
                g.DrawLine(Pens.Black, 0, y + TrackH - 1, w, y + TrackH - 1);

                // clips
                g.SetClip(new Rectangle(HeaderW, Math.Max(RulerH, y), w - HeaderW, TrackH));
                foreach (var c in t.Clips) DrawClip(g, c, t, y, w);
                if (RecStart >= 0 && RecTrack == t)
                {
                    int x1 = X(RecStart), x2 = X(RecStart + RecLength);
                    using (var b = new SolidBrush(Color.FromArgb(150, 200, 60, 60))) g.FillRectangle(b, x1, y + 4, Math.Max(2, x2 - x1), TrackH - 9);
                    g.DrawString("● REC", fName, Brushes.White, x1 + 4, y + 6);
                }
                g.SetClip(new Rectangle(0, 0, w, h));

                // header
                using (var b = new SolidBrush(t == SelTrack ? Color.FromArgb(70, 76, 92) : Color.FromArgb(58, 61, 70)))
                    g.FillRectangle(b, 0, y, HeaderW, TrackH - 1);
                g.DrawString(t.Name, fName, Brushes.White, new RectangleF(8, y + 8, HeaderW - 40, 20), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
                var cr = CloseRect(y);
                g.DrawString("✕", fName, Brushes.Silver, cr.X + 2, cr.Y + 1);
                DrawToggle(g, MuteRect(y), "M", t.Mute, Color.FromArgb(200, 140, 40));
                DrawToggle(g, SoloRect(y), "S", t.Solo, Color.FromArgb(60, 170, 200));
                DrawToggle(g, DuckRect(y), "D", t.Duck, Color.FromArgb(170, 120, 220));
                g.DrawString((int)Math.Round(t.Volume * 100) + "%", fSmall, Brushes.Gainsboro, 94, y + 33);
                var vr = VolRect(y);
                g.FillRectangle(Brushes.DimGray, vr);
                using (var b = new SolidBrush(Color.FromArgb(120, 200, 120)))
                    g.FillRectangle(b, vr.X, vr.Y, (int)(vr.Width * Math.Min(1, t.Volume / 2f)), vr.Height);
                g.DrawLine(Pens.White, vr.X + vr.Width / 2, vr.Y - 2, vr.X + vr.Width / 2, vr.Bottom + 1);
                var pr = PanRect(y);
                g.DrawString("L", fSmall, Brushes.Silver, 9, pr.Y - 4);
                g.DrawString("R", fSmall, Brushes.Silver, pr.Right + 3, pr.Y - 4);
                g.FillRectangle(Brushes.DimGray, pr);
                int pc = pr.X + pr.Width / 2, px0 = pc + (int)(t.Pan * pr.Width / 2);
                using (var b = new SolidBrush(Color.FromArgb(120, 170, 230)))
                    g.FillRectangle(b, Math.Min(pc, px0), pr.Y, Math.Max(2, Math.Abs(px0 - pc)), pr.Height);
                g.DrawLine(Pens.White, pc, pr.Y - 2, pc, pr.Bottom + 1);
                g.DrawLine(Pens.Black, HeaderW - 1, y, HeaderW - 1, y + TrackH);
            }

            // "+ Add track" row
            var ar = AddRowRect();
            if (ar.Y < h)
            {
                using (var b = new SolidBrush(Color.FromArgb(50, 53, 61))) g.FillRectangle(b, ar);
                g.DrawString("＋ " + Lang.T("Add track"), fName, Brushes.LightGray, ar.X + 10, ar.Y + 9);
            }
            if (P.Tracks.Count == 0 || P.Tracks.All(t => t.Clips.Count == 0))
            {
                string hint = Lang.T("Drag audio files here from Windows, add speech below, import music or record your voice.\nEvery file, speech or recording can go on its own track — add as many tracks as you like.");
                g.DrawString(hint, Font, Brushes.Gray, new RectangleF(HeaderW + 20, TrackY(P.Tracks.Count) + 44, Math.Max(100, w - HeaderW - 40), 60));
            }
            if (dropY >= 0)
            {
                int ti = TrackAt(dropY);
                var r = ti >= 0 ? new Rectangle(HeaderW, TrackY(ti), w - HeaderW, TrackH) : new Rectangle(HeaderW, TrackY(P.Tracks.Count), w - HeaderW, TrackH);
                using (var pen = new Pen(Color.Gold, 3)) g.DrawRectangle(pen, r);
                g.DrawString(ti >= 0 ? Lang.T("Drop to add to this track") : Lang.T("Drop to add as new track(s)"), fName, Brushes.Gold, r.X + 10, r.Y + 10);
            }

            // ruler
            g.FillRectangle(new SolidBrush(Color.FromArgb(28, 30, 34)), 0, 0, w, RulerH);
            double[] steps = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };
            double step = steps.FirstOrDefault(s => s * Pps >= 70); if (step == 0) step = 600;
            double t0 = Math.Floor(ScrollX / (double)Rate / step) * step;
            for (double ts = t0; ; ts += step)
            {
                int x = X((long)(ts * Rate));
                if (x > w) break;
                if (x < HeaderW) continue;
                g.DrawLine(Pens.Gray, x, RulerH - 10, x, RulerH);
                g.DrawString(Time((long)(ts * Rate), Rate), fSmall, Brushes.Silver, x + 2, 4);
                int xm = X((long)((ts + step / 2) * Rate));
                if (xm < w) g.DrawLine(Pens.DimGray, xm, RulerH - 5, xm, RulerH);
            }
            g.FillRectangle(new SolidBrush(Color.FromArgb(28, 30, 34)), 0, 0, HeaderW, RulerH);
            g.DrawString(Time(Playhead, Rate), fName, Brushes.White, 8, 7);

            // playhead
            int px = X(Playhead);
            if (px >= HeaderW && px <= w)
            {
                using (var pen = new Pen(Color.FromArgb(255, 90, 90), 2)) g.DrawLine(pen, px, 0, px, h);
                g.FillPolygon(Brushes.Tomato, new[] { new Point(px - 6, 0), new Point(px + 6, 0), new Point(px, 9) });
            }
            if (Focused) { }
        }

        static void DrawToggle(Graphics g, Rectangle r, string s, bool on, Color c)
        {
            using (var b = new SolidBrush(on ? c : Color.FromArgb(80, 84, 96))) g.FillRectangle(b, r);
            g.DrawRectangle(Pens.Black, r);
            var sz = g.MeasureString(s, fName);
            g.DrawString(s, fName, on ? Brushes.Black : Brushes.Gainsboro, r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
        }

        void DrawClip(Graphics g, Clip c, Track t, int y, int w)
        {
            int x1 = X(c.Start), x2 = X(c.End);
            if (x2 < HeaderW || x1 > w) return;
            var r = new Rectangle(x1, y + 4, Math.Max(3, x2 - x1), TrackH - 9);
            var col = KindColor(c);
            bool sel = c == SelClip;
            using (var b = new SolidBrush(Color.FromArgb(sel ? 120 : 80, col))) g.FillRectangle(b, r);
            // waveform
            var pk = Peaks(c.Data);
            var pkR = c.DataR != null ? Peaks(c.DataR) : null;
            bool ducked = t.Duck && duckRangesPaint != null && duckRangesPaint.Count > 0;
            double duckF = Project.DuckFactor(t.DuckDb);
            int mid = r.Y + 16 + (r.Height - 16) / 2, half = (r.Height - 18) / 2;
            using (var pen = new Pen(Color.FromArgb(230, Color.White)))
            {
                int xa = Math.Max(x1, HeaderW), xb = Math.Min(x2, w);
                double spp = SamplesPerPx;
                long len = c.Length, fi = (long)(c.FadeIn * Rate), fo = (long)(c.FadeOut * Rate);
                for (int x = xa; x < xb; x++)
                {
                    long s0 = (long)((x - x1) * spp), s1 = (long)((x - x1 + 1) * spp);
                    if (s1 <= s0) s1 = s0 + 1;
                    long a = c.In + s0, bnd = Math.Min(c.Out, c.In + s1);
                    int m = 0;
                    if (spp >= PeakBlock)
                    {
                        for (long k = a / PeakBlock; k <= (bnd - 1) / PeakBlock && k < pk.Length; k++) { if (pk[k] > m) m = pk[k]; if (pkR != null && pkR[k] > m) m = pkR[k]; }
                    }
                    else
                    {
                        for (long k = a; k < bnd && k < c.Data.Length; k++)
                        {
                            int v = c.Data[k] < 0 ? -c.Data[k] : c.Data[k]; if (v > m) m = v;
                            if (c.DataR != null) { int vr2 = c.DataR[k] < 0 ? -c.DataR[k] : c.DataR[k]; if (vr2 > m) m = vr2; }
                        }
                    }
                    double gain = c.Gain * (t.Mute ? 0.15 : t.Volume);
                    if (ducked) gain *= 1 - (1 - duckF) * P.DuckActivity(duckRangesPaint, c.Start + s0);
                    if (fi > 0 && s0 < fi) gain *= s0 / (double)fi;
                    if (fo > 0 && s0 > len - fo) gain *= Math.Max(0, len - s0) / (double)fo;
                    int hh = (int)Math.Min(half, m / 32768.0 * half * gain);
                    if (hh > 0) g.DrawLine(pen, x, mid - hh, x, mid + hh); else g.DrawLine(pen, x, mid, x + 1, mid);
                }
            }
            // fades
            if (c.FadeIn > 0) using (var pen = new Pen(Color.Yellow)) g.DrawLine(pen, x1, r.Bottom, X(c.Start + (long)(c.FadeIn * Rate)), r.Y + 14);
            if (c.FadeOut > 0) using (var pen = new Pen(Color.Yellow)) g.DrawLine(pen, X(c.End - (long)(c.FadeOut * Rate)), r.Y + 14, x2, r.Bottom);
            // title bar
            using (var b = new SolidBrush(Color.FromArgb(sel ? 255 : 200, col))) g.FillRectangle(b, r.X, r.Y, r.Width, 15);
            string title = c.Name;
            if (!string.IsNullOrEmpty(c.Text) && c.Text != c.Name) title = c.Text;
            if (Math.Abs(c.Gain - 1) > 0.01) title = (int)Math.Round(c.Gain * 100) + "% · " + title;
            var tr = new RectangleF(Math.Max(r.X, HeaderW) + 3, r.Y + 1, Math.Max(0, r.Right - Math.Max(r.X, HeaderW) - 4), 14);
            g.DrawString(title, fClip, Brushes.White, tr, new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
            using (var pen = new Pen(sel ? Color.White : Color.FromArgb(160, col), sel ? 2 : 1)) g.DrawRectangle(pen, r);
        }

        // ---------------------------------------------------------------- mouse
        Clip HitClip(Point pt, out Track track, out int edge)
        {
            track = null; edge = 0;
            int ti = TrackAt(pt.Y);
            if (ti < 0 || pt.X < HeaderW) return null;
            track = P.Tracks[ti];
            foreach (var c in Enumerable.Reverse(track.Clips))
            {
                int x1 = X(c.Start), x2 = X(c.End);
                if (pt.X >= x1 - 4 && pt.X <= x2 + 4)
                {
                    if (pt.X - x1 <= 6) edge = -1; else if (x2 - pt.X <= 6) edge = 1;
                    if (x2 - x1 < 18) edge = 0;
                    return c;
                }
            }
            return null;
        }

        void Fire(Action a) { if (a != null) a(); }

        void StartChange() { if (!dragChanged) { dragChanged = true; Fire(BeforeChange); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            dragFrom = e.Location; dragChanged = false; drag = Drag.None;
            if (e.Y < RulerH && e.X >= HeaderW)
            {
                drag = Drag.Scrub; SetPlayhead(S(e.X)); return;
            }
            int ti = TrackAt(e.Y);
            if (ti < 0 && AddRowRect().Contains(e.Location)) { if (AddTrackClicked != null) AddTrackClicked(); return; }
            if (e.X < HeaderW)
            {
                if (ti < 0) return;
                if (CloseRect(TrackY(ti)).Contains(e.Location) && e.Button == MouseButtons.Left) { if (DeleteTrack != null) DeleteTrack(P.Tracks[ti]); return; }
                var t = P.Tracks[ti]; int y = TrackY(ti);
                SelTrack = t; SelClip = null; Fire(SelectionChanged);
                if (e.Button == MouseButtons.Right) { if (TrackMenu != null) TrackMenu(t, e.Location); Invalidate(); return; }
                if (MuteRect(y).Contains(e.Location)) { Fire(BeforeChange); t.Mute = !t.Mute; Fire(Changed); }
                else if (SoloRect(y).Contains(e.Location)) { Fire(BeforeChange); t.Solo = !t.Solo; Fire(Changed); }
                else if (DuckRect(y).Contains(e.Location)) { Fire(BeforeChange); t.Duck = !t.Duck; Fire(Changed); }
                else if (Rectangle.Inflate(VolRect(y), 0, 5).Contains(e.Location)) { drag = Drag.Volume; dragTrack = t; SetVolume(e.X); }
                else if (Rectangle.Inflate(PanRect(y), 8, 5).Contains(e.Location))
                {
                    if (e.Clicks >= 2) { Fire(BeforeChange); t.Pan = 0; Fire(Changed); }
                    else { drag = Drag.Pan; dragTrack = t; SetPan(e.X); }
                }
                Invalidate(); return;
            }
            Track track; int edge;
            var c = HitClip(e.Location, out track, out edge);
            if (track != null) SelTrack = track;
            SelClip = c;
            Fire(SelectionChanged);
            if (c != null)
            {
                if (e.Button == MouseButtons.Right) { Invalidate(); if (ClipMenu != null) ClipMenu(c, track, e.Location); return; }
                drag = edge < 0 ? Drag.TrimL : edge > 0 ? Drag.TrimR : Drag.Move;
                dragTrack = track; origStart = c.Start; origIn = c.In; origOut = c.Out;
            }
            else
            {
                if (e.Button == MouseButtons.Right && track != null && TrackMenu != null) { TrackMenu(track, e.Location); return; }
                SetPlayhead(S(e.X)); drag = Drag.Scrub;
            }
            Invalidate();
        }

        void SetPlayhead(long s) { Playhead = s; if (Seek != null) Seek(s); Invalidate(); }

        void SetPan(int x)
        {
            var pr = PanRect(0);
            float v = (float)Math.Max(-1, Math.Min(1, (x - (pr.X + pr.Width / 2.0)) / (pr.Width / 2.0)));
            if (Math.Abs(v) < 0.06) v = 0;
            StartChange();
            dragTrack.Pan = v; Invalidate();
            if (HoverHint != null) HoverHint(PanText(v));
        }

        static string PanText(float v)
        {
            if (Math.Abs(v) < 0.001) return Lang.T("Pan: centre");
            return Lang.T("Pan: ") + (v < 0 ? Lang.T("left") + " " : Lang.T("right") + " ") + (int)Math.Round(Math.Abs(v) * 100) + "%";
        }

        void SetVolume(int x)
        {
            var vr = VolRect(0);
            float v = (float)Math.Max(0, Math.Min(2, (x - vr.X) / (double)vr.Width * 2));
            if (Math.Abs(v - 1) < 0.05) v = 1;
            StartChange();
            dragTrack.Volume = v; Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag == Drag.None && e.X < HeaderW)
            {
                int hti = TrackAt(e.Y);
                string hint = null;
                if (hti >= 0)
                {
                    int hy = TrackY(hti); var ht = P.Tracks[hti];
                    if (MuteRect(hy).Contains(e.Location)) hint = Lang.T("M = mute this track");
                    else if (SoloRect(hy).Contains(e.Location)) hint = Lang.T("S = solo: play only this track (and other solo tracks)");
                    else if (DuckRect(hy).Contains(e.Location)) hint = Lang.T("D = duck: this track gets quieter automatically while other tracks play (great for background music). Right-click the track name to change how much.");
                    else if (Rectangle.Inflate(VolRect(hy), 0, 5).Contains(e.Location)) hint = Lang.T("Volume — drag. The white line is 100%.");
                    else if (Rectangle.Inflate(PanRect(hy), 8, 5).Contains(e.Location)) hint = PanText(ht.Pan) + " — " + Lang.T("drag left/right, double-click to centre");
                    else if (CloseRect(hy).Contains(e.Location)) hint = Lang.T("Delete this track");
                }
                if (hint != lastHint) { lastHint = hint; if (HoverHint != null && hint != null) HoverHint(hint); }
                Cursor = Cursors.Default;
                return;
            }
            if (drag == Drag.None)
            {
                Track t; int edge;
                var c = HitClip(e.Location, out t, out edge);
                Cursor = c != null && edge != 0 ? Cursors.SizeWE : (c != null ? Cursors.SizeAll : Cursors.Default);
                return;
            }
            long d = (long)((e.X - dragFrom.X) * SamplesPerPx);
            long minLen = Rate / 50;
            switch (drag)
            {
                case Drag.Scrub: SetPlayhead(S(e.X)); break;
                case Drag.Volume: SetVolume(e.X); break;
                case Drag.Pan: SetPan(e.X); break;
                case Drag.Move:
                    if (Math.Abs(e.X - dragFrom.X) < 3 && Math.Abs(e.Y - dragFrom.Y) < 3 && !dragChanged) return;
                    StartChange();
                    SelClip.Start = Math.Max(0, origStart + d);
                    int ti = TrackAt(e.Y);
                    if (ti < 0 && e.Y >= TrackY(P.Tracks.Count) && dragTrack.Clips.Count > 1)
                    {
                        // dragged below the last track: put the clip on a new track (a new "layer")
                        var nt = new Track { Name = Lang.T("Track") + " " + (P.Tracks.Count + 1) };
                        P.Tracks.Add(nt); ti = P.Tracks.Count - 1;
                        UpdateScroll();
                    }
                    if (ti >= 0 && P.Tracks[ti] != dragTrack)
                    {
                        dragTrack.Clips.Remove(SelClip); P.Tracks[ti].Clips.Add(SelClip);
                        dragTrack = P.Tracks[ti]; SelTrack = dragTrack;
                    }
                    break;
                case Drag.TrimL:
                    StartChange();
                    SelClip.In = Math.Max(0, Math.Min(origOut - minLen, origIn + d));
                    SelClip.Start = Math.Max(0, origStart + (SelClip.In - origIn));
                    break;
                case Drag.TrimR:
                    StartChange();
                    SelClip.Out = Math.Max(origIn + minLen, Math.Min(SelClip.Data.Length, origOut + d));
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragChanged) Fire(Changed);
            drag = Drag.None; dragChanged = false;
            UpdateScroll(); Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            Track t; int edge;
            var c = HitClip(e.Location, out t, out edge);
            if (c != null && ClipDoubleClick != null) ClipDoubleClick(c, t);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0) { Zoom(e.Delta > 0 ? 1.25 : 0.8, Math.Max(HeaderW, e.X)); return; }
            if ((ModifierKeys & Keys.Shift) != 0 || !vbar.Enabled)
            {
                ScrollX = Math.Max(0, ScrollX - (long)(e.Delta / 120.0 * 60 * SamplesPerPx));
            }
            else ScrollY = Math.Max(0, Math.Min(Math.Max(0, vbar.Maximum - vbar.LargeChange + 1), ScrollY - e.Delta / 2));
            UpdateScroll(); Invalidate();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            var pt = PointToClient(new Point(e.X, e.Y));
            if (pt.Y != dropY) { dropY = Math.Max(RulerH, pt.Y); Invalidate(); }
        }

        protected override void OnDragLeave(EventArgs e) { dropY = -1; Invalidate(); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            dropY = -1; Invalidate();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            var pt = PointToClient(new Point(e.X, e.Y));
            int ti = TrackAt(pt.Y);
            long pos = pt.X >= HeaderW ? S(pt.X) : 0;
            if (FilesDropped != null) FilesDropped(files, ti >= 0 ? P.Tracks[ti] : null, pos);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Space: return true; }
            return base.IsInputKey(keyData);
        }
    }
}
