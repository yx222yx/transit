using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Suiyi.Windows;

internal sealed record OcrBlock(string Text, Rectangle Bounds);
internal sealed record TranslatedBlock(OcrBlock Source, string Translation);
internal sealed record ReadingSnapshot(IReadOnlyList<TranslatedBlock> Blocks, string Status, int RequestCount);

// The session boundary consumes local text/positions and exposes only current visible results.
internal sealed class ReadingSession : IDisposable
{
    internal event Action<ReadingSnapshot>? Changed;
    internal ReadingSnapshot Current { get; private set; } = new(Array.Empty<TranslatedBlock>(), "等待内容", 0);
    private readonly TranslationService translator;
    private readonly ApiSettings settings;
    private readonly Dictionary<string, string> cache = new(StringComparer.Ordinal);
    private CancellationTokenSource? pending;
    private string? fingerprint;
    private int version;
    private int requests;
    private bool disposed;

    internal ReadingSession(TranslationService translator, ApiSettings settings)
    {
        this.translator = translator;
        this.settings = settings;
    }

    internal async Task ObserveAsync(IReadOnlyList<OcrBlock> blocks)
    {
        if (disposed) return;
        string content = string.Join("\n", blocks.Select(block => $"{block.Bounds}:{block.Text}"));
        if (fingerprint == content) return;
        Invalidate("正在翻译，完成的文字将先显示…");
        fingerprint = content;
        int currentVersion = version;
        using var cancellation = new CancellationTokenSource();
        pending = cancellation;
        var visible = new List<TranslatedBlock>();
        try
        {
            foreach (var block in blocks)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Regex.IsMatch(block.Text, "[a-zA-Zа-яА-ЯёЁ]")) continue;
                if (!cache.TryGetValue(block.Text, out string? result))
                {
                    requests++;
                    Publish(visible, "正在翻译，完成的文字将先显示…");
                    result = await translator.TranslateAsync(block.Text, "auto", settings, cancellation.Token);
                    if (disposed || currentVersion != version) return;
                    // Never display a changed price, number or URL as if it were on the page.
                    if (!PreservesLiteralValues(block.Text, result))
                        throw new TranslationException("CHANGED_VALUES", "译文中的数字或网址与原文不一致，已保留原文。请重试或修正识别文字。");
                    if (cache.Count >= 500) cache.Clear();
                    cache[block.Text] = result;
                }
                if (disposed || currentVersion != version) return;
                visible.Add(new TranslatedBlock(block, result));
                Publish(visible, "正在逐块显示中文…");
            }
            if (currentVersion == version) Publish(visible, visible.Count == 0 ? "没有可翻译的英俄文字；请放大文字或重新选择。" : "持续翻译中 · 滚动后自动更新");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!disposed && currentVersion == version) Publish(visible, error.Message);
        }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; }
    }

    internal void Invalidate(string status)
    {
        version++;
        pending?.Cancel();
        pending = null;
        fingerprint = null;
        Publish(Array.Empty<TranslatedBlock>(), status);
    }

    private void Publish(IEnumerable<TranslatedBlock> blocks, string status)
    {
        Current = new ReadingSnapshot(blocks.ToArray(), status, requests);
        Changed?.Invoke(Current);
    }

    private static bool PreservesLiteralValues(string original, string translated)
    {
        const string pattern = @"https?://[^\s]+|\b(?=[A-Za-z0-9._-]*[A-Za-z])(?=[A-Za-z0-9._-]*\d)[A-Za-z0-9]+(?:[-_.][A-Za-z0-9]+)*\b|\d+(?:[.,]\d+)*|[₽$€£¥]";
        string Values(string text) => string.Join("|", Regex.Matches(text, pattern).Select(match => match.Value).OrderBy(value => value, StringComparer.Ordinal));
        return Values(original) == Values(translated);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Invalidate("持续翻译已结束");
        cache.Clear();
    }
}
