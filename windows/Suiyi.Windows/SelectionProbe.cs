using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Suiyi.Windows;

public static class SelectionProbe
{
    private const int MaxSelectedCharacters = 8000;

    /// <summary>Run before starting WPF. Writes exactly one JSON result to redirected stdout.</summary>
    public static int Run(string[] args)
    {
        double x = 0, y = 0;
        CaptureResult Failure(string message) => new("", "auto", double.IsFinite(x) ? x : 0,
            double.IsFinite(y) ? y : 0, 0, 0, message);
        CaptureResult result;
        if (args.Length != 4 || args[0] != "--selection-probe"
            || !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long handle)
            || !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            || !double.IsFinite(x) || !double.IsFinite(y) || handle == 0)
        {
            result = Failure("选区读取参数无效。");
        }
        else
        {
            CaptureResult? captured = null;
            var worker = new Thread(() =>
            {
                try { captured = ReadSelection(new IntPtr(handle), x, y); }
                catch (Exception) { captured = Failure("该应用没有提供可读取的选区，请尝试截图翻译。"); }
            }) { IsBackground = true, Name = "SuiyiSelectionProbe" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            result = worker.Join(1200) && captured is not null
                ? captured : Failure("读取选区超时，请尝试截图翻译。");
        }

        // Explicitly open the inherited stdout handle for the WinExe helper process.
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        output.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }

    private static CaptureResult ReadSelection(IntPtr foreground, double x, double y)
    {
        CaptureResult Failure(string message) => new("", "auto", x, y, 0, 0, message);
        int targetProcess = Native.GetWindowProcessId(foreground);
        if (targetProcess == 0 || targetProcess == Environment.ProcessId)
            return Failure("请先在其他应用中选择文字。");
        if (Native.GetForegroundWindow() != foreground)
            return Failure("阅读窗口已切换，请重新选择文字。");

        var root = AutomationElement.FromHandle(foreground);
        if (root is null) return Failure("无法访问该窗口的选区，请尝试截图翻译。");
        var candidates = new List<AutomationElement>();
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                if (focused.Current.IsPassword) return Failure("密码控件不支持翻译。");
                AddWithinWindow(focused, root, candidates);
            }
        }
        catch (ElementNotAvailableException) { }
        try
        {
            AutomationElement? underMouse = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (underMouse is not null)
            {
                if (underMouse.Current.IsPassword) return Failure("密码控件不支持翻译。");
                AddWithinWindow(underMouse, root, candidates);
            }
        }
        catch (ElementNotAvailableException) { }

        foreach (AutomationElement candidate in candidates)
        {
            try
            {
                if (candidate.Current.IsPassword) continue;
                if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject)) continue;
                var pattern = (TextPattern)patternObject;
                if (pattern.SupportedTextSelection == SupportedTextSelection.None) continue;
                TextPatternRange[] ranges = pattern.GetSelection();
                if (ranges is null || ranges.Length == 0) continue;
                if (ranges.Length > 16) return Failure("选区过多，请选择一段连续文字再试。");

                var pieces = new List<string>();
                var rectangles = new List<(double X, double Y, double Width, double Height)>();
                int characters = 0;
                foreach (TextPatternRange range in ranges)
                {
                    // Only inspect selected ranges. Never request DocumentRange or copy the control value.
                    string rawText = range.GetText(MaxSelectedCharacters + 1);
                    if (rawText.Length > MaxSelectedCharacters)
                        return Failure($"选中文字过长，请缩小选区（最多 {MaxSelectedCharacters} 个字符）。");
                    string text = rawText.Trim();
                    if (text.Length == 0) continue;
                    characters += text.Length;
                    if (characters > MaxSelectedCharacters)
                        return Failure($"选中文字过长，请缩小选区（最多 {MaxSelectedCharacters} 个字符）。");
                    pieces.Add(text);
                    System.Windows.Rect[] bounds = range.GetBoundingRectangles();
                    foreach (var rectangle in bounds)
                    {
                        double left = rectangle.Left, top = rectangle.Top;
                        double width = rectangle.Width, height = rectangle.Height;
                        if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width)
                            && double.IsFinite(height) && width > 0 && height > 0)
                            rectangles.Add((left, top, width, height));
                    }
                }
                if (pieces.Count == 0) continue;
                if (Native.GetForegroundWindow() != foreground)
                    return Failure("阅读窗口已切换，请重新选择文字。");
                string selectedText = string.Join("\n\n", pieces);
                string language = selectedText.Any(c => c is >= '\u0400' and <= '\u04ff') ? "ru" : "en";
                if (rectangles.Count == 0) return new(selectedText, language, x, y, 0, 0);
                var anchor = rectangles.MinBy(rectangle => DistanceSquared(rectangle, x, y));
                return new(selectedText, language, anchor.X, anchor.Y, anchor.Width, anchor.Height);
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException
                or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                // A provider may disappear or reject a pattern. Try the next candidate in this window.
            }
        }
        return Failure("未读取到选中文字；该应用可能不支持划译，请重新选择或使用截图翻译。");
    }

    private static void AddWithinWindow(AutomationElement element, AutomationElement root,
        List<AutomationElement> candidates)
    {
        var ancestors = new List<AutomationElement>();
        AutomationElement? current = element;
        // Walk a bounded ancestor chain; never enumerate the desktop or unrelated applications.
        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            if (current.Current.IsPassword) return;
            ancestors.Add(current);
            if (Automation.Compare(current, root))
            {
                foreach (AutomationElement ancestor in ancestors.Take(8).Concat(new[] { root }))
                    if (!candidates.Any(candidate => Automation.Compare(candidate, ancestor))) candidates.Add(ancestor);
                return;
            }
            current = TreeWalker.RawViewWalker.GetParent(current);
        }
    }

    private static double DistanceSquared((double X, double Y, double Width, double Height) rectangle,
        double x, double y)
    {
        double dx = Math.Max(rectangle.X - x, Math.Max(0, x - rectangle.X - rectangle.Width));
        double dy = Math.Max(rectangle.Y - y, Math.Max(0, y - rectangle.Y - rectangle.Height));
        return dx * dx + dy * dy;
    }
}
