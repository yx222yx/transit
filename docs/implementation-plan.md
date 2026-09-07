# 随译：从原型到 Windows 工具

更新日期：2026-09-07。目标为 Windows 优先的英语、俄语 → 简体中文阅读工具；Android 是后续独立客户端。当前版本为 **0.1.0-alpha.1**，是仍在迭代的最初版本，尚未最终定版。此前原生 MVP 已生成 Windows x64 自包含验证包，并通过编译、协议模拟测试、英俄本地 OCR、热键注册和鼠标监听装卸；真实 DeepSeek 账户与各软件跨窗口兼容性尚待验收。

早期文件名中的 `0.3.0` 只是本地验证包编号，不代表第三版产品或稳定发布。今后的开发以 [transit 仓库](https://github.com/yx222yx/transit) 和 [开发约定](development.md) 为准；第二版、第三版新增功能将在使用反馈后逐轮确定。

## 1. 当前完成到哪一层

| 层 | 当前实现与证据 | 后续验证 |
|---|---|---|
| 阅读体验 | 网页原型；WPF 浮窗、托盘、设置；WPF 离屏渲染通过 | 实际不抢焦点、跨屏位置与应用切换 |
| 翻译引擎 | 网页与原生 HttpClient 均接 DeepSeek；协议、取消、超时、安全错误模拟测试通过 | 用户账户、翻译质量和耗时 |
| 取词 | 网页 DOM Range；原生独立 UIA helper、手动与可选自动划译 | 浏览器、Word、PDF、终端的实际 UIA 覆盖 |
| OCR | 屏幕框选、本地 Tesseract eng+rus；原生库加载与两条生成图像识别通过 | 真实截图质量、多屏 DPI |
| 凭据 | 网页及原生均保存在各自进程内存；不共享 Key | 原生用户操作中的重启／清除验收 |
| 分发 | 历史验证包已通过 Release 编译并生成自包含目录与 ZIP；当前采用根目录启动器 + app 目录 | 当前打包复测、干净机器、安装器、签名、升级卸载 |

因此，接下来最值得验证的是“能否从常用 Windows 应用可靠取得选中文字”，而不是继续增加原型主题。

## 2. Windows 技术选择

已选择 **C# + .NET 10 LTS + WPF**，原生 MVP 面向 Windows x64，目标框架为 `net10.0-windows`。短译文用不激活的置顶浮窗，设置窗口是普通窗口；常驻入口放在系统托盘。

微软官方支持政策将 .NET 10 列为 LTS，支持到 2028 年 11 月；受支持部署需要跟进该版本的补丁。项目使用稳定版 .NET 10 SDK，实际 SDK、Runtime 和构建结果记入验收记录。[.NET 官方支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

这是一项工程选择：核心难点是系统输入、UI Automation、截图、窗口焦点和 DPI。直接使用 Windows API 的宿主，比在 Electron 外再维护原生 helper 更直接。现有 HTML/CSS 保留为交互参考，界面不承诺原封不动复用。

翻译链路迁移为宿主内的 HttpClient 服务，沿用现有适配器的请求字段、提示词、错误分类和取消语义；Windows 行为仍需单独验证。原生程序包已包含 .NET Runtime，不依赖 Node 或浏览器服务。

OCR 已选 **NuGet `Tesseract` 5.2.0 + 本地 `eng` / `rus` 语言数据**，用于直接运行的原生 MVP。它是 charlesw 维护的 .NET wrapper，包许可证为 Apache-2.0；NuGet 所列 `net10.0` 为计算得到的框架兼容性；本项目另已实际运行并通过英俄 OCR 图像测试。[Tesseract 5.2.0 包页](https://www.nuget.org/packages/Tesseract/5.2.0)

wrapper 官方仓库说明：其 Tesseract 和 Leptonica 二进制由 Visual Studio 2019 编译，需要相应 VC++ 运行时；语言数据需要另行提供并复制至输出目录。微软当前受支持的 Visual C++ v14 Redistributable 可用于 VS2019 构建产物，架构必须匹配应用；当前 x64 构建验收使用 x64 运行时。分发前核对 wrapper、OCR 原生库及语言数据各自的许可证文件并随包保留所需声明。[wrapper 依赖与许可证](https://github.com/charlesw/tesseract#dependencies)、[VC++ 运行时](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)

平台约束与官方依据详见 [平台研究](platform-research.md)。微软的 [TextPattern 文本模型](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-understandingtheuiautomationtextobjectmodel)说明取词依赖目标应用支持，不存在一个保证读取所有窗口的万能接口。

## 3. 两条取词路径

### 划译：优先取得真实文字

手动路径：在原应用选中文字 → `Ctrl+Alt+T` → 取词线程读取前台窗口的 TextPattern 选区 → 校验文本长度和控件类型 → 取消旧请求 → 在选区附近显示加载与中文译文。

自动路径：用户开启自动划译后，左键选择文字并松开，进入同一取词与翻译链路。自动划译默认关闭，可从设置或托盘开启、暂停；是否开启不影响手动热键。不能取词时给出框译入口。热键若冲突，先增加 Shift，再尝试 Ctrl+Alt+F8/F9/F10；窗口和托盘显示实际分配。本机取词为 Ctrl+Alt+T、框译为 Ctrl+Alt+Shift+Q、暂停为 Ctrl+Alt+Shift+P。

- 全局输入监听只记录事件，不拦截原应用的输入。
- UIA 读取不放在 WPF UI 线程或鼠标钩子回调中；首个验证包含目标窗口无响应的情况。
- 排除本工具与密码控件；按应用禁用的黑名单尚未实现。
- 默认不模拟 Ctrl+C，不自动覆盖用户剪贴板，也不后台抓取整页/整个文件。
- 自动划译的启用、暂停和连续选区更新纳入首版验收；更多修饰键触发偏好留待后续。

### 框译：覆盖无法选中的文本

`Ctrl+Alt+Q` → 冻结当前画面 → 用户框选 → 本地 Tesseract 识别英俄文字 → 文本发给 DeepSeek → 近旁显示结果。框选时 `Esc` 取消。

- 屏幕图像在本地处理，首版不把截图直接发给模型。
- OCR 无文字时提示重新框选；识别出的原文可通过浮窗「编辑原文」进入主窗口修改并重新翻译。
- 当前采用 Tesseract，不要求 Windows OCR 的 package identity。研究文档中的 MSIX + Windows.Media.Ocr 是备选方案；若后续切换，再验证其包身份、语言包与安装流程。[Windows OCR API](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine)
- 受保护窗口或无法捕获的画面应直接提示，不绕过保护。

## 4. 模块边界

| 模块 | 输入 → 输出 | 独立验收 |
|---|---|---|
| SelectionCapture | 鼠标松开/手动热键 → 原文、语言提示、选区矩形、窗口身份 | 前台窗口有无 TextPattern、空选区、超时 |
| ScreenCapture | 用户框选 → 本地裁剪位图 | 多屏、负坐标、不同 DPI、取消 |
| OcrService | 位图 + en/ru → 文本 | 英俄、模糊/缩放、无文字 |
| TranslationService | 原文 + 源语言 + 配置 + 取消信号 → 中文或结构化错误 | 401/402/429、超时、截断、迟到响应 |
| PopupPresenter | 选区位置 + 翻译状态 → 不抢焦点的小窗 | 边缘避让、源窗口切换；当前不跟随外部窗口滚动 |
| SettingsStore | 用户配置 → 会话内存 | 重启、清除、无 Key 回显；系统凭据保存后续再做 |

取词任务和翻译任务分别携带请求身份；快速连续选择时合并或取消旧任务，迟到结果不得覆盖新选区。去重、请求数和取消结果需要实测，不能用取消本地等待推断远端请求已停止。

## 5. 实施顺序与验收门槛

当前 alpha 的构建入口为 `powershell -File .\windows\build.ps1 -Publish`，生成根目录 `随译.exe` 和 `app/`；加上 `-Package` 输出 `dist/Transit-Windows-x64-v0.1.0-alpha.1.zip`。程序包完整解压后运行最外层 `随译.exe`，保留整个 `app/`。以下应用覆盖和安装验收继续作为稳定发布前的门槛；历史验证与当前复测分开记录在 [验收记录](windows-acceptance.md)。

### 阶段 A：真实翻译链路（已实现，真实账户待验证）

完成配置、固定样例连接测试、划译/框选/粘贴接入、取消与错误处理。用用户自己的 Key 验证英语和俄语各一段；记录首次响应和总耗时，不记录 Key 或敏感原文。

### 阶段 B：Windows 原生 MVP（已有初始实现，应用覆盖待验收）

`windows/Suiyi.Windows` 已实现 .NET 10 WPF 宿主、`Ctrl+Alt+T` 手动 UIA 取词、可选自动划译、`Ctrl+Alt+Q` 框选与本地英俄 OCR、不抢焦点浮窗、托盘和会话内 Key 设置，并连接 DeepSeek。未配置 Key 时可验收启动、设置和明确的配置提示；真实翻译需输入用户自己的 Key。

先记录恢复依赖与 Release 编译结果，再实际检查 Edge/Chrome、记事本、Word、PDF 阅读器和终端。记录哪些应用可直接取词、哪些必须框译；同时验证热键、焦点、多屏 DPI 与超时退出。完整步骤及结果模板见 [Windows 验收清单](windows-acceptance.md)。编译成功只能证明项目可构建。

### 阶段 C：Windows 可用初版

修复阶段 B 中发现的取词、OCR、浮窗和请求问题，完成托盘暂停、设置、复制、取消、清除 Key 和退出的实测记录。UIA 不支持的应用必须明确记录为框译覆盖，不能并入自动划译成功率。

验收从用户任务出发：阅读一篇英文网页、一份俄文文件、一页扫描 PDF；在工具帮助下完成划译或框译，全程不需切换到另一个翻译网页。

### 阶段 D：可安装的 Windows 版本

在当前 Tesseract 路线上选定便携目录或安装包，验证 .NET 部署方式、VC++ 运行时、OCR 模型缺失、干净机器首次运行及升级/卸载。安装包与签名尚待开发。可选“记住 Key”使用系统凭据存储，不使用明文 JSON。

若要公开发布，加入安装包签名、更新渠道和清晰的网络发送说明。自动更新和云同步不属于第一版必需范围。

## 6. 应用覆盖验收表

下表所有场景均待 Windows 实测；不是已支持清单。执行记录统一填写到 [Windows 验收清单](windows-acceptance.md)，包括应用版本、源语言、取词方式与证据。

| 场景 | 首选 | 补充路径 | 必测内容 |
|---|---|---|---|
| Edge / Chrome 正文 | UIA 划译 | 框译；确有缺口再做扩展 | 单词、跨行、网页缩放、PDF标签页 |
| 记事本 / Word | UIA 划译 | 框译 | 长文选区、俄文、表格 |
| 文本型 PDF | UIA 划译 | 框译 | 双栏、断词、缩放 |
| 扫描 PDF / 图片 | 框译 | 调整缩放重新框选 | eng/rus、低分辨率、倾斜 |
| VS Code / 终端 / 自绘软件 | 实测 UIA | 框译 | 自定义控件、代码与正文混排 |
| 管理员或受保护窗口 | 明确检测限制 | 可用时手动路径 | 不自动提权、不绕过捕获保护 |

浏览器扩展是可选补强：只有 UIA 在常用网页上的实际表现不足，才新增 DOM 取词及 Native Messaging；避免首版同时维护两条重复的浏览器取词链路。

## 7. Android 后续路线

使用 Kotlin 原生壳，复用翻译协议、提示词、错误语义和验收语料，不强求复用 Windows 的输入/截图/浮窗代码。

1. 文本选中菜单的 `PROCESS_TEXT` 与系统分享入口：先形成可用的英俄翻译 App。
2. 用户授权的 MediaProjection 截图 + 本地 OCR + 可关闭悬浮结果。
3. 最后评估无障碍辅助取词；只有确有产品必要且满足分发渠道声明要求才加入。

默认 ML Kit Android OCR 不能直接承诺俄文支持，需要单独验证俄文模型路线。MediaProjection 授权与系统悬浮窗权限也不能静默跳过。具体官方文档及政策链接见 [Android 研究章节](platform-research.md#7-android-后续如何接入)。

## 8. 第一版的完成定义

原生 MVP 的完成需要实际启动、配置自己的 DeepSeek Key，并在记录过的应用与屏幕环境中完成英文、俄文的手动取词、自动划译和框译；失败路径可理解、可取消；不干扰原应用输入或焦点；多屏位置正确；退出后没有残留进程、钩子和热键。公开分发还需阶段 D 的安装与升级验收。文档、编译结果或网页原型测试均不能代替这些 Windows 验收结果。
