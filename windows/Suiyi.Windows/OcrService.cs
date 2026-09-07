using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace Suiyi.Windows;

public sealed class OcrService : IDisposable
{
    private int _disposed;

    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken token)
    {
        var blocks = await RecognizeBlocksAsync(bitmap, token).ConfigureAwait(false);
        return string.Join("\n", blocks.Select(block => block.Text));
    }

    internal async Task<IReadOnlyList<OcrBlock>> RecognizeBlocksAsync(Bitmap bitmap, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        token.ThrowIfCancellationRequested();
        if (bitmap is null) throw new InvalidOperationException("未取得截图，请重新框选屏幕区域。");
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "tessdata");
        foreach (string language in new[] { "eng", "rus" })
        {
            if (!File.Exists(Path.Combine(dataDirectory, language + ".traineddata")))
                throw new InvalidOperationException("缺少本地 OCR 语言包。请重新解压完整程序包，确认 tessdata 文件夹内同时存在 eng.traineddata 和 rus.traineddata。");
        }

        byte[] bytes;
        try
        {
            // Copy before the first await so callers can release their bitmap after this call starts.
            // PNG encoding stays in memory; no screenshot or image file is sent to an API.
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            bytes = memory.ToArray();
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or ObjectDisposedException)
        {
            throw new InvalidOperationException("无法读取这张截图，请重新框选屏幕区域。");
        }

        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            try
            {
                // Each operation owns its engine, page and Pix, so parallel or cancelled calls
                // never dispose an engine while a different call is using it.
                using var engine = new TesseractEngine(dataDirectory, "eng+rus", EngineMode.LstmOnly);
                using var pix = Pix.LoadFromMemory(bytes);
                token.ThrowIfCancellationRequested();
                using var page = engine.Process(pix, PageSegMode.Auto);
                var blocks = new List<OcrBlock>();
                using (var iterator = page.GetIterator())
                {
                    iterator.Begin();
                    do
                    {
                        string text = iterator.GetText(PageIteratorLevel.Para)?.Trim() ?? "";
                        if (text.Length > 0 && iterator.TryGetBoundingBox(PageIteratorLevel.Para, out var bounds))
                            blocks.Add(new OcrBlock(text, new Rectangle(bounds.X1, bounds.Y1, bounds.Width, bounds.Height)));
                    } while (iterator.Next(PageIteratorLevel.Para));
                }
                // Native recognition is synchronous; cancellation discards its eventual result.
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (blocks.Count == 0)
                    throw new InvalidOperationException("没有识别到英文或俄文。请框选更清晰的文字区域，或先放大原文。");
                return (IReadOnlyList<OcrBlock>)blocks;
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or TypeInitializationException or FileLoadException)
            {
                throw new InvalidOperationException("无法加载本地 OCR 运行库。请使用完整的 Windows x64 程序包，并安装 Microsoft Visual C++ 2015–2022 x64 运行库后重试。");
            }
            catch (TesseractException)
            {
                throw new InvalidOperationException("本地 OCR 引擎初始化或识别失败。请确认英俄语言包完整、x64 运行库可用，再重新框选。");
            }
            catch (IOException)
            {
                throw new InvalidOperationException("本地 OCR 无法读取图片或语言数据，请重新框选并检查语言包。");
            }
        }, token).ConfigureAwait(false);
    }

    // Native resources are owned and disposed by each call; disposal blocks future work/results.
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
