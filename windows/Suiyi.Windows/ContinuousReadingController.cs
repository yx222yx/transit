using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Suiyi.Windows;

internal sealed class ContinuousReadingController : IDisposable
{
    private readonly ReadingTarget target;
    private readonly ReadingSession session;
    private readonly OcrService ocr;
    private readonly ReadingOverlay overlay;
    private readonly ReadingControlBar bar;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private ReadingPanel? panel;
    private IReadOnlyList<OcrBlock> recognized = Array.Empty<OcrBlock>();
    private string lastTranslation = "";
    private Size? capturedWindowSize;
    private CancellationTokenSource? captureCancellation;
    private bool refreshRequested = true;
    private bool checkingGuard;
    private int captureCount;
    private bool paused;
    private bool original;
    private bool heldOriginal;
    private bool busy;
    private bool disposed;
    private string? suspended;
    private int captureVersion;
    internal event Action? ReselectRequested;
    internal event Action? Ended;
    internal event Action<string>? CorrectRequested;
    internal event Action<string>? StatusChanged;

    internal ContinuousReadingController(ReadingTarget target, TranslationService translator, OcrService ocr, ApiSettings settings)
    {
        this.target = target;
        this.ocr = ocr;
        session = new ReadingSession(translator, settings);
        overlay = new ReadingOverlay();
        bar = new ReadingControlBar();
        bar.PauseRequested += TogglePause;
        bar.OriginalRequested += ToggleOriginal;
        bar.ReselectRequested += () => ReselectRequested?.Invoke();
        bar.EndRequested += Dispose;
        bar.ReadRequested += ShowReading;
        bar.RefreshRequested += Refresh;
        session.Changed += OnChanged;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        try
        {
            bar.Show();
            bar.Place(target.CurrentBounds());
        }
        catch
        {
            timer.Stop();
            session.Changed -= OnChanged;
            session.Dispose();
            overlay.Dispose();
            bar.Close();
            throw;
        }
        timer.Start();
    }

    internal void TogglePause()
    {
        if (disposed) return;
        paused = !paused;
        if (paused)
        {
            refreshRequested = false;
            CancelCapture();
        }
        suspended = null;
        RefreshOverlay();
    }

    internal void ToggleOriginal()
    {
        if (disposed) return;
        original = !original;
        RefreshOverlay();
    }

    internal void Refresh()
    {
        if (disposed) return;
        CancelCapture();
        captureVersion++;
        paused = false;
        original = false;
        suspended = null;
        refreshRequested = true;
        capturedWindowSize = null;
        ClearContent("等待截取选区一次；请返回目标窗口。");
    }

    internal void ShowReading()
    {
        if (disposed) return;
        if (panel is null)
        {
            panel = new ReadingPanel();
            panel.Closed += (_, _) => panel = null;
            panel.CorrectRequested += text => { if (!paused) TogglePause(); CorrectRequested?.Invoke(text); };
        }
        panel.Present(string.Join("\n\n", recognized.Select(block => block.Text)), lastTranslation);
    }

    private void Tick()
    {
        if (disposed) return;
        string? unavailable = ReadingWindow.Unavailable(target, out bool closed);
        if (closed) { Dispose(); return; }
        bool held = ReadingWindow.KeyDown(0x11) && ReadingWindow.KeyDown(0x12) && ReadingWindow.KeyDown(0x4F); // Ctrl + Alt + O
        if (heldOriginal != held) { heldOriginal = held; RefreshOverlay(); }
        if (paused) { RefreshOverlay(); return; }
        if (unavailable is not null) { Suspend(unavailable); return; }
        if (capturedWindowSize is not null && ReadingWindow.Bounds(target.Handle).Size != capturedWindowSize)
        {
            CancelCapture();
            capturedWindowSize = null;
            ClearContent("窗口尺寸已改变，点击「刷新选区」重新截取。");
        }
        RefreshOverlay();
        if (!checkingGuard) _ = CheckGuardAsync();
    }

