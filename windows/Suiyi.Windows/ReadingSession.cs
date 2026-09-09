using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Suiyi.Windows;

internal sealed record OcrBlock(string Text, Rectangle Bounds, float Confidence = 100)
{
    internal bool IsReliable => float.IsFinite(Confidence) && Confidence is >= 60 and <= 100;
}
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
        string content = string.Join("\n", blocks.Select(block => $"{block.Bounds}:{block.IsReliable}:{block.Text}"));
        if (fingerprint == content) return;
        Invalidate("正在翻译，完成的文字将先显示…");
        fingerprint = content;
        int currentVersion = version;
        using var cancellation = new CancellationTokenSource();
        pending = cancellation;
        var visible = new List<TranslatedBlock>();
        int uncertain = 0;
        int failed = 0;
        string? blockFailure = null;
        try
        {
            foreach (var block in blocks)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Regex.IsMatch(block.Text, "[a-zA-Zа-яА-ЯёЁ]")) continue;
                if (!block.IsReliable) { uncertain++; continue; }
                try
                {
                    if (!cache.TryGetValue(block.Text, out string? result))
                    {
                        requests++;
                        Publish(visible, "正在翻译，完成的文字将先显示…");
                        result = await translator.TranslateAsync(block.Text, "auto", settings, cancellation.Token);
                        if (disposed || currentVersion != version) return;
                        // Never display changed literal facts or existing Chinese as if they were on the page.
                        if (!PreservesLiteralValues(block.Text, result))
                            throw new TranslationException("CHANGED_VALUES", "译文改动了原有数字、型号、网址或中文，已保留这一块原文。");
                        if (cache.Count >= 500) cache.Clear();
                        cache[block.Text] = result;
                    }
                    if (disposed || currentVersion != version) return;
                    visible.Add(new TranslatedBlock(block, result));
                    Publish(visible, "正在逐块显示中文…");
                }
                catch (TranslationException error) when (error.Code is "CHANGED_VALUES" or "EMPTY_TRANSLATION" or
                    "TRANSLATION_REFUSED" or "TRANSLATION_TRUNCATED" or "INVALID_RESPONSE")
                {
                    if (disposed || currentVersion != version) return;
                    failed++;
                    blockFailure ??= error.Message;
                }
            }
            if (currentVersion == version)
                Publish(visible, uncertain + failed > 0
                    ? $"已显示 {visible.Count} 块中文；{uncertain} 块识别不清、{failed} 块翻译未完成，已保留原文。可刷新选区或在完整译文中修正。" + blockFailure
                    : visible.Count == 0 ? "没有可翻译的英俄文字；请放大后刷新选区。" : "本次截图翻译完成 · 点击「刷新选区」更新");
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
        return Values(original) == Values(translated) && Regex.Matches(original, @"[\u3400-\u4dbf\u4e00-\u9fff]+")
            .All(match => translated.Contains(match.Value, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Invalidate("覆盖翻译已结束");
        cache.Clear();
    }
}
