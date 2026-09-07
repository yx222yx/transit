using System;
using System.Threading;
using System.Windows;

namespace Suiyi.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selection-probe") return SelectionProbe.Run(args);
        if (args.Length > 0 && args[0] == "--reading-guard") return ReadingGuard.Run(args);
        if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args);
        if (args.Length > 0 && args[0] == "--reading-self-test") return ReadingSessionSelfTest.RunAsync().GetAwaiter().GetResult();
        IsDevelopment = Array.IndexOf(args, "--dev") >= 0;
        using var singleInstance = new Mutex(true, IsDevelopment ? @"Local\Suiyi.Windows.Development" : @"Local\Suiyi.Windows.0.3", out bool created);
        if (!created)
        {
            MessageBox.Show("随译已经在运行，请从任务栏右侧的托盘图标打开。", "随译");
            return 0;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("操作未完成，请重试。若问题持续出现，请退出随译后重新打开。", "随译");
            e.Handled = true;
        };
        using var controller = new AppController(app);
        controller.Start();
        return app.Run();
    }
    internal static bool IsDevelopment { get; private set; }
}
