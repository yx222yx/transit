using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Suiyi.Windows;

internal sealed class TranslationPopup : Window
{
    private readonly TextBlock source = MainWindow.Label("", 12);
    private readonly TextBlock result = MainWindow.Label("", 16);
    private readonly TextBlock status = MainWindow.Label("", 12);
    private readonly Button copy = MainWindow.Button("复制译文");
    internal event Action? Dismissed;
    internal event Action<string>? CopyRequested;
    internal event Action? EditRequested;
    private System.Drawing.Point anchor;
    private bool needsPlacement = true;
    private bool manuallyMoved;
    private bool shutdown;

    public TranslationPopup()
    {
        Title = "随译 · 译文";
        Icon = AppIdentity.WindowIcon;
        Width = 430;
        Height = 420;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Foreground = MainWindow.Brush("#213A35");
        var border = new Border { Background = Brushes.White, BorderBrush = MainWindow.Brush("#B8D0C4"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(20) };
        var layout = new DockPanel();
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = MainWindow.Button("×");
        close.Click += (_, _) => { Hide(); Dismissed?.Invoke(); };
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        var handle = MainWindow.Label("⋮⋮ 随译　英／俄 → 中文", 15, true);
        handle.Cursor = System.Windows.Input.Cursors.SizeAll;
        handle.ToolTip = "按住这里拖动译文框";
        handle.MouseLeftButtonDown += (_, e) =>
        {
            manuallyMoved = true;
            try { DragMove(); } catch (InvalidOperationException) { }
            e.Handled = true;
        };
        title.Children.Add(handle);
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        copy.Click += (_, _) => CopyRequested?.Invoke(result.Text);
        actions.Children.Add(copy);
        var edit = MainWindow.Button("编辑原文");
        edit.Click += (_, _) => EditRequested?.Invoke();
        actions.Children.Add(edit);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        var body = new StackPanel();
        body.Children.Add(source);
        body.Children.Add(status);
        body.Children.Add(result);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        border.Child = layout;
        Content = border;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, new IntPtr(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L));
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!manuallyMoved) Place(); });
        Closing += (_, e) => { if (!shutdown) { e.Cancel = true; Hide(); Dismissed?.Invoke(); } };
    }

    internal void Present(System.Drawing.Point location, string original, string translated, string state, bool canCopy)
    {
        bool place = needsPlacement || !IsVisible;
        if (place) { anchor = location; manuallyMoved = false; }
        source.Text = original.Length > 450 ? original[..450] + "…" : original;
        result.Text = translated;
        status.Text = state;
        copy.IsEnabled = canCopy;
        if (!IsVisible) Show();
        if (place) { needsPlacement = false; Place(); }
    }

    internal void ResetPlacement() { needsPlacement = true; manuallyMoved = false; }

    private void Place()
    {
        if (!IsVisible) return;
        var work = Forms.Screen.FromPoint(anchor).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)(ActualWidth * dpi.DpiScaleX);
        int height = (int)(ActualHeight * dpi.DpiScaleY);
        int x = Math.Clamp(anchor.X + 16, work.Left, Math.Max(work.Left, work.Right - width));
        int y = anchor.Y + 20 + height > work.Bottom ? anchor.Y - height - 16 : anchor.Y + 20;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }

    internal void Shutdown() { shutdown = true; Close(); }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
