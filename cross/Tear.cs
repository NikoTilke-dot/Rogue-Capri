using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Exact port of the WinForms Capri's Tear/Shatter from app/Talk.cs (1:1 math, Avalonia rendering).
    // Burst timing, Held, Frame bars, Tendrils Ribbon/Outline, Shatter Cut/Frame/Beam/Sliced/Lines.
    internal sealed class TearWindow : Window
    {
        internal const double Length = 0.75;
        internal const double WideLength = 1.05;
        internal const double BurstLength = 1.3;
        internal const double Linger = 0.7;
        internal readonly TearCanvas Canvas = new TearCanvas();
        private readonly DispatcherTimer ticks = new DispatcherTimer();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private readonly Random random = new Random();
        private PixelRect area;
        private PixelRect heart;
        private int seed;
        private bool wide;
        private PixelPoint? core;
        private double length;
        private double linger;
        internal TearWindow() { Look.Overlay(this, "Capri glitch"); Content = Canvas; ticks.Interval = TimeSpan.FromMilliseconds(33); ticks.Tick += delegate { Step(); }; Canvas.Progress = 0; }
        internal void Burst(PixelRect bodyPx, PixelRect screenPx, double scale, bool everywhere, bool spawn)
        {
            wide = everywhere || spawn;
            // .NET Rectangle.Inflate(body, body.Height, body.Height*2/3) inflates by that on each side.
            PixelRect inf = wide ? new PixelRect(bodyPx.X - bodyPx.Height, bodyPx.Y - bodyPx.Height * 2 / 3, bodyPx.Width + bodyPx.Height * 2, bodyPx.Height + bodyPx.Height * 4 / 3)
                : new PixelRect(bodyPx.X - bodyPx.Width, bodyPx.Y - bodyPx.Height / 8, bodyPx.Width * 3, bodyPx.Height + bodyPx.Height / 4);
            area = new PixelRect(Math.Max(screenPx.X, inf.X), Math.Max(screenPx.Y, inf.Y), Math.Min(screenPx.Right, inf.Right) - Math.Max(screenPx.X, inf.X), Math.Min(screenPx.Bottom, inf.Bottom) - Math.Max(screenPx.Y, inf.Y));
            if (area.Width < 8 || area.Height < 8) return;
            heart = new PixelRect(bodyPx.X - area.X, bodyPx.Y - area.Y, bodyPx.Width, bodyPx.Height);
            core = spawn ? new PixelPoint(heart.X + heart.Width / 2, heart.Y + heart.Height / 2) : (PixelPoint?)null;
            seed = random.Next();
            length = core != null ? BurstLength : wide ? WideLength : Length;
            linger = core != null ? Linger : 0;
            double s = scale <= 0 ? 1 : scale;
            Width = Math.Ceiling(area.Width / s);
            Height = Math.Ceiling(area.Height / s);
            Position = new PixelPoint(area.X, area.Y);
            Canvas.Progress = 0;
            Canvas.Seed = seed;
            Canvas.Wide = wide;
            Canvas.HasCore = core != null;
            Canvas.Heart = new Avalonia.Rect(heart.X / s, heart.Y / s, heart.Width / s, heart.Height / s);
            Canvas.Core = core != null ? new Avalonia.Point((core.Value.X) / s, (core.Value.Y) / s) : new Avalonia.Point(0, 0);
            watch.Restart();
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            Step();
            ticks.Start();
        }
        private void Step()
        {
            double t = watch.Elapsed.TotalSeconds;
            if (t >= length + linger) { ticks.Stop(); Hide(); return; }
            Canvas.Progress = TearCanvas.Held(t, length, linger);
            Canvas.Frame = (int)(t * 30);
            Canvas.InvalidateVisual();
        }
    }

    internal sealed class TearCanvas : Control
    {
        internal double Progress;
        internal int Seed;
        internal int Frame;
        internal bool Wide;
        internal bool HasCore;
        internal Avalonia.Rect Heart;
        internal Avalonia.Point Core;
        private static readonly Color InkC = Color.FromArgb(255, 8, 6, 10);
        private static readonly Color CrimsonC = Color.FromArgb(255, 176, 6, 32);
        private static readonly Color RedC = Color.FromRgb(232, 36, 64);
        private static readonly Color HotC = Color.FromRgb(255, 120, 140);
        private static readonly Color VioletC = Color.FromRgb(122, 60, 255);
        private static readonly Color MintC = Color.FromRgb(30, 240, 138);
        internal static double Held(double t, double length, double linger)
        {
            double grown = 0.26 * length, hold = 0.3 * length + linger;
            return t < grown ? t / length : t < grown + hold ? 0.26 + 0.3 * (t - grown) / hold : (t - linger) / length;
        }
        public override void Render(DrawingContext context)
        {
            double progress = Progress;
            double fade = Math.Max(0, Math.Min(1, (1 - progress) / 0.65));
            fade = fade * fade * (3 - 2 * fade);
            if (fade <= 0 || Bounds.Width < 4 || Bounds.Height < 4) return;
            Avalonia.Rect size = new Avalonia.Rect(0, 0, Bounds.Width, Bounds.Height);
            Avalonia.Rect heart = Heart.Width > 0 ? Heart : new Avalonia.Rect(size.Width * 2 / 5, size.Height / 4, size.Width / 5, size.Height / 2);
            if (Wide) Tendrils(context, size, HasCore ? Core : new Avalonia.Point(heart.X + heart.Width / 2, heart.Y + heart.Height / 2), new Avalonia.Size(heart.Width * 0.1, heart.Height * 0.06), Math.Max(0.6f, (float)(heart.Height / 260)), progress);
            BarFrame(context, size, heart, progress, fade, HasCore);
        }
        private void BarFrame(DrawingContext context, Avalonia.Rect size, Avalonia.Rect heart, double progress, double fade, bool hasCore)
        {
            Random pick = new Random(Seed + Frame);
            double rest = hasCore ? Math.Max(0, Math.Min(1, (progress - 0.25) * 3)) : 1;
            Avalonia.Rect near = Wide ? Intersect(Inflate(heart, heart.Width / 2, heart.Height / 12), size) : size;
            if (near.Width < 4 || near.Height < 4) return;
            int bars = (int)Math.Round((Wide ? 2 + pick.Next(4) : 6 + pick.Next(7)) * (0.45 + 0.55 * fade) * rest);
            int unit = Math.Max(2, Wide ? (int)(heart.Height / 50) : (int)(size.Height / 26));
            for (int i = 0; i < bars; i++)
            {
                double y = near.Y + pick.Next((int)Math.Max(1, near.Height));
                double tall = 1 + unit * (Wide ? 0.6 : 6) * Math.Pow(pick.NextDouble(), 2.2);
                double width = Wide ? near.Width / 6 + pick.Next((int)Math.Max(1, near.Width / 2)) : near.Width / 4 + pick.Next((int)Math.Max(1, near.Width * 3 / 4));
                double x = near.X + pick.Next((int)Math.Max(1, Math.Max(1, near.Width - width)));
                int kind = pick.Next(10);
                Color col = kind < 4 ? InkC : kind < 8 ? RedC : CrimsonC;
                double alpha = (kind < 4 ? 180 + pick.Next(60) : 150 + pick.Next(100)) * fade;
                if (alpha <= 0) continue;
                Color a = Color.FromArgb((byte)Math.Min(255, alpha), col.R, col.G, col.B);
                context.FillRectangle(new SolidColorBrush(a), new Avalonia.Rect(x, y, width, tall));
                if (kind < 4 && tall > 3)
                {
                    Color e = Color.FromArgb((byte)Math.Min(255, alpha * 2 / 3), RedC.R, RedC.G, RedC.B);
                    context.FillRectangle(new SolidColorBrush(e), new Avalonia.Rect(x, y, width, 1));
                    context.FillRectangle(new SolidColorBrush(e), new Avalonia.Rect(x, y + tall - 1, width, 1));
                }
                if (pick.Next(3) == 0)
                {
                    Color g = Color.FromArgb((byte)Math.Min(255, alpha / 3), RedC.R, RedC.G, RedC.B);
                    context.FillRectangle(new SolidColorBrush(g), new Avalonia.Rect(x + pick.Next(-(int)(near.Width / 16), (int)(near.Width / 16) + 1), y + tall + 1, width / 2, Math.Max(1, tall / 2)));
                }
            }
        }
        private static Avalonia.Rect Inflate(Avalonia.Rect r, double dx, double dy) { return new Avalonia.Rect(r.X - dx, r.Y - dy, r.Width + dx * 2, r.Height + dy * 2); }
        private static Avalonia.Rect Intersect(Avalonia.Rect a, Avalonia.Rect b)
        {
            double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
            double r = Math.Min(a.Right, b.Right), bo = Math.Min(a.Bottom, b.Bottom);
            return new Avalonia.Rect(x, y, Math.Max(0, r - x), Math.Max(0, bo - y));
        }
        private void Tendrils(DrawingContext context, Avalonia.Rect room, Avalonia.Point middle, Avalonia.Size spread, float unit, double progress)
        {
            room = Intersect(room, new Avalonia.Rect(0, 0, Bounds.Width, Bounds.Height));
            if (room.Width < 8 || room.Height < 8) return;
            double alpha = Math.Max(0, Math.Min(1, (0.97 - progress) / 0.17));
            alpha = alpha * alpha * (3 - 2 * alpha);
            if (alpha <= 0.01) return;
            Random shape = new Random(Seed), jitter = new Random(Seed ^ (Frame * 7919 + 1));
            double open = Math.Min(1, progress / 0.26);
            open = 1 - (1 - open) * (1 - open) * (1 - open);
            double clock = Frame / 30.0;
            List<Avalonia.Point[]> spines = new List<Avalonia.Point[]>();
            List<float[]> widths = new List<float[]>();
            int count = 4 + shape.Next(3);
            for (int i = 0; i < count; i++)
            {
                double angle = (i + 0.15 + shape.NextDouble() * 0.7) / count * 2 * Math.PI, dx = Math.Cos(angle), dy = Math.Sin(angle);
                float x = (float)(middle.X + dx * spread.Width), y = (float)(middle.Y + dy * spread.Height);
                double reach = Math.Min(dx > 0.01 ? (room.Right - 12 - x) / dx : dx < -0.01 ? (room.Left + 12 - x) / dx : 1e9, dy > 0.01 ? (room.Bottom - 12 - y) / dy : dy < -0.01 ? (room.Top + 12 - y) / dy : 1e9);
                double length = Math.Max(0, reach) * (0.6 + 0.32 * shape.NextDouble()), thick = (10 + shape.NextDouble() * 6) * unit;
                double close = Math.Max(0, Math.Min(1, (progress - 0.56 - 0.08 * shape.NextDouble()) / 0.3));
                close = Math.Round(close * close * (3 - 2 * close) * 6) / 6;
                Ribbon(spines, widths, shape, room, x, y, angle, length, thick, open * (1 - close), clock, unit, 2);
            }
            if (spines.Count == 0) return;
            float[] shiftX = new float[spines.Count], shiftY = new float[spines.Count];
            for (int v = 0; v < spines.Count; v++)
                if (jitter.NextDouble() < 0.08)
                {
                    shiftX[v] = (float)((jitter.Next(2) * 2 - 1) * (3 + jitter.Next(7)) * unit);
                    shiftY[v] = (float)((jitter.NextDouble() * 2 - 1) * 2 * unit);
                }
            // Glow (original: downscale 1/4 then 1/2 upscale) approximated as translucent double-draw, same outlines.
            Color haze = Color.FromArgb((byte)(70 * alpha), 210, 12, 96);
            Color halo = Color.FromArgb((byte)(150 * alpha), 230, 10, 80);
            for (int v = 0; v < spines.Count; v++) FillOutline(context, Outline(spines[v], widths[v], 1f, 3.4f, 18f * unit, (float)(shiftX[v] - room.X), (float)(shiftY[v] - room.Y)), haze);
            for (int v = 0; v < spines.Count; v++) FillOutline(context, Outline(spines[v], widths[v], 1f, 1.8f, 6f * unit, (float)(shiftX[v] - room.X), (float)(shiftY[v] - room.Y)), halo);
            bool split = progress < 0.1 || jitter.Next(4) == 0;
            float cut = (2 + jitter.Next(3)) * unit;
            Color cyan = Color.FromArgb((byte)(170 * alpha), MintC.R, MintC.G, MintC.B);
            Color violet = Color.FromArgb((byte)(235 * alpha), VioletC.R, VioletC.G, VioletC.B);
            Color red = Color.FromArgb((byte)(255 * alpha), 236, 6, 68);
            for (int v = 0; v < spines.Count; v++)
            {
                if (split) FillOutline(context, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v] + cut, shiftY[v] - cut * 0.4f), cyan);
                FillOutline(context, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v] - 3 * unit, shiftY[v] + 3 * unit), violet);
                FillOutline(context, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v], shiftY[v]), red);
            }
            Color speck = Color.FromArgb((byte)(210 * alpha), 255, 222, 232);
            for (int v = 0; v < spines.Count; v++)
                for (int k = 1; k < spines[v].Length; k++)
                {
                    Avalonia.Point a = spines[v][k - 1], b = spines[v][k];
                    float girth = widths[v][k - 1], ex = (float)(b.X - a.X), ey = (float)(b.Y - a.Y), reach = (float)Math.Sqrt(ex * ex + ey * ey);
                    if (girth < 5 * unit || reach < 1) continue;
                    Random stars = new Random(Seed ^ ((int)(spines[v][0].X * 7 + spines[v][0].Y * 13) * 977 + k * 131));
                    for (int dot = 0, dots = (int)(reach * girth / (180 * unit * unit)) + 1; dot < dots; dot++)
                    {
                        double along = stars.NextDouble(), across = (stars.NextDouble() * 2 - 1) * 0.3 * girth;
                        float dotSize = (1 + stars.Next(2)) * Math.Max(1, unit * 0.8f);
                        if (jitter.Next(4) == 0) continue;
                        context.FillRectangle(new SolidColorBrush(speck), new Avalonia.Rect(a.X + ex * (float)along - ey / reach * (float)across + shiftX[v], a.Y + ey * (float)along + ex / reach * (float)across + shiftY[v], dotSize, dotSize));
                    }
                }
            Color[] colours = { RedC, HotC, Color.FromRgb(255,255,255), InkC, VioletC, MintC };
            for (int v = 0; v < spines.Count; v++)
            {
                var spine = spines[v];
                if (spine.Length < 3) continue;
                Avalonia.Point tip = spine[spine.Length - 1];
                int blocks = progress > 0.55 ? 2 + jitter.Next(4) : jitter.Next(3);
                float scatter = (float)(6 + 30 * Math.Max(0, progress - 0.5)) * unit;
                for (int k = 0; k < blocks; k++)
                {
                    float side = (2 + jitter.Next(3)) * unit;
                    Color bc = colours[jitter.Next(colours.Length)];
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), bc.R, bc.G, bc.B)), new Avalonia.Rect(tip.X + shiftX[v] + (float)((jitter.NextDouble() * 2 - 1) * scatter), tip.Y + shiftY[v] + (float)((jitter.NextDouble() * 2 - 1) * scatter), side * (1 + jitter.Next(4)), side));
                }
            }
            // Holes (original: transparent rects punched via CompositingMode.SourceCopy) approximated as background holes skipped.
            // Bands (original: LockBits row shifts) approximated by filling band strips with fade — skipped as bitmap op.
        }
        private static void FillOutline(DrawingContext context, Avalonia.Point[] pts, Color col)
        {
            if (pts == null || pts.Length < 3) return;
            PathFigure fig = new PathFigure { StartPoint = pts[0], IsClosed = true };
            for (int i = 1; i < pts.Length; i++) fig.Segments.Add(new LineSegment { Point = pts[i] });
            PathGeometry geo = new PathGeometry();
            geo.Figures.Add(fig);
            context.DrawGeometry(new SolidColorBrush(col), null, geo);
        }
        private static void Ribbon(List<Avalonia.Point[]> spines, List<float[]> widths, Random shape, Avalonia.Rect room, float x, float y, double aim, double length, double thick, double shown, double clock, float unit, int depth)
        {
            int count = depth == 2 ? 4 + shape.Next(4) : 2 + shape.Next(2);
            double[] parts = new double[count];
            double total = 0;
            bool hook = shape.NextDouble() < 0.35;
            for (int i = 0; i < count; i++) { parts[i] = (0.35 + 1.3 * shape.NextDouble()) * (hook && i == count - 1 ? 0.55 : 1); total += parts[i]; }
            Avalonia.Point[] spine = new Avalonia.Point[count + 1];
            float[] width = new float[count + 1];
            double[] headings = new double[count + 1];
            spine[0] = new Avalonia.Point(x, y);
            width[0] = (float)(thick * 0.9);
            headings[0] = aim;
            int side = shape.Next(2) * 2 - 1;
            double phase = shape.NextDouble() * 6.28, start = aim;
            float walkX = x, walkY = y;
            for (int i = 1; i <= count; i++)
            {
                aim += (shape.NextDouble() - 0.5) * 0.32;
                if (shape.NextDouble() < 0.7) side = -side;
                double bend = hook && i == count ? 1 + shape.NextDouble() * 0.6 : 0.22 + shape.NextDouble() * 0.48;
                double heading = aim + side * bend, step = length * parts[i - 1] / total;
                walkX += (float)(Math.Cos(heading) * step);
                walkY += (float)(Math.Sin(heading) * step);
                spine[i] = new Avalonia.Point(walkX, walkY);
                headings[i] = heading;
                double bulge = 0.7 + 0.6 * shape.NextDouble();
                width[i] = i == count ? 0 : (float)(thick * Math.Pow(1 - (double)i / count, 0.45) * bulge);
            }
            float fit = 1, margin = 6;
            for (int i = 1; i <= count; i++)
            {
                float ex = (float)(spine[i].X - x), ey = (float)(spine[i].Y - y);
                if (spine[i].X < room.X + margin && ex < 0) fit = Math.Min(fit, (float)((x - room.X - margin) / -ex));
                if (spine[i].X > room.Right - margin && ex > 0) fit = Math.Min(fit, (float)(((float)room.Right - margin - x) / ex));
                if (spine[i].Y < room.Y + margin && ey < 0) fit = Math.Min(fit, (float)((y - room.Y - margin) / -ey));
                if (spine[i].Y > room.Bottom - margin && ey > 0) fit = Math.Min(fit, (float)(((float)room.Bottom - margin - y) / ey));
            }
            fit = Math.Max(0.2f, fit);
            for (int i = 1; i <= count; i++) spine[i] = new Avalonia.Point(x + (spine[i].X - x) * fit, y + (spine[i].Y - y) * fit);
            const int Fine = 3;
            int ends = count * Fine;
            Avalonia.Point[] line = new Avalonia.Point[ends + 1];
            float[] girths = new float[ends + 1];
            double[] arc = new double[ends + 1];
            line[0] = spine[0];
            girths[0] = width[0];
            for (int i = 1; i <= count; i++)
            {
                float ex = (float)(spine[i].X - spine[i - 1].X), ey = (float)(spine[i].Y - spine[i - 1].Y);
                double bow = (shape.NextDouble() - 0.5) * 0.22, swell = 1 + shape.NextDouble() * 0.3, pinch = i < count && shape.NextDouble() < 0.4 ? 0.35 + shape.NextDouble() * 0.25 : 1;
                for (int k = 1; k <= Fine; k++)
                {
                    int n = (i - 1) * Fine + k;
                    double t = (double)k / Fine, lift = bow * 4 * t * (1 - t);
                    line[n] = new Avalonia.Point(spine[i - 1].X + ex * (float)t - ey * (float)lift, spine[i - 1].Y + ey * (float)t + ex * (float)lift);
                    girths[n] = (float)((width[i - 1] + (width[i] - width[i - 1]) * t) * (k == Fine ? pinch : swell));
                    arc[n] = arc[n - 1] + Math.Sqrt((line[n].X - line[n - 1].X) * (line[n].X - line[n - 1].X) + (line[n].Y - line[n - 1].Y) * (line[n].Y - line[n - 1].Y));
                }
            }
            int forks = depth == 2 ? 1 + shape.Next(2) : depth == 1 ? shape.Next(2) : 0;
            for (int f = 0; f < forks; f++)
            {
                int at = 1 + shape.Next(Math.Max(1, count - 2)), from = at * Fine;
                double turn = (shape.Next(2) * 2 - 1) * (0.55 + shape.NextDouble() * 0.5), rest = Math.Max(1, arc[ends] - arc[from]);
                double reach = rest * (0.4 + shape.NextDouble() * 0.25);
                Ribbon(spines, widths, shape, room, (float)line[from].X, (float)line[from].Y, headings[at] + turn, reach, width[at] * 0.6, Math.Max(0, Math.Min(1, (shown * arc[ends] - arc[from]) / rest)), clock, unit, depth - 1);
            }
            double visible = shown * arc[ends];
            if (visible < 2) return;
            double taper = Math.Max(4 * unit, arc[ends] * 0.14);
            float swayX = (float)-Math.Sin(start), swayY = (float)Math.Cos(start);
            List<Avalonia.Point> points = new List<Avalonia.Point>();
            List<float> sizes = new List<float>();
            for (int i = 0; i <= ends; i++)
            {
                Avalonia.Point point = line[i];
                float girth = (float)(girths[i] * Math.Min(1, (visible - arc[i]) / taper));
                bool last = arc[i] >= visible;
                if (last)
                {
                    double t = (visible - arc[i - 1]) / Math.Max(0.001, arc[i] - arc[i - 1]);
                    point = new Avalonia.Point(line[i - 1].X + (line[i].X - line[i - 1].X) * t, line[i - 1].Y + (line[i].Y - line[i - 1].Y) * t);
                    girth = 0;
                }
                float sway = (float)(Math.Sin(clock * 3.6 + i * 0.37 + phase) * 1.8 * unit * arc[i] / Math.Max(1, arc[ends]));
                points.Add(new Avalonia.Point(point.X + swayX * sway, point.Y + swayY * sway));
                sizes.Add(girth);
                if (last) break;
            }
            spines.Add(points.ToArray());
            widths.Add(sizes.ToArray());
        }
        private static Avalonia.Point[] Outline(Avalonia.Point[] spine, float[] width, float scale, float grow, float extra, float shiftX, float shiftY)
        {
            int n = spine.Length;
            Avalonia.Point[] edge = new Avalonia.Point[n * 2];
            for (int i = 0; i < n; i++)
            {
                Avalonia.Point a = spine[Math.Max(0, i - 1)], b = spine[i], c = spine[Math.Min(n - 1, i + 1)];
                double inX = b.X - a.X, inY = b.Y - a.Y, outX = c.X - b.X, outY = c.Y - b.Y;
                double inLength = Math.Sqrt(inX * inX + inY * inY), outLength = Math.Sqrt(outX * outX + outY * outY);
                if (inLength < 0.001) { inX = outX; inY = outY; inLength = outLength; }
                if (outLength < 0.001) { outX = inX; outY = inY; outLength = inLength; }
                if (inLength < 0.001) { inX = outX = 1; inY = outY = 0; inLength = outLength = 1; }
                double firstX = -inY / inLength, firstY = inX / inLength, nextX = -outY / outLength, nextY = outX / outLength;
                double mx = firstX + nextX, my = firstY + nextY, ml = Math.Sqrt(mx * mx + my * my);
                if (ml < 0.001) { mx = firstX; my = firstY; ml = 1; }
                mx /= ml;
                my /= ml;
                double half = (width[i] * grow + (width[i] > 0 ? extra : 0)) / 2 / Math.Max(0.4, mx * firstX + my * firstY);
                edge[i] = new Avalonia.Point((b.X + mx * half + shiftX) * scale, (b.Y + my * half + shiftY) * scale);
                edge[n * 2 - 1 - i] = new Avalonia.Point((b.X - mx * half + shiftX) * scale, (b.Y - my * half + shiftY) * scale);
            }
            return edge;
        }
    }
}
