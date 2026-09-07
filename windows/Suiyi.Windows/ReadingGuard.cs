using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Suiyi.Windows;

internal sealed record ReadingGuardResult(bool Safe, bool Editing, Rectangle[] PasswordBounds,
    string? Error = null);

/// <summary>Inspect input metadata in an isolated process; never read a control's text or value.</summary>
internal static class ReadingGuard
{
    private const int MaximumPasswordControls = 512;

    public static int Run(string[] args)
    {
        ReadingGuardResult result;
        if (args.Length != 2 || args[0] != "--reading-guard"
            || !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long handle)
            || handle == 0)
        {
            result = Failure("阅读保护参数无效，已暂停截图。");
        }
        else
        {
            ReadingGuardResult? captured = null;
            var worker = new Thread(() =>
            {
                try { captured = Inspect(new IntPtr(handle)); }
                catch (Exception) { captured = Failure("无法完整检查该窗口的输入保护，已暂停截图。"); }
            }) { IsBackground = true, Name = "SuiyiReadingGuard" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            result = worker.Join(1200) && captured is not null
                ? captured : Failure("检查窗口输入保护超时，已暂停截图。");
        }

        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        { AutoFlush = true };
        output.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }

    public static async Task<ReadingGuardResult> ReadAsync(IntPtr target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int processId = Native.GetWindowProcessId(target);
        if (!IsCurrentTarget(target, processId) || processId == Environment.ProcessId)
            return Failure("阅读窗口不可用或已切换，已暂停截图。");

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return Failure("无法启动阅读保护程序，已暂停截图。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assembly)) return Failure("无法定位阅读保护程序，已暂停截图。");
            start.ArgumentList.Add(assembly);
        }
        start.ArgumentList.Add("--reading-guard");
        start.ArgumentList.Add(target.ToInt64().ToString(CultureInfo.InvariantCulture));

        using var child = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(1500);
        bool started = false;
        try
        {
            started = child.Start();
            if (!started) return Failure("无法启动阅读保护程序，已暂停截图。");
            Task<string> output = child.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> diagnostics = child.StandardError.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string json = (await output.ConfigureAwait(false)).Trim();
            await diagnostics.ConfigureAwait(false); // Drain only; never log provider diagnostics.
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentTarget(target, processId))
                return Failure("阅读窗口已切换，已暂停截图。");
            if (child.ExitCode != 0 || json.Length == 0 || json.Length > 250_000)
                return Failure("阅读保护程序未返回有效结果，已暂停截图。");
            ReadingGuardResult? result = JsonSerializer.Deserialize<ReadingGuardResult>(json);
            if (result is null || result.PasswordBounds is null
                || result.PasswordBounds.Length > MaximumPasswordControls)
                return Failure("无法解析阅读保护结果，已暂停截图。");
            if (!result.Safe) return Failure(result.Error ?? "无法确认窗口输入保护，已暂停截图。");
            if (!string.IsNullOrEmpty(result.Error)) return Failure("阅读保护结果不完整，已暂停截图。");
            foreach (Rectangle bounds in result.PasswordBounds)
                if (bounds.Width <= 0 || bounds.Height <= 0
                    || (long)bounds.X + bounds.Width > int.MaxValue
                    || (long)bounds.Y + bounds.Height > int.MaxValue)
                    return Failure("密码控件位置无效，已暂停截图。");
            return result;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("检查窗口输入保护超时，已暂停截图。");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or JsonException or UnauthorizedAccessException)
        {
            return Failure("无法检查该窗口的输入保护，已暂停截图。");
        }
        finally
        {
            // Every exit path, including caller cancellation, terminates only our own helper.
            if (started) KillOwnChild(child);
        }
    }

    private static ReadingGuardResult Inspect(IntPtr target)
    {
        int processId = Native.GetWindowProcessId(target);
        if (!IsCurrentTarget(target, processId))
            return Failure("阅读窗口不可用或已切换，已暂停截图。");
        AutomationElement? root = AutomationElement.FromHandle(target);
        if (root is null) return Failure("无法访问阅读窗口的输入保护，已暂停截图。");

        // FindAll must finish successfully for the complete target subtree. A timeout,
        // disappearing element or provider exception fails the entire guard, never an empty mask.
        AutomationElementCollection passwords = root.FindAll(TreeScope.Subtree,
            new PropertyCondition(AutomationElement.IsPasswordProperty, true));
        if (passwords.Count > MaximumPasswordControls)
            return Failure("该窗口的密码控件过多，已暂停截图。");
        var masks = new List<Rectangle>();
        foreach (AutomationElement password in passwords)
        {
            // Recheck the property because the UI may have changed after FindAll.
            if (!password.Current.IsPassword)
                return Failure("窗口输入控件正在变化，已暂停截图。");
            System.Windows.Rect bounds = password.Current.BoundingRectangle;
            if (password.Current.IsOffscreen) continue;
            if (!TryGetMask(bounds, out Rectangle mask))
                return Failure("无法确定密码控件的位置，已暂停截图。");
            masks.Add(mask);
        }

        bool editing = false;
        AutomationElement? focused = AutomationElement.FocusedElement;
        if (focused is not null && focused.Current.ProcessId == processId)
        {
            bool password = focused.Current.IsPassword;
            editing = password || focused.Current.ControlType == ControlType.Edit;
            if (!editing && focused.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                editing = !((ValuePattern)pattern).Current.IsReadOnly;
            if (password)
            {
                // Focus may have changed since the subtree scan; include its current bounds too.
                if (!TryGetMask(focused.Current.BoundingRectangle, out Rectangle mask))
                    return Failure("无法确定当前密码输入框的位置，已暂停截图。");
                if (!masks.Contains(mask)) masks.Add(mask);
            }
        }

        if (!IsCurrentTarget(target, processId))
            return Failure("阅读窗口已切换，已暂停截图。");
        return new ReadingGuardResult(true, editing, masks.ToArray());
    }

    private static bool TryGetMask(System.Windows.Rect bounds, out Rectangle mask)
    {
        mask = Rectangle.Empty;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)
            || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)
            || bounds.Width <= 0 || bounds.Height <= 0) return false;
        // Round outwards so fractional DPI coordinates cannot leave an uncovered edge.
        double left = Math.Floor(bounds.Left), top = Math.Floor(bounds.Top);
        double right = Math.Ceiling(bounds.Right), bottom = Math.Ceiling(bounds.Bottom);
        if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
            || right - left > int.MaxValue || bottom - top > int.MaxValue) return false;
        mask = Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
        return true;
    }

    private static bool IsCurrentTarget(IntPtr target, int processId) => target != IntPtr.Zero
        && processId != 0 && IsWindow(target) && Native.GetWindowProcessId(target) == processId
        && Native.GetForegroundWindow() == target;

    private static ReadingGuardResult Failure(string message) => new(false, false, Array.Empty<Rectangle>(), message);

    private static void KillOwnChild(Process child)
    {
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
}
