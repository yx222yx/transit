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
    private Rectangle lastBounds;
    private ulong? signature;
    private ulong? textSignature;
    private DateTime stableSince;
    private DateTime changedSince;
    private bool contentDirty;
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
        bar.RetryRequested += Retry;
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
        Suspend(paused ? "已手动暂停 · 点击继续恢复" : "等待内容稳定…");
        if (!paused) suspended = null;
    }

    internal void ToggleOriginal()
    {
        if (disposed) return;
        original = !original;
        RefreshOverlay();
    }

    internal void Retry()
    {
        if (disposed) return;
        Suspend("等待重新识别…");
        suspended = null;
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
        if (paused) return;
        if (unavailable is not null) { Suspend(unavailable); return; }
        Rectangle bounds = target.CurrentBounds();
        if (bounds != lastBounds)
        {
            lastBounds = bounds;
            Suspend("窗口位置变化，等待内容稳定…");
        }
        if (suspended is not null) { suspended = null; signature = null; textSignature = null; contentDirty = false; }
        if (!busy) _ = CaptureAsync(bounds);
    }

    private async Task CaptureAsync(Rectangle bounds)
    {
        busy = true;
        int currentVersion = captureVersion;
        try
        {
            ReadingGuardResult guard = await ReadingGuard.ReadAsync(target.Handle, lifetime.Token);
            if (!Current(currentVersion, bounds)) return;
            if (!guard.Safe || guard.Editing)
            {
                Suspend(guard.Editing ? "输入控件正在编辑，已露出原文；离开输入框后恢复。" : guard.Error ?? "无法确认输入保护状态，已暂停。请重试。");
                return;
            }
            using var image = ReadingWindow.Capture(bounds, guard.PasswordBounds);
            ulong value = ReadingWindow.Signature(image);
            if (signature != value)
            {
                if (textSignature is not null && textSignature != ReadingWindow.Signature(image, recognized.Select(block => block.Bounds)))
                {
                    ClearContent("文字位置或内容变化，已露出原文…");
                    textSignature = null;
                }
                if (!contentDirty) changedSince = DateTime.UtcNow;
                contentDirty = true;
                signature = value;
                stableSince = DateTime.UtcNow;
            }
            if (!contentDirty || (DateTime.UtcNow - stableSince < TimeSpan.FromMilliseconds(650) &&
                DateTime.UtcNow - changedSince < TimeSpan.FromMilliseconds(1800))) return;
            contentDirty = false;
            if (recognized.Count == 0) session.Invalidate("正在本地识别英／俄文字…");
            var blocks = await ocr.RecognizeBlocksAsync(image, lifetime.Token);
            if (!Current(currentVersion, bounds)) return;
            // OCR may finish after a scroll. Compare a fresh protected frame before using positions.
            guard = await ReadingGuard.ReadAsync(target.Handle, lifetime.Token);
            if (!Current(currentVersion, bounds)) return;
            if (!guard.Safe || guard.Editing) { Suspend("输入状态变化，已露出原文。返回阅读后恢复。"); return; }
            using var fresh = ReadingWindow.Capture(bounds, guard.PasswordBounds);
            // Ignore image animation outside OCR text bounds. Only corresponding text must still match.
            if (ReadingWindow.Signature(fresh, blocks.Select(block => block.Bounds)) !=
                ReadingWindow.Signature(image, blocks.Select(block => block.Bounds)))
            {
                signature = null;
                textSignature = null;
                ClearContent("文字内容变化，等待停稳后翻译…");
                return;
            }
            if (!recognized.SequenceEqual(blocks))
            {
                lastTranslation = "";
                panel?.UpdateContent(string.Join("\n\n", blocks.Select(block => block.Text)), "");
            }
            recognized = blocks;
            signature = ReadingWindow.Signature(fresh);
            textSignature = ReadingWindow.Signature(fresh, blocks.Select(block => block.Bounds));
            _ = session.ObserveAsync(blocks);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && currentVersion == captureVersion) ClearContent(error.Message + " 可点重试或重新选择。");
        }
        finally { busy = false; }
    }

    private bool Current(int version, Rectangle bounds) => !disposed && !paused && version == captureVersion &&
        target.CurrentBounds() == bounds && ReadingWindow.Unavailable(target, out _) is null;

    private void Suspend(string status)
    {
        overlay.Hide();
        if (suspended == status) return;
        suspended = status;
        captureVersion++;
        signature = null;
        textSignature = null;
        contentDirty = false;
        session.Invalidate(status);
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
        StatusChanged?.Invoke(snapshot.Status + $" · 本次请求 {snapshot.RequestCount}");
    }

    private void RefreshOverlay()
    {
        if (disposed) return;
        bar.Update(session.Current, paused, original || heldOriginal);
        if (paused || original || heldOriginal || suspended is not null || ReadingWindow.Unavailable(target, out _) is not null) overlay.Hide();
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
