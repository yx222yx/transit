using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Suiyi.Windows;

internal sealed class MainWindow : Window
{
    internal readonly TextBox Source = TextArea(false, 130);
    internal readonly TextBox Translation = TextArea(true, 160);
    internal readonly TextBlock Status = Label("配置 DeepSeek 后，即可翻译英／俄文本。", 13);
    internal readonly TextBlock Engine = Label("尚未配置 API", 13);
    internal readonly CheckBox Automatic = new() { Content = "自动划译：拖动选中文字后翻译", Margin = new Thickness(0, 14, 0, 8) };
    internal readonly TextBlock ContinuousStatus = Label("持续覆盖翻译未开启。", 13);
    private readonly TabControl tabs = new();
    private readonly TextBlock shortcuts = Label("", 12);
    private readonly TextBlock helpSteps = Label("", 14, false, 16);
    private readonly TextBox apiBase = Field("https://api.deepseek.com");
    private readonly TextBox apiModel = Field("deepseek-v4-flash");
    private readonly PasswordBox apiKey = new() { Padding = new Thickness(10), FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox apiTimeout = Field("30");
    private readonly TextBlock apiMessage = Label("Key 只在本次运行中保留，退出后清空。", 13);
    private readonly Button translate = Button("翻译为中文", true);
    private readonly Button test = Button("测试连接");
    private readonly Button save = Button("保存并应用", true);
    private readonly Button clear = Button("清除 Key");
    internal bool Exiting;
    internal event Action? TranslateRequested;
    internal event Action? CancelRequested;
    internal event Action? OcrRequested;
    internal event Action? CopyRequested;
    internal event Action<ApiSettings>? SaveRequested;
    internal event Action<ApiSettings>? TestRequested;
    internal event Action? ClearRequested;
    internal event Action<bool>? AutomaticChanged;
    internal event Action? ExitRequested;
    internal event Action<bool>? ContinuousRequested;
    internal event Action? ContinuousPauseRequested;
    internal event Action? ContinuousOriginalRequested;
    internal event Action? ContinuousReadRequested;
    internal event Action? ContinuousEndRequested;
    private ApiSettings applied = new();

    public MainWindow()
    {
        Title = AppIdentity.Title + (Program.IsDevelopment ? " · 开发试用" : "");
        Icon = AppIdentity.WindowIcon;
        Width = 820;
        Height = Math.Min(800, SystemParameters.WorkArea.Height - 50);
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F5F7F5");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 14;
        Foreground = Brush("#213A35");
        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 20), Background = Background };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(Label("随译", 28, true));
        heading.Children.Add(Label(AppIdentity.Version + " · 早期版本，持续迭代", 12));
        heading.Children.Add(Label("英语、俄语 → 中文　·　在正在阅读的窗口旁翻译", 14));
        heading.Children.Add(shortcuts);
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var quit = Button("退出随译");
        quit.Click += (_, _) => ExitRequested?.Invoke();
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(quit);
        footer.Children.Add(Label("关闭此窗口后继续在托盘运行", 12));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        tabs.Background = Brushes.White;
        tabs.BorderBrush = Brush("#DDE5E1");
        tabs.Items.Add(Tab("翻译", ReadingPanel()));
        tabs.Items.Add(Tab("API 配置", ApiPanel()));
        tabs.Items.Add(Tab("使用方法", HelpPanel()));
        tabs.Items.Add(Tab("持续覆盖", ContinuousPanel()));
        root.Children.Add(tabs);
        Content = root;
        Closing += OnClosing;
        Source.TextChanged += (_, _) => { CancelRequested?.Invoke(); Translation.Clear(); };
    }

    private UIElement ReadingPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Engine);
        panel.Children.Add(Label("原文", 13, true, 12));
        Source.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(Source);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        translate.Click += (_, _) => TranslateRequested?.Invoke();
        buttons.Children.Add(translate);
        var screenshot = Button("框选屏幕文字");
        screenshot.Click += (_, _) => OcrRequested?.Invoke();
        buttons.Children.Add(screenshot);
        var cancel = Button("取消");
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        buttons.Children.Add(cancel);
        var copy = Button("复制译文");
        copy.Click += (_, _) => CopyRequested?.Invoke();
        buttons.Children.Add(copy);
        panel.Children.Add(buttons);
        panel.Children.Add(Status);
        panel.Children.Add(Label("中文译文", 13, true, 12));
        panel.Children.Add(Translation);
        Automatic.Checked += (_, _) => AutomaticChanged?.Invoke(true);
        Automatic.Unchecked += (_, _) => AutomaticChanged?.Invoke(false);
        panel.Children.Add(Automatic);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement ApiPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(22), MaxWidth = 660, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Label("连接 DeepSeek", 20, true));
        panel.Children.Add(Label("填写你自己的 API Key。翻译会发送选中或提交的文字，并产生 API 用量。", 13, false, 10));
        panel.Children.Add(Label("Base URL", 13, true, 14));
        panel.Children.Add(apiBase);
        panel.Children.Add(Label("模型", 13, true));
        panel.Children.Add(apiModel);
        panel.Children.Add(Label("推荐 deepseek-v4-flash；也支持 deepseek-v4-pro。", 12));
        panel.Children.Add(Label("API Key（已配置时，留空保留原 Key）", 13, true, 14));
        panel.Children.Add(apiKey);
        panel.Children.Add(Label("超时（秒，5–45）", 13, true));
        panel.Children.Add(apiTimeout);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 10) };
        test.Click += (_, _) => ReadSettings(TestRequested);
        save.Click += (_, _) => ReadSettings(SaveRequested);
        clear.Click += (_, _) => ClearRequested?.Invoke();
        buttons.Children.Add(test);
        buttons.Children.Add(save);
        buttons.Children.Add(clear);
        panel.Children.Add(buttons);
        panel.Children.Add(apiMessage);
        panel.Children.Add(Label("测试连接只发送固定样例：A new day begins.\n关闭设置页会清除未保存的 Key 输入。退出程序会清空已应用的 Key。", 12, false, 14));
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs) && tabs.SelectedIndex != 1) apiKey.Clear(); };
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement ContinuousPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Label("在原位置持续阅读中文", 22, true));
        panel.Children.Add(Label("手动选择一个窗口内的范围，停稳后把英／俄文字覆盖成中文。滚动后自动更新；原网页、软件和文件保持原样。", 14, false, 8));
        var start = new WrapPanel { Margin = new Thickness(0, 14, 0, 18) };
        Add(start, "框选并开始", () => ContinuousRequested?.Invoke(false), true);
        Add(start, "选择整个窗口", () => ContinuousRequested?.Invoke(true));
        panel.Children.Add(start);
        panel.Children.Add(ContinuousStatus);
        var controls = new WrapPanel { Margin = new Thickness(0, 18, 0, 14) };
        Add(controls, "暂停／继续", () => ContinuousPauseRequested?.Invoke());
        Add(controls, "原文／中文", () => ContinuousOriginalRequested?.Invoke());
        Add(controls, "完整译文", () => ContinuousReadRequested?.Invoke());
        Add(controls, "结束", () => ContinuousEndRequested?.Invoke());
        panel.Children.Add(controls);
        panel.Children.Add(Label("覆盖层不接收点击；可直接点击、滚动原应用。按住 Ctrl+Alt+O 临时看原文，松开恢复。点击小控制条的「完整译文」可选择、复制或修正识别文字。", 13, false, 12));
        panel.Children.Add(Label("切换到其他窗口、输入控件编辑或出现遮挡时暂时露出原文，回到目标窗口后恢复。此开发试用先验证局部阅读流程；长译文在完整阅读面板展开。", 13, false, 12));
        panel.Children.Add(Label("截图只在本地识别；模型只接收文字。需先在 API 配置保存 Key。每次重新启动都关闭，不自动恢复采集。", 13, false, 12));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        static void Add(Panel panel, string text, Action action, bool primary = false) { var button = Button(text, primary); button.Click += (_, _) => action(); panel.Children.Add(button); }
    }

    private UIElement HelpPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Label("让翻译留在阅读现场", 21, true));
        panel.Children.Add(helpSteps);
        panel.Children.Add(Label("有些应用没有提供可读取的选区，此时使用框译。受保护画面无法保证捕获；工具不会自动提权或模拟 Ctrl+C。", 13, false, 22));
        panel.Children.Add(Label("这一版按选区翻译，不修改原文件。Key 和翻译内容只留在本次运行的内存中。", 13, false, 18));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void ReadSettings(Action<ApiSettings>? action)
    {
        try
        {
            if (!int.TryParse(apiTimeout.Text, out int timeout) || timeout is < 5 or > 45) throw new InvalidOperationException("请填写 5–45 秒的超时。");
            var config = TranslationService.Validate(new ApiSettings(apiBase.Text.Trim(), apiModel.Text.Trim(), string.IsNullOrWhiteSpace(apiKey.Password) ? applied.ApiKey : apiKey.Password.Trim(), timeout));
            action?.Invoke(config);
        }
        catch (Exception e) { SetApiMessage(e.Message, true); }
    }

    internal void Applied(ApiSettings value)
    {
        applied = value;
        apiKey.Clear();
        apiBase.Text = value.BaseUrl;
        apiModel.Text = value.Model;
        apiTimeout.Text = value.TimeoutSeconds.ToString();
        Engine.Text = value.ApiKey.Length == 0 ? "尚未配置 API" : $"DeepSeek · {value.Model} · 英／俄 → 中文";
        SetApiMessage(value.ApiKey.Length == 0 ? "Key 已清除。请填写 Key 后保存。" : "已应用。Key 只在本次运行中保留。");
    }

    internal void SetApiBusy(bool busy) { test.IsEnabled = save.IsEnabled = clear.IsEnabled = !busy; }
    internal void ConfigureHotkeys(string selection, string ocr, string pause, string notes)
    {
        shortcuts.Text = $"选中文字 {selection}　　框译 {ocr}　　暂停／开启 {pause}" + (notes.Length > 0 ? "\n" + notes : "");
        helpSteps.Text = $"1　先在「API 配置」填写 Key，测试后保存。\n\n2　在网页、文档或软件中选中英／俄文字，按 {selection}。\n\n3　遇到图片或扫描 PDF，按 {ocr}，拖动框选文字。OCR 在本地进行，识别后可编辑原文再翻译。\n\n4　译文出现在选区附近。点击「编辑原文」可回到主窗口修正 OCR 或继续长文阅读。\n\n5　需要连续阅读时开启自动划译。{pause} 随时暂停，托盘也可切换。";
    }
    internal void SetApiMessage(string text, bool error = false) { apiMessage.Text = text; apiMessage.Foreground = Brush(error ? "#A44236" : "#357263"); }
    internal void SetBusy(bool busy) { translate.IsEnabled = !busy; translate.Content = busy ? "正在翻译…" : "翻译为中文"; }
    internal void ShowSettings() { ShowNormal(); tabs.SelectedIndex = 1; }
    internal void ShowReading() { ShowNormal(); tabs.SelectedIndex = 0; }
    internal void ShowContinuous() { ShowNormal(); tabs.SelectedIndex = 3; }
    internal void ShowNormal() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!Exiting) { apiKey.Clear(); e.Cancel = true; Hide(); } }

    private static TabItem Tab(string title, UIElement content) => new() { Header = title, Content = content, Padding = new Thickness(18, 10, 18, 10) };
    internal static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    internal static TextBlock Label(string text, double size, bool bold = false, double top = 0) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 6), LineHeight = size * 1.65 };
    internal static Button Button(string text, bool primary = false) => new() { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Background = Brush(primary ? "#286957" : "#EFF4F1"), Foreground = primary ? Brushes.White : Brush("#284A40"), BorderBrush = Brush("#D5E1DA"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
    private static TextBox Field(string value) => new() { Text = value, Padding = new Thickness(10), FontSize = 14, Margin = new Thickness(0, 0, 0, 12), MinWidth = 440 };
    private static TextBox TextArea(bool readOnly, double height) => new() { IsReadOnly = readOnly, Height = height, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true, FontSize = 15, Padding = new Thickness(12), BorderBrush = Brush("#DCE5DE"), Background = Brush(readOnly ? "#F3F8F5" : "#FFFFFF"), MaxLength = readOnly ? 0 : 24000 };
}
