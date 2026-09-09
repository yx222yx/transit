using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;

namespace Suiyi.Windows;

internal sealed class AppController : IDisposable
{
    private readonly Application app;
    private readonly MainWindow main = new();
    private readonly TranslationPopup popup = new();
    private readonly TranslationService translator = new();
    private readonly OcrService ocr = new();
    private readonly GlobalInput input = new();
    private readonly Forms.NotifyIcon tray;
    private readonly Icon trayIcon;
    private readonly Forms.ToolStripMenuItem automaticMenu;
    private ContinuousReadingController? continuous;
    private ApiSettings settings = new();
    private CancellationTokenSource? pending;
    private CancellationTokenSource? testing;
    private int generation;
    private bool writingSource;
    private bool capturing;
    private bool disposed;
    private string lastSelection = "";
    private IntPtr lastWindow;
    private DateTime lastRequested;
    private string SelectionKey => input.GetHotkeyLabel(GlobalInput.TranslateSelectionHotkey);
    private string OcrKey => input.GetHotkeyLabel(GlobalInput.CaptureOcrHotkey);
    private string PauseKey => input.GetHotkeyLabel(GlobalInput.ToggleAutomaticHotkey);

    public AppController(Application app)
    {
        this.app = app;
        main.TranslateRequested += () => _ = TranslateManualAsync();
        main.CancelRequested += () => { if (!writingSource) Cancel(); };
        main.CopyRequested += () => Copy(main.Translation.Text);
        main.OcrRequested += () => _ = CaptureOcrAsync();
        main.SaveRequested += Apply;
        main.TestRequested += config => _ = TestAsync(config);
        main.ClearRequested += () => Apply(new ApiSettings());
        main.AutomaticChanged += SetAutomatic;
        main.ExitRequested += Exit;
        main.ContinuousRequested += whole => _ = StartContinuousAsync(whole);
        main.ContinuousPauseRequested += () => continuous?.TogglePause();
        main.ContinuousOriginalRequested += () => continuous?.ToggleOriginal();
        main.ContinuousReadRequested += () => continuous?.ShowReading();
        main.ContinuousRefreshRequested += () => continuous?.Refresh();
        main.ContinuousEndRequested += StopContinuous;
        popup.Dismissed += Cancel;
        popup.CopyRequested += Copy;
        popup.EditRequested += () => { popup.Hide(); main.ShowReading(); };
        input.HotkeyPressed += id =>
        {
            if (id == GlobalInput.TranslateSelectionHotkey) _ = CaptureSelectionAsync(Native.GetCursorPoint(), false);
            else if (id == GlobalInput.CaptureOcrHotkey) _ = CaptureOcrAsync();
            else if (id == GlobalInput.ToggleAutomaticHotkey) SetAutomatic(!input.AutoSelectionEnabled);
            else if (id == GlobalInput.StartContinuousHotkey) _ = StartContinuousAsync(false);
            else if (id == GlobalInput.PauseContinuousHotkey) continuous?.TogglePause();
            else if (id == GlobalInput.EndContinuousHotkey) StopContinuous();
        };
        input.SelectionReleased += point => { if (!capturing) _ = CaptureSelectionAsync(point, true); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开随译", null, (_, _) => main.ShowReading());
        menu.Items.Add("API 配置", null, (_, _) => main.ShowSettings());
        menu.Items.Add("框选屏幕文字  " + OcrKey, null, (_, _) => _ = CaptureOcrAsync());
        automaticMenu = new Forms.ToolStripMenuItem("开启自动划译  " + PauseKey);
        automaticMenu.Click += (_, _) => SetAutomatic(!input.AutoSelectionEnabled);
        menu.Items.Add(automaticMenu);
        menu.Items.Add("选区覆盖设置", null, (_, _) => main.ShowContinuous());
        menu.Items.Add("框选并覆盖翻译  " + input.GetHotkeyLabel(GlobalInput.StartContinuousHotkey), null, (_, _) => _ = StartContinuousAsync(false));
        menu.Items.Add("选择整窗口覆盖翻译", null, (_, _) => _ = StartContinuousAsync(true));
        menu.Items.Add("刷新选区 · 截取一次新内容", null, (_, _) => continuous?.Refresh());
        menu.Items.Add("暂停／继续覆盖翻译  " + input.GetHotkeyLabel(GlobalInput.PauseContinuousHotkey), null, (_, _) => continuous?.TogglePause());
        menu.Items.Add("覆盖翻译 · 原文／中文", null, (_, _) => continuous?.ToggleOriginal());
        menu.Items.Add("结束覆盖翻译  " + input.GetHotkeyLabel(GlobalInput.EndContinuousHotkey), null, (_, _) => StopContinuous());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出随译", null, (_, _) => Exit());
        trayIcon = AppIdentity.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Text = "随译 · 英／俄 → 中文 · Alpha", Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => main.ShowReading();
    }

    internal void Start()
    {
        main.Show();
        main.ShowSettings();
        main.ConfigureHotkeys(SelectionKey, OcrKey, PauseKey, string.Join(" ", input.RegistrationNotes));
        main.ContinuousStatus.Text = "选区覆盖未开启。框选开始 " + input.GetHotkeyLabel(GlobalInput.StartContinuousHotkey) + "；暂停／继续 " + input.GetHotkeyLabel(GlobalInput.PauseContinuousHotkey) + "；结束 " + input.GetHotkeyLabel(GlobalInput.EndContinuousHotkey) + "。";
        if (input.RegistrationErrors.Count > 0)
            main.Status.Text = string.Join("\n", input.RegistrationErrors);
    }

    internal void ShowMainWindow()
    {
        if (disposed) return;
        if (settings.ApiKey.Length == 0) main.ShowSettings();
        else main.ShowContinuous();
    }

    private void SetAutomatic(bool enabled)
    {
        if (enabled && settings.ApiKey.Length == 0)
        {
            main.Automatic.IsChecked = false;
            main.ShowSettings();
            main.SetApiMessage("请先配置并保存 API，再开启自动划译。", true);
            return;
        }
        try
        {
            input.AutoSelectionEnabled = enabled;
            if (main.Automatic.IsChecked != enabled) main.Automatic.IsChecked = enabled;
            automaticMenu.Checked = enabled;
            automaticMenu.Text = (enabled ? "暂停自动划译  " : "开启自动划译  ") + PauseKey;
            main.Status.Text = enabled ? "自动划译已开启：选中的文字会发送给 DeepSeek。" : "自动划译已暂停，仍可使用快捷键。";
            if (!enabled) { Cancel(); popup.Hide(); }
        }
        catch { main.Automatic.IsChecked = false; main.Status.Text = "无法开启划译监听，请使用快捷键翻译。"; }
    }

    private void Apply(ApiSettings config)
    {
        StopContinuous();
        Cancel();
        testing?.Cancel();
        settings = config;
        main.Translation.Clear();
        main.Applied(config);
        popup.Hide();
        if (config.ApiKey.Length == 0) SetAutomatic(false);
        main.Status.Text = config.ApiKey.Length == 0 ? "请配置 DeepSeek API。" : $"已准备好。选中文字后按 {SelectionKey}。";
    }

    private async Task StartContinuousAsync(bool wholeWindow)
    {
        if (capturing || disposed || !RequireKey()) return;
        StopContinuous();
        SetAutomatic(false);
        capturing = true;
        var job = Begin();
        popup.Hide();
        main.Hide();
        try
        {
            await Task.Delay(250, job.Token);
            var selection = await RegionSelector.SelectAsync(reading: true, wholeWindow);
            if (selection is null) { main.ShowContinuous(); return; }
            using (selection.Image)
            {
                if (disposed || job.Id != generation) return;
                var target = ReadingTarget.From(selection);
                continuous = new ContinuousReadingController(target, translator, ocr, settings);
                continuous.ReselectRequested += () => _ = StartContinuousAsync(false);
                continuous.CorrectRequested += text => { SetSource(text); main.ShowReading(); main.Status.Text = "选区覆盖已暂停。修正原文后点击翻译，可在此对照阅读。"; };
                continuous.StatusChanged += status => main.ContinuousStatus.Text = status;
                continuous.Ended += () => { continuous = null; main.ContinuousStatus.Text = "覆盖翻译已结束。"; };
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { main.ShowContinuous(); main.ContinuousStatus.Text = error.Message; }
        finally { capturing = false; }
    }

    private void StopContinuous()
    {
        continuous?.Dispose();
        continuous = null;
    }

    private async Task TestAsync(ApiSettings config)
    {
        testing?.Cancel();
        using var cancellation = new CancellationTokenSource();
        testing = cancellation;
        main.SetApiBusy(true);
        main.SetApiMessage("正在发送固定样例…");
        try
        {
            string result = await translator.TranslateAsync("A new day begins.", "en", config, cancellation.Token);
            if (!cancellation.IsCancellationRequested) main.SetApiMessage("连接成功：" + result);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!cancellation.IsCancellationRequested) main.SetApiMessage(e.Message, true); }
        finally { if (ReferenceEquals(testing, cancellation)) { testing = null; main.SetApiBusy(false); } }
    }

    private bool RequireKey()
    {
        if (settings.ApiKey.Length > 0) return true;
        main.ShowSettings();
        main.SetApiMessage("请填写 API Key 并保存后开始翻译。", true);
        return false;
    }

    private (int Id, CancellationToken Token) Begin()
    {
        Cancel();
        popup.ResetPlacement();
        pending = new CancellationTokenSource();
        return (generation, pending.Token);
    }

    private void Cancel()
    {
        generation++;
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        main.SetBusy(false);
    }

    private void SetSource(string text)
    {
        writingSource = true;
        try { main.Source.Text = text; }
        finally { writingSource = false; }
    }

    private async Task TranslateManualAsync()
    {
        if (!RequireKey()) return;
        string text = main.Source.Text.Trim();
        if (text.Length == 0) { main.Status.Text = "请先输入或粘贴英文、俄文。"; return; }
        var job = Begin();
        await TranslateAsync(text, "auto", Native.GetCursorPoint(), false, job.Id, job.Token);
    }

    private async Task CaptureSelectionAsync(Point point, bool automatic)
    {
        if (capturing || disposed) return;
        IntPtr foreground = Native.GetForegroundWindow();
        if (Native.GetWindowProcessId(foreground) == Environment.ProcessId) return;
        if (!RequireKey()) return;
        var job = Begin();
        try
        {
            await Task.Delay(automatic ? 180 : 50, job.Token);
            var selection = await SelectionCapture.ReadAsync(foreground, point, job.Token);
            if (job.Id != generation) return;
            if (!string.IsNullOrEmpty(selection.Error) || string.IsNullOrWhiteSpace(selection.Text))
            {
                if (!automatic) popup.Present(point, "", (selection.Error ?? "没有读取到选区。") + $"\n可使用 {OcrKey} 框译。", "取词未完成", false);
                return;
            }
            if (automatic && selection.Text == lastSelection && foreground == lastWindow
                && DateTime.UtcNow - lastRequested < TimeSpan.FromSeconds(3)
                && !string.IsNullOrWhiteSpace(main.Translation.Text))
            {
                popup.Present(point, selection.Text, main.Translation.Text, $"DeepSeek · {settings.Model}", true);
                return;
            }
            lastSelection = selection.Text;
            lastWindow = foreground;
            lastRequested = DateTime.UtcNow;
            SetSource(selection.Text);
            var anchor = new Point((int)(selection.X + Math.Min(selection.Width, 120)), (int)(selection.Y + selection.Height));
            await TranslateAsync(selection.Text, selection.Language, anchor, true, job.Id, job.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!automatic && job.Id == generation) popup.Present(point, "", $"读取失败，请使用 {OcrKey} 框选屏幕文字。", "取词未完成", false); }
    }

    private async Task CaptureOcrAsync()
    {
        if (capturing || disposed) return;
        bool restore = main.IsVisible;
        capturing = true;
        var job = Begin();
        popup.Hide();
        main.Hide();
        try
        {
            await Task.Delay(180, job.Token);
            var region = await RegionSelector.SelectAsync();
            if (region is null) { if (restore) main.ShowReading(); return; }
            using (region.Image)
            {
                if (job.Id != generation) return;
                var anchor = new Point(region.Bounds.Left, region.Bounds.Bottom);
                popup.Present(anchor, "", "正在本地识别英／俄文字…", "屏幕框译", false);
                string text = await ocr.RecognizeAsync(region.Image, job.Token);
                if (job.Id != generation) return;
                SetSource(text);
                if (settings.ApiKey.Length == 0)
                {
                    main.ShowReading();
                    main.Status.Text = "已识别文字。请先在 API 配置保存 Key，再翻译。";
                    popup.Hide();
                    return;
                }
                await TranslateAsync(text, "auto", anchor, true, job.Id, job.Token);
            }
        }
        catch (OperationCanceledException) { popup.Hide(); }
        catch (Exception e) { if (job.Id == generation) popup.Present(Native.GetCursorPoint(), "", e.Message, "框译未完成", false); }
        finally { capturing = false; }
    }

    private async Task TranslateAsync(string text, string language, Point anchor, bool showPopup, int id, CancellationToken token)
    {
        ApiSettings config = settings;
        main.SetBusy(true);
        main.Translation.Clear();
        main.Status.Text = $"{config.Model} 正在翻译…";
        if (showPopup) popup.Present(anchor, text, "正在翻译…", $"DeepSeek · {config.Model}", false);
        try
        {
            string result = await translator.TranslateAsync(text, language, config, token);
            if (id != generation) return;
            main.Translation.Text = result;
            main.Status.Text = $"翻译完成 · {config.Model}";
            if (showPopup) popup.Present(anchor, text, result, $"DeepSeek · {config.Model}", true);
        }
        catch (OperationCanceledException) { if (id == generation) main.Status.Text = "翻译已取消。"; }
        catch (Exception e)
        {
            if (id != generation) return;
            main.Status.Text = e.Message;
            if (showPopup) popup.Present(anchor, text, e.Message, "翻译未完成", false);
        }
        finally { if (id == generation) main.SetBusy(false); }
    }

    private void Copy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); main.Status.Text = "译文已复制。"; }
        catch { main.Status.Text = "剪贴板正忙，请稍后再试。"; }
    }

    private void Exit() { Dispose(); app.Shutdown(); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopContinuous();
        Cancel();
        testing?.Cancel();
        settings = new ApiSettings();
        input.Dispose();
        tray.Visible = false;
        tray.Dispose();
        trayIcon.Dispose();
        translator.Dispose();
        ocr.Dispose();
        popup.Shutdown();
        main.Exiting = true;
        main.Close();
    }
}
