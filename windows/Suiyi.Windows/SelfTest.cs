using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingFont = System.Drawing.Font;

namespace Suiyi.Windows;

internal static class SelfTest
{
    internal static int Run(string[] args)
    {
        string reportPath = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "self-test.json");
        var checks = new List<object>();
        try
        {
            ServiceSelfTest.RunAsync().GetAwaiter().GetResult();
            checks.Add(new { name = "DeepSeek protocol and error fixtures", passed = true });

            using (var service = new OcrService())
            {
                foreach (var item in new[] { ("en", "A new day begins. Read the world.", "day"), ("ru", "Сегодня хороший день. Я читаю книгу.", "день") })
                {
                    using var bitmap = new Bitmap(1600, 190);
                    using (var graphics = Graphics.FromImage(bitmap))
                    using (var font = new DrawingFont("Arial", 38))
                    {
                        graphics.Clear(System.Drawing.Color.White);
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        graphics.DrawString(item.Item2, font, DrawingBrushes.Black, 30, 45);
                    }
                    string recognized = service.RecognizeAsync(bitmap, CancellationToken.None).GetAwaiter().GetResult();
                    if (!recognized.Contains(item.Item3, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"OCR {item.Item1} fixture failed: {recognized}");
                    checks.Add(new { name = "Local OCR " + item.Item1, passed = true, recognized });
                }
            }

            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var trayIcon = AppIdentity.CreateTrayIcon())
            {
                if (trayIcon.Width != 32 || trayIcon.Height != 32) throw new InvalidOperationException("Tray icon did not load at 32 px.");
                checks.Add(new { name = "Embedded tray icon", passed = true, width = trayIcon.Width, height = trayIcon.Height });
            }
            using (var hotkeys = new GlobalInput())
            {
                if (hotkeys.RegistrationErrors.Count > 0) throw new InvalidOperationException(string.Join("; ", hotkeys.RegistrationErrors));
                hotkeys.AutoSelectionEnabled = true;
                hotkeys.AutoSelectionEnabled = false;
                checks.Add(new { name = "Global hotkey registration and mouse hook lifecycle", passed = true, selection = hotkeys.GetHotkeyLabel(1), ocr = hotkeys.GetHotkeyLabel(2), pause = hotkeys.GetHotkeyLabel(3), notes = hotkeys.RegistrationNotes });
            }
            var window = new MainWindow();
            window.ConfigureHotkeys("Ctrl+Alt+T", "Ctrl+Alt+Q", "Ctrl+Alt+P", "");
            window.Source.Text = "Read the world, one selection at a time.\nЧитать мир — по одной фразе.";
            window.Translation.Text = "一次选中一段文字，读懂世界。";
            window.Status.Text = "本地界面渲染样例";
            var panel = (FrameworkElement)window.Content;
            panel.Measure(new System.Windows.Size(820, 720));
            panel.Arrange(new Rect(0, 0, 820, 720));
            panel.UpdateLayout();
            var preview = new RenderTargetBitmap(820, 720, 96, 96, PixelFormats.Pbgra32);
            preview.Render(panel);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(preview));
            string previewPath = Path.ChangeExtension(reportPath, ".png");
            using (var file = File.Create(previewPath)) png.Save(file);
            checks.Add(new { name = "WPF layout render", passed = true, previewPath });
            window.Exiting = true;
            window.Close();
            application.Shutdown();
            WriteReport(reportPath, new { passed = true, version = AppIdentity.Version, createdAtUtc = DateTimeOffset.UtcNow, checks });
            return 0;
        }
        catch (Exception e)
        {
            WriteReport(reportPath, new { passed = false, version = AppIdentity.Version, createdAtUtc = DateTimeOffset.UtcNow, checks, error = e.Message, type = e.GetType().Name });
            return 1;
        }
    }

    private static void WriteReport(string path, object report)
    {
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        stdout.WriteLine(json);
    }
}
