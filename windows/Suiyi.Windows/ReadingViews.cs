using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using DrawingBrushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace Suiyi.Windows;

internal sealed class ReadingOverlay : Forms.Form
{
    private IReadOnlyList<TranslatedBlock> blocks = Array.Empty<TranslatedBlock>();
    internal ReadingOverlay()
    {
        AutoScaleMode = Forms.AutoScaleMode.None;
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        Text = "随译 · 中文覆盖";
        _ = Handle;
        if (!ReadingWindow.ExcludeFromCapture(Handle))
        {
            Dispose();
            throw new InvalidOperationException("当前 Windows 无法排除自家覆盖层。持续覆盖需要 Windows 10 2004 或更新版本；仍可使用单次框译。");
        }
    }

    protected override bool ShowWithoutActivation => true;
    protected override Forms.CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return parameters; }
    }
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
        if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
        base.WndProc(ref message);
    }

    internal void Present(Rectangle screenBounds, IReadOnlyList<TranslatedBlock> translations)
    {
        Bounds = screenBounds;
        blocks = translations;
        if (blocks.Count == 0) { Hide(); return; }
        Invalidate();
        if (!Visible) Show();
    }

    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        foreach (var block in blocks)
        {
            var bounds = block.Source.Bounds;
            bounds.Inflate(2, 2);
            bounds.Intersect(ClientRectangle);
            if (bounds.Width < 4 || bounds.Height < 4) continue;
            e.Graphics.FillRectangle(DrawingBrushes.White, bounds);
            int lines = Math.Max(1, block.Source.Text.Count(character => character == '\n') + 1);
            float fontSize = Math.Clamp(block.Source.Bounds.Height / (float)lines * 0.76f, 12, 26);
            using var font = new Font("Microsoft YaHei UI", fontSize, GraphicsUnit.Pixel);
            var flags = Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPadding;
            Forms.TextRenderer.DrawText(e.Graphics, block.Translation, font, bounds, Color.FromArgb(28, 63, 50), Color.White, flags);
        }
    }
}

internal sealed class ReadingControlBar : Window
{
    private readonly TextBlock status = MainWindow.Label("等待目标内容…", 12);
    private readonly TextBlock count = MainWindow.Label("本次请求 0", 11);
    private readonly Button pause = MainWindow.Button("暂停");
    private readonly Button original = MainWindow.Button("看原文");
    internal event Action? PauseRequested;
    internal event Action? OriginalRequested;
    internal event Action? ReselectRequested;
    internal event Action? EndRequested;
    internal event Action? ReadRequested;
    internal event Action? RetryRequested;

    internal ReadingControlBar()
    {
        Title = "随译 · 持续翻译控制";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = MainWindow.Brush("#F5F8F5");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
        var heading = MainWindow.Label("⋮⋮  随译 · 持续覆盖  |  拖动此处移动", 12, true);
        heading.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) { } };
        panel.Children.Add(heading);
        panel.Children.Add(status);
        var buttons = new WrapPanel();
        Add(pause, PauseRequestedHandler);
        Add(original, () => OriginalRequested?.Invoke());
        Add(MainWindow.Button("完整译文"), () => ReadRequested?.Invoke());
        Add(MainWindow.Button("重试"), () => RetryRequested?.Invoke());
        Add(MainWindow.Button("重选"), () => ReselectRequested?.Invoke());
        Add(MainWindow.Button("结束"), () => EndRequested?.Invoke());
        panel.Children.Add(buttons);
        panel.Children.Add(count);
        Content = new Border { BorderBrush = MainWindow.Brush("#98B5A6"), BorderThickness = new Thickness(1), Child = panel };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x00000080));
            HwndSource.FromHwnd(handle)?.AddHook(NoActivate);
            if (!ReadingWindow.ExcludeFromCapture(handle)) throw new InvalidOperationException("无法排除控制条捕获，请结束后重试。");
        };
        void Add(Button button, Action action) { button.FontSize = 12; button.Padding = new Thickness(10, 6, 10, 6); button.Margin = new Thickness(0, 0, 5, 5); button.Click += (_, _) => action(); buttons.Children.Add(button); }
    }

    private void PauseRequestedHandler() => PauseRequested?.Invoke();
    internal void Update(ReadingSnapshot snapshot, bool paused, bool showOriginal)
    {
        status.Text = snapshot.Status;
        count.Text = $"本次翻译请求 {snapshot.RequestCount}  ·  长译文请打开「完整译文」";
        pause.Content = paused ? "继续" : "暂停";
        original.Content = showOriginal ? "显示中文" : "看原文";
    }
    internal void Place(Rectangle selection)
    {
        var source = PresentationSource.FromVisual(this);
        double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
        var work = Forms.Screen.FromRectangle(selection).WorkingArea;
        Left = Math.Max(work.Left * scaleX, Math.Min(selection.Left * scaleX, work.Right * scaleX - Width));
        Top = Math.Max(work.Top * scaleY, Math.Min(selection.Bottom * scaleY + 10, work.Bottom * scaleY - ActualHeight - 10));
    }
    private static IntPtr NoActivate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
}

internal sealed class ReadingPanel : Window
{
    private readonly TextBox source = Area();
    private readonly TextBox translation = Area();
    internal event Action<string>? CorrectRequested;
    internal ReadingPanel()
    {
        Title = "随译 · 完整译文与原文对照";
        Icon = AppIdentity.WindowIcon;
        Width = 660;
        Height = 650;
        MinWidth = 460;
        MinHeight = 420;
        Background = MainWindow.Brush("#F5F8F5");
        var panel = new DockPanel { Margin = new Thickness(18) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var copy = MainWindow.Button("复制完整译文");
        var correct = MainWindow.Button("修正识别文字");
        copy.Click += (_, _) => { try { if (translation.Text.Length > 0) System.Windows.Clipboard.SetText(translation.Text); } catch { } };
        correct.Click += (_, _) => CorrectRequested?.Invoke(source.Text);
        actions.Children.Add(copy);
        actions.Children.Add(correct);
        actions.Children.Add(MainWindow.Label("显示最近一次捕获内容；暂停期间非实时。返回目标窗口继续更新。", 12));
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var first = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var sourceLabel = MainWindow.Label("本次识别的原文", 13, true);
        DockPanel.SetDock(sourceLabel, Dock.Top);
        first.Children.Add(sourceLabel);
        first.Children.Add(source);
        grid.Children.Add(first);
        var second = new DockPanel();
        var translationLabel = MainWindow.Label("中文 · 完整内容", 13, true);
        DockPanel.SetDock(translationLabel, Dock.Top);
        second.Children.Add(translationLabel);
        second.Children.Add(translation);
        Grid.SetRow(second, 1);
        grid.Children.Add(second);
        panel.Children.Add(grid);
        Content = panel;
    }
    internal void Present(string originalText, string translatedText)
    {
        UpdateContent(originalText, translatedText);
        Show();
        Activate();
    }
    internal void UpdateContent(string originalText, string translatedText) { source.Text = originalText; translation.Text = translatedText; }
    private static TextBox Area() => new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), FontSize = 15 };
}
