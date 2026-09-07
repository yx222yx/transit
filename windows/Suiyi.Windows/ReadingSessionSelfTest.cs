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
