using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;

namespace CapriDesktop
{
    // Desktop icon rectangles (original: app/Ledges.cs Icons() + app/Icons.cs Read), so Capri can sit on desktop shortcuts.
    // Original reads them via UI Automation bounding rectangles; here via the Win32 list view (Progman/WorkerW -> SHELLDLL_DefView
    // -> SysListView32): LVM_GETITEMCOUNT + LVM_GETITEMPOSITION through Explorer's process, same as app/Icons.cs Reach.
    // LVM positions are client-relative points (top-left of each icon cell); cells are roughly 76x98 px at 100% DPI.
    internal static class DesktopIcons
    {
        private static readonly bool OnWin = OperatingSystem.IsWindows();
        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const int LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const int SMTO_ABORTIFHUNG = 0x0002;
        private const uint MemCommit = 0x1000 | 0x2000, PageReadWrite = 0x04, MemRelease = 0x8000;
        private const int ProcessVm = 0x0008 | 0x0010 | 0x0020;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out IntPtr read);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool ClientToScreen(IntPtr window, ref Pnt point);
        [StructLayout(LayoutKind.Sequential)] private struct Pnt { internal int X, Y; }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string type, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string type, string title);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Win32Callback visit, IntPtr data);
        private delegate bool Win32Callback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);

        internal static IntPtr ListView()
        {
            if (!OnWin) return IntPtr.Zero;
            IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero)
            {
                Win32Callback visit = delegate(IntPtr window, IntPtr data)
                {
                    System.Text.StringBuilder name = new System.Text.StringBuilder(128);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "WorkerW") view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                    return view == IntPtr.Zero;
                };
                EnumWindows(visit, IntPtr.Zero);
                GC.KeepAlive(visit);
            }
            return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        internal static List<PixelRect> Icons()
        {
            List<PixelRect> found = new List<PixelRect>();
            if (!OnWin) return found;
            try
            {
                IntPtr list = ListView();
                if (list == IntPtr.Zero || !IsWindowVisible(list)) return found;
                int count;
                List<PixelPoint> points = ReadPositions(list, out count);
                if (points == null) return found;
                Pnt origin = new Pnt();
                if (!ClientToScreen(list, ref origin)) return found;
                // Grid spacing heuristic: cell width from neighbour distance on the same row, else ~76 px.
                List<PixelPoint> sorted = new List<PixelPoint>(points);
                sorted.Sort(delegate(PixelPoint a, PixelPoint b) { return a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X); });
                int cellW = 76, cellH = 98;
                for (int i = 1; i < sorted.Count; i++)
                    if (Math.Abs(sorted[i].Y - sorted[i - 1].Y) <= 4 && sorted[i].X > sorted[i - 1].X)
                    {
                        int gap = sorted[i].X - sorted[i - 1].X;
                        if (gap >= 24 && gap <= 400) { cellW = gap; break; }
                    }
                foreach (PixelPoint p in points)
                    found.Add(new PixelRect((int)origin.X + p.X, (int)origin.Y + p.Y, cellW, cellH));
            }
            catch (Exception) { }
            return found;
        }

        private static List<PixelPoint> ReadPositions(IntPtr list, out int count)
        {
            count = 0;
            if (list == IntPtr.Zero || !IsWindow(list)) return null;
            int process;
            GetWindowThreadProcessId(list, out process);
            IntPtr handle = OpenProcess(ProcessVm, false, process);
            if (handle == IntPtr.Zero) return null;
            IntPtr buffer = VirtualAllocEx(handle, IntPtr.Zero, new UIntPtr(64), MemCommit, PageReadWrite);
            try
            {
                if (buffer == IntPtr.Zero) return null;
                if (!Send(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, out IntPtr counted)) return null;
                count = counted.ToInt32();
                if (count < 0 || count > 512) return null;
                List<PixelPoint> points = new List<PixelPoint>(count);
                for (int i = 0; i < count; i++)
                {
                    if (!Send(list, LVM_GETITEMPOSITION, new IntPtr(i), buffer, out IntPtr got) || got == IntPtr.Zero) return null;
                    byte[] raw = new byte[8];
                    IntPtr read;
                    if (!ReadProcessMemory(handle, buffer, raw, new UIntPtr(8), out read) || read.ToInt64() < 8) return null;
                    int x = raw[0] | raw[1] << 8 | raw[2] << 16 | raw[3] << 24;
                    int y = raw[4] | raw[5] << 8 | raw[6] << 16 | raw[7] << 24;
                    points.Add(new PixelPoint(x, y));
                }
                return points;
            }
            finally
            {
                if (buffer != IntPtr.Zero) VirtualFreeEx(handle, buffer, UIntPtr.Zero, MemRelease);
                CloseHandle(handle);
            }
        }

        private static bool Send(IntPtr list, int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            IntPtr answer;
            if (SendMessageTimeout(list, message, wParam, lParam, SMTO_ABORTIFHUNG, 2000, out answer) == IntPtr.Zero) return false;
            result = answer;
            return true;
        }
    }
}