    // The timer checks window/input availability only. Screenshots require the initial
    // selection or an explicit refresh; returning to a window never queues a screenshot.
    private async Task CheckGuardAsync()
    {
        checkingGuard = true;
        int currentVersion = captureVersion;
        try
        {
            ReadingGuardResult guard = await ReadingGuard.ReadAsync(target.Handle, lifetime.Token);
            if (!Current(currentVersion)) return;
            if (!guard.Safe || guard.Editing)
            {
                Suspend(guard.Editing ? "输入控件正在编辑，已露出原文；离开输入框后显示已有译文。" : guard.Error ?? "无法确认输入保护状态，已隐藏覆盖。");
                return;
            }
            suspended = null;
            if (refreshRequested && !busy)
            {
                refreshRequested = false;
                _ = CaptureAsync(target.CurrentBounds(), guard);
            }
            RefreshOverlay();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && currentVersion == captureVersion) Suspend(error.Message);
        }
        finally { checkingGuard = false; }
    }

    private async Task CaptureAsync(Rectangle bounds, ReadingGuardResult guard)
    {
        busy = true;
        int currentVersion = captureVersion;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        captureCancellation = cancellation;
        try
        {
            using var image = ReadingWindow.Capture(bounds, guard.PasswordBounds);
            captureCount++;
            capturedWindowSize = ReadingWindow.Bounds(target.Handle).Size;
            session.Invalidate("已截取一次，正在本地识别英／俄文字…");
            var blocks = await ocr.RecognizeBlocksAsync(image, cancellation.Token);
            if (!Current(currentVersion)) { if (currentVersion == captureVersion) CancelCapture(); return; }
            guard = await ReadingGuard.ReadAsync(target.Handle, cancellation.Token);
            if (!Current(currentVersion)) { if (currentVersion == captureVersion) CancelCapture(); return; }
            if (!guard.Safe || guard.Editing)
            {
                Suspend("输入状态变化，已中断本次处理；点击「刷新选区」重试。");
                return;
            }
            recognized = blocks;
            panel?.UpdateContent(string.Join("\n\n", blocks.Select(block => block.Text)), "");
            await session.ObserveAsync(blocks);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && currentVersion == captureVersion) ClearContent(error.Message + " 可点击「刷新选区」或重新选择。");
        }
        finally
        {
            if (ReferenceEquals(captureCancellation, cancellation)) captureCancellation = null;
            busy = false;
            RefreshOverlay();
        }
    }

    private bool Current(int version) => !disposed && !paused && version == captureVersion &&
        ReadingWindow.Unavailable(target, out _) is null &&
        (capturedWindowSize is null || ReadingWindow.Bounds(target.Handle).Size == capturedWindowSize);

    private void Suspend(string status)
    {
        suspended = status;
        CancelCapture();
        RefreshOverlay();
    }

    private void CancelCapture()
    {
        if (captureCancellation is null) return;
        captureVersion++;
        captureCancellation.Cancel();
        captureCancellation = null;
        ClearContent("本次处理已中断，点击「刷新选区」重试。");
    }

    private void ClearContent(string status)
    {
        recognized = Array.Empty<OcrBlock>();
        lastTranslation = "";
        panel?.UpdateContent("", "");
        session.Invalidate(status);
    }

    private void OnChanged(ReadingSnapshot snapshot)
    {
        if (disposed) return;
        if (snapshot.Blocks.Count > 0)
        {
            lastTranslation = string.Join("\n\n", snapshot.Blocks.Select(block => block.Translation));
            panel?.UpdateContent(string.Join("\n\n", recognized.Select(block => block.Text)), lastTranslation);
        }
        RefreshOverlay();
    }

    private void RefreshOverlay()
    {
        if (disposed) return;
        string? unavailable = ReadingWindow.Unavailable(target, out _);
        string status = paused ? "已手动暂停；继续只显示已有译文，刷新才截取新内容。"
            : suspended ?? unavailable ?? (refreshRequested ? "等待截取选区一次；请返回目标窗口。" : session.Current.Status);
        if (refreshRequested && (suspended is not null || unavailable is not null)) status += " 本次刷新等待返回可阅读的目标窗口。";
        bar.Update(session.Current with { Status = status }, paused, original || heldOriginal, captureCount);
        StatusChanged?.Invoke(status + $" · 截图 {captureCount} 次 · 翻译请求 {session.Current.RequestCount} 次");
        if (paused || original || heldOriginal || suspended is not null || unavailable is not null) overlay.Hide();
        else overlay.Present(target.CurrentBounds(), session.Current.Blocks);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        captureVersion++;
        lifetime.Cancel();
        session.Changed -= OnChanged;
        session.Dispose();
        overlay.Dispose();
        bar.Close();
        panel?.Close();
        recognized = Array.Empty<OcrBlock>();
        lastTranslation = "";
        Ended?.Invoke();
    }
}
