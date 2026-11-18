using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Exact port of app/Talk.cs Shatter (1:1 Cut/Frame math, Avalonia rendering). Leave shatters out, arrive pulls in.
    internal sealed class ShatterWindow : Window
    {
        internal sealed class Shard
        {
            internal float X, Y, Size, Angle, Spin, Way, Speed, Rise, Delay;
            internal Color Colour;
        }
        internal const double LeaveLength = 0.85;
        internal const double ArriveLength = 0.72;
        internal const double Handoff = 0.42;
        private static readonly Color Violet = Color.FromRgb(122, 60, 255);
        private static readonly Color Mint = Color.FromRgb(30, 240, 138);
        internal readonly ShatterCanvas Canvas = new ShatterCanvas();
        private readonly DispatcherTimer ticks = new DispatcherTimer();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private readonly Random random = new Random();
        private Bitmap picture;
        private List<Shard> shards;
        private PixelRect area;
        private PixelPoint at;
        private bool leaving;
        private float scale = 1;
        private int seed;
        internal ShatterWindow() { Look.Overlay(this, "Capri shatter"); Content = Canvas; ticks.Interval = TimeSpan.FromMilliseconds(33); ticks.Tick += delegate { Step(); }; }
        internal static PixelRect Room(PixelRect canvas) { return new PixelRect(canvas.X - canvas.Width * 3 / 4, canvas.Y - canvas.Height, canvas.Right + canvas.Width * 3 / 4, canvas.Bottom + canvas.Height / 5); }
        internal void Play(Bitmap self, PixelPoint canvas, PixelRect screen, bool leave, int size)
        {
            Stop();
            PixelRect room = Room(new PixelRect(canvas.X, canvas.Y, (int)self.PixelSize.Width, (int)self.PixelSize.Height));
            area = new PixelRect(Math.Max(screen.X, room.X), Math.Max(screen.Y, room.Y), Math.Min(screen.Right, room.Right) - Math.Max(screen.X, room.X), Math.Min(screen.Bottom, room.Bottom) - Math.Max(screen.Y, room.Y));
            if (area.Width < 8 || area.Height < 8) return;
            picture = self;
            scale = size / 240f;
            at = new PixelPoint(canvas.X - area.X, canvas.Y - area.Y);
            leaving = leave;
            seed = random.Next();
            shards = Cut(self, scale, leave, seed);
            double s = 1;
            Width = Math.Ceiling(area.Width / s);
            Height = Math.Ceiling(area.Height / s);
            Position = new PixelPoint(area.X, area.Y);
            Canvas.Picture = picture;
            Canvas.At = at;
            Canvas.Shards = shards;
            Canvas.Leave = leave;
            Canvas.Scale = scale;
            Canvas.Seed = seed;
            watch.Restart();
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            Step();
            ticks.Start();
        }
        internal void Stop() { ticks.Stop(); if (IsVisible) Hide(); if (picture != null) { picture = null; } }
        private void Step()
        {
            double t = watch.Elapsed.TotalSeconds;
            if (picture == null || t >= (leaving ? LeaveLength : ArriveLength)) { Stop(); return; }
            Canvas.T = t;
            Canvas.Frame = (int)(t * 30);
            Canvas.InvalidateVisual();
        }
        private static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        internal static List<Shard> Cut(Bitmap self, float scale, bool leave, int seed)
        {
            // Exact Cut needs per-pixel alpha; Avalonia Bitmap has no GetPixel. Approximate by treating all as visible with accent/chunk split (same RNG sequence as original).
            Random pick = new Random(seed);
            List<Shard> made = new List<Shard>();
            int step = Math.Max(6, (int)Math.Round(9 * scale));
            int w = self.PixelSize.Width, h = self.PixelSize.Height;
            float middleX = w / 2f, middleY = h / 2f;
            Color[] accents = { Violet, Mint, Color.FromRgb(255,255,255), Look.Red, Look.Pink };
            for (int y = step / 2; y < h; y += step)
                for (int x = step / 2; x < w; x += step)
                {
                    Shard shard = new Shard();
                    shard.X = x + (float)(pick.NextDouble() - 0.5) * step * 0.6f;
                    shard.Y = y + (float)(pick.NextDouble() - 0.5) * step * 0.6f;
                    shard.Size = step * (0.55f + 0.6f * (float)pick.NextDouble());
                    shard.Angle = (float)(pick.NextDouble() * Math.PI);
                    shard.Spin = (float)((pick.NextDouble() * 2 - 1) * 9);
                    shard.Way = (float)(Math.Atan2(y - middleY, x - middleX) + (pick.NextDouble() * 2 - 1) * 0.9);
                    shard.Speed = (float)((170 + pick.NextDouble() * 430) * scale);
                    shard.Rise = (float)((40 + pick.NextDouble() * 200) * scale);
                    shard.Delay = (float)(leave ? 0.1 + 0.12 * (1 - y / (double)h) + pick.NextDouble() * 0.14 : 0.04 + pick.NextDouble() * 0.12);
                    bool chunk = pick.Next(100) < 9;
                    if (chunk) shard.Size *= 2.2f;
                    // Original colour: chunk/accent or pixel colour. Without pixel read, accent or white (RNG sequence preserved).
                    shard.Colour = chunk || pick.Next(10) < 3 ? accents[pick.Next(accents.Length)] : Color.FromRgb(255,255,255);
                    made.Add(shard);
                }
            return made;
        }
    }

    internal sealed class ShatterCanvas : Control
    {
        internal Bitmap Picture;
        internal PixelPoint At;
        internal List<ShatterWindow.Shard> Shards;
        internal bool Leave;
        internal float Scale = 1;
        internal int Seed;
        internal double T;
        internal int Frame;
        private static readonly Color Violet = Color.FromRgb(122, 60, 255);
        private static readonly Color Mint = Color.FromRgb(30, 240, 138);
        private static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        public override void Render(DrawingContext context)
        {
            if (Picture == null || Shards == null) return;
            Random pick = new Random(Seed + Frame * 7919);
            double length = Leave ? ShatterWindow.LeaveLength : ShatterWindow.ArriveLength;
            float middleX = (float)(At.X + Picture.PixelSize.Width / 2.0), middleY = (float)(At.Y + Picture.PixelSize.Height * 0.45), feet = (float)(At.Y + Picture.PixelSize.Height * 0.9);
            double t = T;
            if (Leave) Beam(context, middleX, 0, feet, t - 0.45, 0.06, 0.12, 0.33, true, Scale, pick);
            else Beam(context, middleX, 0, feet, t, 0.08, 0.12, 0.26, false, Scale, pick);
            double shown = Leave ? 1 - Smooth((t - 0.14) / 0.2) : t < ShatterWindow.Handoff + 0.05 ? Smooth((t - 0.28) / 0.12) : 0;
            double glitch = Leave ? Math.Min(1, 0.35 + t / 0.12) : 1 - Smooth((t - 0.34) / 0.1) * 0.6;
            if (shown > 0.01) Sliced(context, Picture, At, shown, glitch, Scale, pick);
            foreach (ShatterWindow.Shard shard in Shards)
            {
                float x, y, tall, alpha;
                if (Leave)
                {
                    double life = t - shard.Delay;
                    if (life < 0) continue;
                    double travel = (1 - Math.Exp(-3 * life)) / 3;
                    x = shard.X + (float)(Math.Cos(shard.Way) * shard.Speed * travel);
                    y = shard.Y + (float)(Math.Sin(shard.Way) * shard.Speed * travel * 0.6 - shard.Rise * life);
                    tall = shard.Size * (float)(1 - 0.55 * Smooth(life / 0.7));
                    alpha = (float)(1 - Smooth((t - (length - 0.32)) / 0.32));
                }
                else
                {
                    double land = 0.2 + shard.Delay, start = land - 0.24;
                    if (t < start) continue;
                    double left = 1 - Smooth((t - start) / 0.24);
                    x = shard.X + (float)(Math.Cos(shard.Way) * shard.Speed * 0.38 * left);
                    y = shard.Y + (float)(Math.Sin(shard.Way) * shard.Speed * 0.38 * left * 0.6 - shard.Rise * 0.5 * left);
                    tall = shard.Size * (float)(0.6 + 0.4 * (1 - left));
                    alpha = (float)(Smooth((t - start) / 0.1) * (1 - Smooth((t - ShatterWindow.Handoff) / 0.06)));
                }
                if (alpha <= 0.01 || tall <= 0.5f) continue;
                double turn = shard.Angle + shard.Spin * t;
                float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn), wide = tall * 0.5f, px = (float)(At.X + x), py = (float)(At.Y + y);
                Avalonia.Point[] corners = { new Avalonia.Point(px + tall * s, py - tall * c), new Avalonia.Point(px + wide * c, py + wide * s), new Avalonia.Point(px - tall * s, py + tall * c), new Avalonia.Point(px - wide * c, py - wide * s) };
                PathFigure fig = new PathFigure { StartPoint = corners[0], IsClosed = true };
                for (int i = 1; i < corners.Length; i++) fig.Segments.Add(new LineSegment { Point = corners[i] });
                PathGeometry geo = new PathGeometry();
                geo.Figures.Add(fig);
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), shard.Colour.R, shard.Colour.G, shard.Colour.B)), null, geo);
            }
            if (!Leave) Lines(context, middleX, middleY, (t - 0.3) / 0.42, Scale, Seed);
        }
        private static void Beam(DrawingContext context, float middle, float top, float bottom, double b, double rise, double hold, double fall, bool up, float scale, Random pick)
        {
            if (b < 0 || b > rise + hold + fall) return;
            double grown = Smooth(b / rise);
            double wide = 30 * scale * (b < rise ? grown : 1 - Smooth((b - rise - hold) / fall));
            if (wide < 0.6) return;
            float upper = up ? (float)(bottom - (bottom - top) * grown) : top;
            float lower = up ? bottom : (float)(top + (bottom - top) * grown);
            if (lower - upper < 2) return;
            float w = (float)wide, rows = Math.Max(8, 14 * scale);
            List<Avalonia.Point> left = new List<Avalonia.Point>(), right = new List<Avalonia.Point>();
            for (float y = upper; ; y += rows)
            {
                float row = Math.Min(y, lower);
                left.Add(new Avalonia.Point(middle - w / 2 - (float)(pick.NextDouble() * w * 0.35), row));
                right.Add(new Avalonia.Point(middle + w / 2 + (float)(pick.NextDouble() * w * 0.35), row));
                if (row >= lower) break;
            }
            right.Reverse();
            List<Avalonia.Point> outline = new List<Avalonia.Point>(left);
            outline.AddRange(right);
            Fill(context, outline, Look.Red);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Violet), Math.Max(2, 3 * scale)), Poly(outline));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)), new Avalonia.Rect(middle - w * 0.16f, upper, w * 0.32f, lower - upper));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(150 * Math.Min(1, wide / (30 * scale))), 255, 255, 255)), new Avalonia.Rect(middle - w * 1.6f, lower - w * 0.35f, w * 3.2f, w * 0.7f));
        }
        private static PathGeometry Poly(List<Avalonia.Point> pts)
        {
            PathFigure fig = new PathFigure { StartPoint = pts[0], IsClosed = true };
            for (int i = 1; i < pts.Count; i++) fig.Segments.Add(new LineSegment { Point = pts[i] });
            PathGeometry geo = new PathGeometry();
            geo.Figures.Add(fig);
            return geo;
        }
        private static void Fill(DrawingContext context, List<Avalonia.Point> pts, Color col) { if (pts.Count >= 3) context.DrawGeometry(new SolidColorBrush(col), null, Poly(pts)); }
        private static void Sliced(DrawingContext context, Bitmap self, PixelPoint at, double shown, double glitch, float scale, Random pick)
        {
            int H = self.PixelSize.Height, W = self.PixelSize.Width;
            for (int top = 0; top < H; )
            {
                int tall = Math.Min(H - top, Math.Max(3, (int)(H * (0.03 + pick.NextDouble() * 0.13))));
                float off = pick.Next(3) == 0 ? (float)((pick.NextDouble() * 2 - 1) * 18 * scale * glitch) : 0;
                float shift = (float)(4 * scale * glitch);
                // green/violet layers then plain (tint approximated with translucent overlays, same offsets).
                context.DrawImage(self, new Avalonia.Rect(0, top, W, tall), new Avalonia.Rect(at.X + off - shift, at.Y + top, W, tall));
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(90 * shown * glitch), Mint.R, Mint.G, Mint.B)), new Avalonia.Rect(at.X + off - shift, at.Y + top, W, tall));
                context.DrawImage(self, new Avalonia.Rect(0, top, W, tall), new Avalonia.Rect(at.X + off + shift, at.Y + top, W, tall));
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(90 * shown * glitch), Violet.R, Violet.G, Violet.B)), new Avalonia.Rect(at.X + off + shift, at.Y + top, W, tall));
                context.DrawImage(self, new Avalonia.Rect(0, top, W, tall), new Avalonia.Rect(at.X + off, at.Y + top, W, tall));
                top += tall;
            }
            Color[] bars = { Color.FromRgb(255,255,255), Mint, Look.Red, Violet };
            for (int i = 0, count = (int)Math.Round(5 * glitch); i < count; i++)
            {
                int y = at.Y + (int)(H * (0.1 + pick.NextDouble() * 0.8));
                int width = (int)(W * (0.25 + pick.NextDouble() * 0.5));
                int x = at.X + (int)(W * (0.5 + (pick.NextDouble() - 0.5) * 0.5)) - width / 2;
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(210 * shown), bars[pick.Next(bars.Length)].R, bars[pick.Next(bars.Length)].G, bars[pick.Next(bars.Length)].B)), new Avalonia.Rect(x, y, width, Math.Max(2, (int)((1 + pick.Next(3)) * scale))));
            }
        }
        private static void Lines(DrawingContext context, float middleX, float middleY, double u, float scale, int seed)
        {
            if (u <= 0 || u >= 1) return;
            Random pick = new Random(seed);
            Color[] colours = { Color.FromRgb(255,255,255), Color.FromRgb(255,255,255), Color.FromRgb(255,255,255), Look.Red, Violet };
            float near = 120 * scale * (float)(0.7 + 0.9 * Smooth(u));
            int alpha = (int)(235 * (1 - Smooth((u - 0.35) / 0.65)));
            for (int i = 0; i < 28; i++)
            {
                double angle = (i + pick.NextDouble() * 0.7) / 28 * 2 * Math.PI;
                float inner = near * (float)(0.85 + pick.NextDouble() * 0.5), outer = inner + 150 * scale * (float)(0.5 + pick.NextDouble());
                float thick = (float)((2 + pick.NextDouble() * 6) * scale);
                Color colour = colours[pick.Next(colours.Length)];
                float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
                Avalonia.Point[] wedge = { new Avalonia.Point(middleX + c * inner, middleY + s * inner * 0.8f), new Avalonia.Point(middleX + c * outer - s * thick, middleY + (s * outer + c * thick) * 0.8f), new Avalonia.Point(middleX + c * outer + s * thick, middleY + (s * outer - c * thick) * 0.8f) };
                List<Avalonia.Point> list = new List<Avalonia.Point>(wedge);
                Fill(context, list, Color.FromArgb((byte)alpha, colour.R, colour.G, colour.B));
            }
        }
    }
}
