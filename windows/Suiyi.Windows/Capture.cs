using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.Point;

namespace Suiyi.Windows;

public sealed record CaptureResult(string Text, string Language, double X, double Y,
    double Width, double Height, string? Error = null);

public static class SelectionCapture
{
    public static async Task<CaptureResult> ReadAsync(IntPtr foreground, DrawingPoint mouse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureResult Failure(string message) => new("", "auto", mouse.X, mouse.Y, 0, 0, message);
        if (foreground == IntPtr.Zero) return Failure("没有可读取的前台窗口。");
        if (Native.GetWindowProcessId(foreground) == Environment.ProcessId)
            return Failure("请先在其他应用中选择文字。");

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return Failure("无法启动选区读取程序。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // This also supports development launches made with `dotnet Suiyi.Windows.dll`.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assembly)) return Failure("无法定位选区读取程序。");
            start.ArgumentList.Add(assembly);
        }
        start.ArgumentList.Add("--selection-probe");
        start.ArgumentList.Add(foreground.ToInt64().ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(mouse.X.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(mouse.Y.ToString(CultureInfo.InvariantCulture));

        using var child = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
        try
        {
            if (!child.Start()) return Failure("无法启动选区读取程序。");
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> diagnostics = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string json = (await output.ConfigureAwait(false)).Trim();
            await diagnostics.ConfigureAwait(false); // Drain redirected stderr without logging captured text.
            if (Native.GetForegroundWindow() != foreground)
                return Failure("阅读窗口已切换，请重新选择文字。");
            if (json.Length == 0 || json.Length > 100_000)
                return Failure("选区读取程序未返回有效结果，请尝试截图翻译。");
            CaptureResult? result = JsonSerializer.Deserialize<CaptureResult>(json);
            if (result is null) return Failure("无法解析选区读取结果。");
            if (string.IsNullOrWhiteSpace(result.Text) && string.IsNullOrWhiteSpace(result.Error))
                return Failure("未读取到选中文字，请重新选择或使用截图翻译。");
            return result;
        }
        catch (OperationCanceledException)
        {
            KillOwnChild(child);
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("该应用读取选区超时，请尝试截图翻译。");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or JsonException)
        {
            KillOwnChild(child);
            return Failure("无法读取该应用的选区，请尝试截图翻译。");
        }
    }

    private static void KillOwnChild(Process child)
    {
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>Construct and dispose on the WPF Dispatcher thread.</summary>
public sealed class GlobalInput : IDisposable
{
    public const int TranslateSelectionHotkey = 1;
    public const int CaptureOcrHotkey = 2;
    public const int ToggleAutomaticHotkey = 3;
    private const int WmHotkey = 0x0312;
    private const int WmLeftDown = 0x0201;
    private const int WmLeftUp = 0x0202;
    private const int WhMouseLl = 14;
    private const uint ModControlAltNoRepeat = 0x0002 | 0x0001 | 0x4000;
    private readonly Dispatcher dispatcher;
    private readonly HwndSource source;
    private readonly HookProcedure hookCallback;
    private readonly List<int> registeredHotkeys = new();
    private readonly List<string> registrationErrors = new();
    private readonly List<string> registrationNotes = new();
    private readonly Dictionary<int, string> hotkeyLabels = new();
    private IntPtr mouseHook;
    private DrawingPoint? dragStart;
    private bool enabled;
    private bool disposed;

    public event Action<int>? HotkeyPressed;
    public event Action<DrawingPoint>? SelectionReleased;
    public IReadOnlyList<string> RegistrationErrors => registrationErrors;
    public IReadOnlyList<string> RegistrationNotes => registrationNotes;
    public string GetHotkeyLabel(int id) => !disposed && hotkeyLabels.TryGetValue(id, out string? label)
        ? label : "未注册";

    public bool AutoSelectionEnabled
    {
        get => enabled;
        set
        {
            dispatcher.VerifyAccess();
            if (disposed) throw new ObjectDisposedException(nameof(GlobalInput));
            dragStart = null;
            if (enabled == value) return;
            if (value)
            {
                mouseHook = SetWindowsHookEx(WhMouseLl, hookCallback, GetModuleHandle(null), 0);
                if (mouseHook == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法启用自动划译鼠标监听。");
                enabled = true;
            }
            else
            {
                enabled = false;
                if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
        }
    }

    public GlobalInput()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        source = new HwndSource(new HwndSourceParameters("SuiyiInputMessages")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE; never appears on screen or takes focus.
        });
        source.AddHook(WindowMessage);
        hookCallback = MouseMessage;
        Register(TranslateSelectionHotkey, 0x54, "Ctrl+Alt+T");
        Register(CaptureOcrHotkey, 0x51, "Ctrl+Alt+Q");
        Register(ToggleAutomaticHotkey, 0x50, "Ctrl+Alt+P");
    }

    private void Register(int id, uint key, string label)
    {
        uint functionKey = id switch
        {
            TranslateSelectionHotkey => 0x77, // F8
            CaptureOcrHotkey => 0x78, // F9
            ToggleAutomaticHotkey => 0x79, // F10
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        string shiftLabel = label.Replace("Ctrl+Alt+", "Ctrl+Alt+Shift+", StringComparison.Ordinal);
        string functionLabel = $"Ctrl+Alt+F{functionKey - 0x70 + 1}";
        var choices = new (uint Modifiers, uint Key, string Label)[]
        {
            (ModControlAltNoRepeat, key, label),
            (ModControlAltNoRepeat | 0x0004, key, shiftLabel),
            (ModControlAltNoRepeat, functionKey, functionLabel)
        };
        foreach (var choice in choices)
        {
            if (!RegisterHotKey(source.Handle, id, choice.Modifiers, choice.Key)) continue;
            registeredHotkeys.Add(id);
            hotkeyLabels.Add(id, choice.Label);
            if (choice.Label != label)
                registrationNotes.Add($"默认快捷键 {label} 不可用，已自动改为 {choice.Label}。");
            return;
        }
        registrationErrors.Add($"{label}、{shiftLabel} 和 {functionLabel} 均注册失败，请使用托盘菜单操作。");
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && !disposed)
        {
            int id = wParam.ToInt32();
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (!disposed) HotkeyPressed?.Invoke(id);
            }));
            handled = true;
        }
        return IntPtr.Zero;
    }

    private IntPtr MouseMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        // Never read UIA, perform I/O, or translate inside this global hook.
        try
        {
            if (code >= 0 && enabled && !disposed)
            {
                int message = wParam.ToInt32();
                if (message is WmLeftDown or WmLeftUp)
                {
                    var data = Marshal.PtrToStructure<MouseHookData>(lParam);
                    if ((data.Flags & 1) == 0) // Ignore injected mouse input.
                    {
                        var point = new DrawingPoint(data.Point.X, data.Point.Y);
                        if (message == WmLeftDown) dragStart = point;
                        else
                        {
                            DrawingPoint? start = dragStart;
                            dragStart = null;
                            if (start is { } origin && (Math.Abs(point.X - origin.X) >= 4 || Math.Abs(point.Y - origin.Y) >= 4))
                                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                                {
                                    if (!disposed && enabled) SelectionReleased?.Invoke(point);
                                }));
                        }
                    }
                }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.SEHException)
        {
            // Input must continue even while the dispatcher is shutting down.
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        enabled = false;
        disposed = true;
        dragStart = null;
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        mouseHook = IntPtr.Zero;
        foreach (int id in registeredHotkeys) UnregisterHotKey(source.Handle, id);
        source.RemoveHook(WindowMessage);
        source.Dispose();
        GC.KeepAlive(hookCallback);
    }

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct HookPoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData
    {
        public HookPoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, HookProcedure callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? module);
}

public static class Native
{
    public static IntPtr GetForegroundWindow() => GetForegroundWindowNative();
    public static DrawingPoint GetCursorPoint()
    {
        GetCursorPos(out var point);
        return point;
    }
    public static int GetWindowProcessId(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        return unchecked((int)processId);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out DrawingPoint point);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
