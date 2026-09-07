using System;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Suiyi.Windows;

internal static class ReadingSessionSelfTest
{
    internal static async Task<int> RunAsync()
    {
        try
        {
            var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requests = 0;
            using var service = new TranslationService(new FixtureHandler(() =>
                Interlocked.Increment(ref requests) == 1 ? delayed.Task : Task.FromResult(Reply("新商品"))));
            using var session = new ReadingSession(service, new ApiSettings(ApiKey: "fixture-only"));
            var oldBlocks = new[] { new OcrBlock("Old product", new Rectangle(8, 8, 100, 22)) };
            var newBlocks = new[] { new OcrBlock("New product", new Rectangle(8, 8, 100, 22)) };
            Task oldRequest = session.ObserveAsync(oldBlocks);
            session.Invalidate("内容变化");
            await session.ObserveAsync(newBlocks);
            delayed.SetResult(Reply("旧商品"));
            await oldRequest;
            await session.ObserveAsync(newBlocks);
            if (session.Current.Blocks.Count != 1 || session.Current.Blocks[0].Translation != "新商品" || requests != 2)
                throw new InvalidOperationException("内容变化后只呈现新商品；不变内容不重复请求。");
            session.Invalidate("已暂停");
            if (session.Current.Blocks.Count != 0) throw new InvalidOperationException("暂停后应立即恢复原文。");
            Console.WriteLine("PASS: reading session stale response, unchanged content, and pause visibility");

            int partialRequests = 0;
            using var partialService = new TranslationService(new FixtureHandler(() => Task.FromResult(
                Reply(Interlocked.Increment(ref partialRequests) == 1 ? "CD-123" : "清晰商品"))));
            using var partialSession = new ReadingSession(partialService, new ApiSettings(ApiKey: "fixture-only"));
            await partialSession.ObserveAsync(new[]
            {
                new OcrBlock("Unclear label", new Rectangle(8, 8, 100, 22), 20),
                new OcrBlock("AB-123", new Rectangle(8, 38, 100, 22)),
                new OcrBlock("Clear product", new Rectangle(8, 68, 100, 22)),
                new OcrBlock("已经是中文", new Rectangle(8, 98, 100, 22))
            });
            if (partialRequests != 2 || partialSession.Current.Blocks.Count != 1 ||
                partialSession.Current.Blocks[0].Translation != "清晰商品")
                throw new InvalidOperationException("模糊文字和纯中文不请求；型号改写保留原文，后续清晰块继续翻译。");

            using var changedChineseService = new TranslationService(new FixtureHandler(() => Task.FromResult(Reply("商标已改写，英文"))));
            using var mixedSession = new ReadingSession(changedChineseService, new ApiSettings(ApiKey: "fixture-only"));
            await mixedSession.ObserveAsync(new[] { new OcrBlock("已有中文 English", new Rectangle(8, 8, 180, 22)) });
            if (mixedSession.Current.Blocks.Count != 0)
                throw new InvalidOperationException("混合文字中的已有中文被改写时，应保留原文。");
            Console.WriteLine("PASS: uncertain OCR, partial block failure, and existing Chinese protection");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"" + text + "\"}}]}", Encoding.UTF8, "application/json")
    };
    private sealed class FixtureHandler(Func<Task<HttpResponseMessage>> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => next();
    }
}
