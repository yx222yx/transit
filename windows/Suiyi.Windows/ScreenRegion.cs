using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace Suiyi.Windows;

internal sealed record ScreenRegion(Bitmap Image, Rectangle Bounds);

internal sealed class RegionSelector : Forms.Form
{
    private readonly Bitmap frozen;
    private readonly Rectangle desktop;
    private readonly TaskCompletionSource<ScreenRegion?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point? start;
    private Rectangle selected;

    private RegionSelector(Bitmap image, Rectangle bounds)
    {
        frozen = image;
        desktop = bounds;
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

    public static Task<ScreenRegion?> SelectAsync()
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        Bitmap? image = null;
        try
        {
            image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            var form = new RegionSelector(image, bounds);
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
        e.Graphics.DrawString("拖动框选英／俄文字 · Esc 取消", font, Brushes.White, x, y);
    }

    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right) { Close(); return; }
        if (e.Button != Forms.MouseButtons.Left) return;
        start = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(Forms.MouseEventArgs e)
    {
        if (start is { } point)
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
        var crop = frozen.Clone(selected, PixelFormat.Format32bppArgb);
        completion.TrySetResult(new ScreenRegion(crop, new Rectangle(desktop.X + selected.X, desktop.Y + selected.Y, selected.Width, selected.Height)));
        Close();
    }

    protected override void OnKeyDown(Forms.KeyEventArgs e)
    {
        if (e.KeyCode == Forms.Keys.Escape) Close();
        base.OnKeyDown(e);
    }
}
