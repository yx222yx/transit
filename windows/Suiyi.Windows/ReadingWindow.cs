using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Suiyi.Windows;

internal sealed record ReadingWindowInfo(IntPtr Handle, Rectangle Bounds);
internal sealed record ReadingTarget(IntPtr Handle, int ProcessId, Rectangle RelativeBounds, bool WholeWindow)
{
    internal static ReadingTarget From(ScreenRegion region)
    {
        var bounds = ReadingWindow.Bounds(region.TargetWindow);
        if (bounds.IsEmpty || !bounds.Contains(region.Bounds)) throw new InvalidOperationException("选区须完全位于同一个窗口内，请重新选择。");
        return new ReadingTarget(region.TargetWindow, Native.GetWindowProcessId(region.TargetWindow),
            new Rectangle(region.Bounds.X - bounds.X, region.Bounds.Y - bounds.Y, region.Bounds.Width, region.Bounds.Height), region.WholeWindow);
    }

    internal Rectangle CurrentBounds()
    {
        var window = ReadingWindow.Bounds(Handle);
        return WholeWindow ? window : new Rectangle(window.X + RelativeBounds.X, window.Y + RelativeBounds.Y, RelativeBounds.Width, RelativeBounds.Height);
    }
}

internal static class ReadingWindow
{
    internal static IReadOnlyList<ReadingWindowInfo> Snapshot()
    {
        var windows = new List<ReadingWindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (Visible(handle) && Native.GetWindowProcessId(handle) != Environment.ProcessId)
            {
                var bounds = Bounds(handle);
                if (bounds.Width >= 12 && bounds.Height >= 12) windows.Add(new ReadingWindowInfo(handle, bounds));
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    internal static Rectangle Bounds(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle)) return Rectangle.Empty;
        if (DwmGetWindowAttribute(handle, 9, out Rect rectangle, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(handle, out rectangle)) return Rectangle.Empty;
        return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    internal static string? Unavailable(ReadingTarget target, out bool closed)
    {
        closed = !IsWindow(target.Handle) || Native.GetWindowProcessId(target.Handle) != target.ProcessId;
        if (closed) return "目标窗口已关闭，持续翻译结束。";
        if (IsIconic(target.Handle) || !IsWindowVisible(target.Handle)) return "目标窗口已最小化或隐藏，返回后恢复。";
        if (Native.GetForegroundWindow() != target.Handle) return "已临时暂停 · 返回目标窗口后恢复";
        if (GetWindowDisplayAffinity(target.Handle, out uint affinity) && affinity != 0) return "目标窗口限制屏幕捕获，已暂停。";
        var bounds = target.CurrentBounds();
        if (!Bounds(target.Handle).Contains(bounds) || !System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds))
            return "选区超出窗口或屏幕，请重选范围。";
        bool occluded = false;
        EnumWindows((handle, _) =>
        {
            if (handle == target.Handle) return false;
            if (Native.GetWindowProcessId(handle) != Environment.ProcessId && Visible(handle) && Bounds(handle).IntersectsWith(bounds))
            {
                occluded = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return occluded ? "目标选区被其他窗口遮挡，移开后恢复。" : null;
    }

    private static bool Visible(IntPtr handle)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle)) return false;
        return DwmGetWindowAttribute(handle, 14, out uint cloaked, sizeof(uint)) != 0 || cloaked == 0;
    }

    internal static Bitmap Capture(Rectangle bounds, IReadOnlyList<Rectangle> passwordBounds)
    {
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            foreach (var password in passwordBounds)
            {
                var intersection = Rectangle.Intersect(password, bounds);
                if (intersection.IsEmpty) continue;
                intersection.Offset(-bounds.X, -bounds.Y);
                graphics.FillRectangle(Brushes.White, intersection);
            }
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    internal static ulong Signature(Bitmap image)
        => Signature(image, new[] { new Rectangle(0, 0, image.Width, image.Height) });

    internal static ulong Signature(Bitmap image, IEnumerable<Rectangle> regions)
    {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            ulong hash = 14695981039346656037UL;
            foreach (var region in regions)
            {
                var bounds = Rectangle.Intersect(region, new Rectangle(0, 0, image.Width, image.Height));
                for (int row = bounds.Top; row < bounds.Bottom; row++)
                    for (int column = bounds.Left; column < bounds.Right; column++)
                    {
                        int index = row * data.Stride + column * 4;
                        hash = (hash ^ (uint)(pixels[index] >> 3)) * 1099511628211UL;
                        hash = (hash ^ (uint)(pixels[index + 1] >> 3)) * 1099511628211UL;
                        hash = (hash ^ (uint)(pixels[index + 2] >> 3)) * 1099511628211UL;
                    }
            }
            return hash;
        }
        finally { image.UnlockBits(data); }
    }

    internal static bool ExcludeFromCapture(IntPtr window) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && SetWindowDisplayAffinity(window, 0x11);

    internal static bool KeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    internal static void ActivateTarget(IntPtr handle) => SetForegroundWindow(handle);
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr handle, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out uint value, int size);
}
