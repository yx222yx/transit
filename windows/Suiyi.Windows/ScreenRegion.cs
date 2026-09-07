using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace Suiyi.Windows;

internal sealed record ScreenRegion(Bitmap Image, Rectangle Bounds, IntPtr TargetWindow = default, bool WholeWindow = false);

internal sealed class RegionSelector : Forms.Form
{
    private readonly Bitmap frozen;
    private readonly Rectangle desktop;
    private readonly TaskCompletionSource<ScreenRegion?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point? start;
    private Rectangle selected;
    private readonly IReadOnlyList<ReadingWindowInfo>? windows;
    private readonly bool wholeWindow;
    private ReadingWindowInfo? target;

    private RegionSelector(Bitmap image, Rectangle bounds, bool reading, bool whole)
    {
        frozen = image;
        desktop = bounds;
        windows = reading ? ReadingWindow.Snapshot() : null;
        wholeWindow = whole;
        AutoScaleMode = Forms.AutoScaleMode.None;
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Forms.Cursors.Cross;
        Text = "随译 · 框选屏幕文字";
        FormClosed += (_, _) => { completion.TrySetResult(null); frozen.Dispose(); };
    }

    public static Task<ScreenRegion?> SelectAsync(bool reading = false, bool wholeWindow = false)
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        Bitmap? image = null;
        try
        {
            image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            var form = new RegionSelector(image, bounds, reading, wholeWindow);
            form.Show();
            form.Activate();
            return form.completion.Task;
        }
        catch
        {
            image?.Dispose();
            throw new InvalidOperationException("无法截取当前屏幕。请切换到普通窗口后重试。");
        }
    }

    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(frozen, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(135, 13, 24, 24));
        using var outside = new Region(ClientRectangle);
        if (selected.Width > 0 && selected.Height > 0) outside.Exclude(selected);
        e.Graphics.FillRegion(shade, outside);
        if (selected.Width > 0 && selected.Height > 0)
        {
            using var line = new Pen(Color.FromArgb(75, 220, 166), 2);
            e.Graphics.DrawRectangle(line, selected);
        }
        using var font = new Font("Microsoft YaHei UI", 12);
        var cursor = PointToClient(Forms.Cursor.Position);
        int x = Math.Clamp(cursor.X + 18, 12, Math.Max(12, Width - 400));
        int y = Math.Clamp(cursor.Y + 26, 12, Math.Max(12, Height - 60));
        e.Graphics.DrawString(wholeWindow ? "点击目标窗口 · Esc 取消" : "拖动框选英／俄文字 · Esc 取消", font, Brushes.White, x, y);
    }

    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right) { Close(); return; }
        if (e.Button != Forms.MouseButtons.Left) return;
        target = windows?.FirstOrDefault(window => window.Bounds.Contains(new Point(desktop.X + e.X, desktop.Y + e.Y)));
        if (windows is not null && target is null) return;
        start = e.Location;
        if (wholeWindow && target is not null)
        {
            selected = Rectangle.Intersect(target.Bounds, desktop);
            selected.Offset(-desktop.X, -desktop.Y);
        }
        Capture = true;
    }

    protected override void OnMouseMove(Forms.MouseEventArgs e)
    {
        if (!wholeWindow && start is { } point)
        {
            int x = Math.Clamp(e.X, 0, ClientSize.Width);
            int y = Math.Clamp(e.Y, 0, ClientSize.Height);
            selected = Rectangle.FromLTRB(Math.Min(point.X, x), Math.Min(point.Y, y), Math.Max(point.X, x), Math.Max(point.Y, y));
        }
        Invalidate();
    }

    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left || start is null) return;
        Capture = false;
        if (selected.Width < 12 || selected.Height < 12) { start = null; return; }
        var screenBounds = new Rectangle(desktop.X + selected.X, desktop.Y + selected.Y, selected.Width, selected.Height);
        if (target is not null && !target.Bounds.Contains(screenBounds)) { start = null; selected = Rectangle.Empty; Invalidate(); return; }
        var crop = frozen.Clone(selected, PixelFormat.Format32bppArgb);
        completion.TrySetResult(new ScreenRegion(crop, screenBounds, target?.Handle ?? IntPtr.Zero, wholeWindow));
        Close();
        if (target is not null) ReadingWindow.ActivateTarget(target.Handle);
    }

    protected override void OnKeyDown(Forms.KeyEventArgs e)
    {
        if (e.KeyCode == Forms.Keys.Escape) Close();
        base.OnKeyDown(e);
    }
}
