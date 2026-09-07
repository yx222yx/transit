# 随译：Windows 落地与 Android 演进研究

研究日期：2026-09-07。范围：英语、俄语 → 简体中文；用户正在阅读的网页、软件窗口和文件。
本文中的“事实”来自官方资料；“建议”和验收指标是工程判断，尚未经过本机跨应用实测。
本文保留路线选择时的研究依据，不作为功能完成证明。当前 Windows alpha 的实现状态见 [实施计划](implementation-plan.md)，运行证据见 [验收记录](windows-acceptance.md)；Android 仍是后续方向。

## 1. 推荐路线

**建议：Windows 首版采用 C# / 当前受支持的 .NET + WPF，取词用 UI Automation，截图后用本地 OCR，模型 API 负责翻译文本。**
现有网页原型继续作为交互与 API 配置验证环境；真正跨应用的能力放进 Windows 原生宿主。
后续 Android 用 Kotlin 原生接入系统能力，两端共享翻译协议、提示词规范、术语数据和验收样本。

目标体验分成两条互补路径：

1. **划译**：在支持文本选择的应用里左键选择，松开后在选区附近显示译文。
2. **框译**：文字不可选时按热键，框选屏幕区域，本地识别英文或俄文，再显示译文。

**不能承诺所有应用都能自动划译，也不能承诺所有可见内容都能截图。**
UIA 依赖目标控件暴露文本接口；窗口完整性级别和内容捕获保护也会限制能力。
这是操作系统与目标应用的边界，不是换一个 UI 框架就能消除的问题。[UIA 文本模型](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-understandingtheuiautomationtextobjectmodel)、[UIA 安全边界](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)、[窗口捕获保护](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

## 2. Windows 自动划译的具体实现

### 2.1 触发与取词

**事实：** `WH_MOUSE_LL` 能观察鼠标按下、移动、松开等事件；回调运行在安装钩子的线程中，该线程必须有消息循环。
回调超时可能导致钩子被系统静默移除，官方建议把工作交给其他线程并立即返回。[LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)

**建议：** 单独的输入线程只记录左键起点、终点、前台窗口和时间；不拦截输入，不在回调中读 UIA 或请求模型。
松开后短暂等待选区稳定，再向取词工作线程投递一次任务；支持双击选词和拖动多行。
首版默认自动划译可暂停，也提供“按修饰键才划译”的偏好，避免用户每次普通选字都产生请求。
输入实现先验证低级钩子，若实际稳定性需要再评估 Raw Input；不同时维护两套全局输入链路。

**事实：** `IUIAutomationTextPattern.GetSelection()` 返回当前选区的文本范围；无选中文字时可能返回空范围，没有选择支持时可能返回 `NULL`。
多段不连续选区也可能返回多个范围，不能假定数组永远只有一项。[GetSelection](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getselection)

**建议流程：**

1. 限定在当前前台窗口，检查焦点元素、鼠标附近元素以及有限层级祖先是否支持 TextPattern。
2. 调用 GetSelection，再读取选区文本；空选区、纯空白、密码控件和本工具窗口直接忽略。
3. 获取每个可见选区的边界，优先用鼠标松开位置附近的行作浮窗锚点。
4. 生成请求 ID；新选区取消旧请求，迟到结果不能覆盖用户当前阅读内容。
5. 取词失败只显示轻量提示或框译入口，不自动复制全文、不自动抓取整个桌面。

**事实：** `GetBoundingRectangles()` 返回可见文本行的矩形；完全离屏、被遮挡或空范围可能返回空数组。
因此“已有文本”不代表“一定有可用坐标”。[选区边界](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-getboundingrectangles)

**事实：** 微软建议跨桌面 UIA 调用使用不拥有窗口的独立 MTA 线程；事件订阅与取消也需要遵守线程规则。[UIA 线程模型](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)
**建议：** UIA 工作线程与 WPF UI 线程分离，限定查找范围与文本上限；首个技术验证必须包含无响应目标应用。
异步等待超时不等于取消了底层 COM 调用；若实测出现提供方长期阻塞，再把 UIA 移到可终止重启的 helper 进程。
不要把普通 `Task` 超时包装宣称为已经解决跨进程调用卡死。

### 2.2 不抢焦点的浮窗

**事实：** `WS_EX_NOACTIVATE` 可使顶层窗口在被点击时不成为前台窗口；`SetWindowPos` 的置顶和不激活选项可分别控制层级和激活行为。[扩展窗口样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)、[SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
**建议：** 短译文用不激活的顶层小窗，设置界面用正常窗口；复制按钮保持原应用焦点，编辑或完整对照阅读显式进入可交互窗口。
浮窗位于选区下方或上方，避开光标和选中文字，受当前显示器工作区约束。
用户滚动时优先重新定位；源窗口切换、选区失效或不可见时关闭，固定译窗除外。
`Topmost=true` 本身不解决焦点、任务栏、全屏应用和跨完整性级别的问题。

### 2.3 多屏、DPI 与常驻入口

**事实：** Per-Monitor V2 DPI 感知要求在 DPI 改变时处理尺寸更新；微软明确建议进行多屏、多 DPI 测试。[高 DPI 桌面开发](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
**建议：** 系统采集链路统一用屏幕像素坐标，显示层明确转换 WPF DIP；保留显示器 ID、缩放比例与屏幕原点。
验收必须覆盖 100% / 150% / 200%、副屏在主屏左侧形成负坐标、选区跨屏、热插拔和任务栏占用。

**事实：** `RegisterHotKey` 定义系统级热键，冲突时可能失败；`Shell_NotifyIcon` 管理系统托盘图标。[热键](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[托盘](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)
**建议：** 托盘提供暂停划译、框译、设置、退出；热键可改且显示冲突；开机启动作为可选项。
关闭设置窗口后仍可继续阅读翻译，真正退出时注销钩子和热键，并取消所有在途请求。

## 3. OCR 框译与文件覆盖

### 3.1 一次截图的链路

**建议：** 热键触发 → 暂停划译 → 采集当前可见屏幕快照 → 遮罩框选 → 裁剪区域 → 本地 OCR → 文本 API 翻译 → 附近浮窗。
截屏图像默认仅存在内存；除非用户主动导出，不保存原图，也不把整张屏幕图发给翻译模型。
OCR 原文应可展开检查和修正，避免识别错误被流畅译文掩盖。

**事实：** `.NET Graphics.CopyFromScreen` 能把屏幕矩形像素复制到绘图表面；`Windows.Graphics.Capture` 可取得显示器或应用窗口的帧，并提供系统选择器。[CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen?view=windowsdesktop-10.0)、[Windows 屏幕捕获](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
**建议：** 首个一次性框选验证用 CopyFromScreen 降低接入成本；把截图封装为独立接口，遇到 GPU/HDR 等兼容问题再评估 Windows.Graphics.Capture。
WGC 的标准选择器流程和桌面互操作路径需分别验证，不能把系统选屏界面假装成已经实现的一步框选体验。
两条捕获路径都不绕过受保护内容；空图或捕获失败应明确提示，不生成猜测译文。

### 3.2 Windows.Media.Ocr 的安装约束

**事实：** 微软当前明确说明，`Windows.Media.Ocr` 仅正式支持具有 package identity 的桌面应用。
因此普通未打包 EXE 即使在开发机偶然能调用，也不能据此认定属于受支持的分发方案。[Windows.Media.Ocr](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr?view=winrt-26100)

**事实：** `AvailableRecognizerLanguages` 可查询设备可用 OCR 语言；`IsLanguageSupported`、`TryCreateFromLanguage` 和 `MaxImageDimension` 用于能力检查。[OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine?view=winrt-26100)
**建议：** 在设置中检测英文、俄文 OCR 实际可用性；缺少语言包时给出安装说明，不能只因系统显示语言是中文就假定英俄都已具备。
测试长截图与超大截图缩放；选取 OCR 语言时允许英语 / 俄语手动切换，混排结果纳入样本验收。

**推荐分发决策：**

| 方案 | 获得什么 | 需要验证什么 |
| --- | --- | --- |
| WPF + MSIX + Windows.Media.Ocr | 遵循系统 OCR 支持范围 | 安装签名、语言包、干净机器首次运行 |
| WPF + EXE/MSI + 本地第三方 OCR | 不依赖 Windows OCR 包身份 | 模型体积、俄文质量、原生库和许可证 |
| 现有安装器 + 外部位置包身份 | 保留安装器并授予身份 | 身份注册、升级卸载、签名和 OCR 实测 |

微软支持通过 MSIX 或外部位置打包获得 package identity；外部位置方式不意味着完全免安装。[打包方式](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/)
**建议：** 默认先用 MSIX + 系统 OCR 做闭环；如果“下载后直接运行”成为硬要求，就明确改用可分发的 OCR 引擎。
候选 Tesseract 官方训练数据包括 `eng` 和 `rus`；这证明有语言模型，不证明小字号屏幕识别质量达标。[Tesseract 语言数据](https://tesseract-ocr.github.io/tessdoc/Data-Files-in-different-versions.html)

### 3.3 能覆盖哪些文件与应用

| 场景 | 优先路径 | 限制与退路 |
| --- | --- | --- |
| Chrome / Edge 普通文字网页 | UIA 划译 | 按实测控件能力判断，失败可框译 |
| Word、记事本、其他文字软件 | UIA 划译 | 软件版本、控件实现影响可取词性 |
| PDF 阅读器的文本层 | UIA 划译 | 扫描件、特殊阅读器可走 OCR |
| 图片、扫描 PDF、Canvas 文本 | 框译 OCR | 只识别当前可见区域，质量受字形和分辨率影响 |
| 远程桌面中的文字 | 本地可见画面 OCR | 本地 UIA 不应假设能读取远端应用树 |
| 管理员窗口、UAC、受保护内容 | 明确能力受限 | 不强行提权或绕过系统限制 |

这些是待验证的覆盖策略，不是已通过兼容性认证的应用清单。
“文件中翻译”首版指用户在阅读器中看到的文字；整份 DOCX/PDF 的批量解析、保版翻译和导出是另一项功能。

## 4. 浏览器扩展、剪贴板与权限边界

**事实：** Chrome content script 可以读取网页 DOM；扩展需要相应站点权限，访问 `file://` 和隐身模式还需要用户单独授权。[Content scripts](https://developer.chrome.com/docs/extensions/develop/concepts/content-scripts)、[扩展权限](https://developer.chrome.com/docs/extensions/develop/concepts/declare-permissions)
**建议：** Windows 先靠 UIA + OCR 覆盖基本需求；若网页划译实测不稳，再做 Chrome/Edge 扩展以精确获取 DOM 选区和位置。
扩展只在用户允许的网站中工作；浏览器内置页面、特殊 PDF 查看器、跨源子框架等单独列兼容测试，不承诺 DOM 能读取任何可见内容。
扩展通过 Native Messaging 把选中文字交给本地宿主，密钥留在宿主；content script 不直接持有 API Key。[Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)

**建议：** “复制后翻译”可以作为用户主动触发的补充入口，首版不默认伪造 Ctrl+C。
自动复制会影响用户剪贴板和应用快捷键语义；恢复剪贴板还可能覆盖用户刚复制的新内容。
若后来加入复制取词，应显式启用、检测剪贴板版本变化，并针对富文本和延迟渲染做验证。
**事实：** `SendInput` 受 UIPI 限制，不能把模拟 Ctrl+C 当成突破管理员窗口的通用办法。[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)

**事实：** UIA 的高完整性级别访问有约束；`UIAccess` 还要求签名、受信任安装位置等条件，微软明确限制它的适用场景。[UIA 安全说明](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
**建议：** 首版以普通用户权限运行，管理员窗口作为已知限制；不要为了浮窗置顶就启用 UIAccess，更不以开机管理员常驻作为默认设计。

## 5. WPF 与 Electron + helper 的取舍

| 维度 | C# / WPF | Electron + .NET/C++ helper |
| --- | --- | --- |
| 现有 HTML/CSS 复用 | 复用交互设计，UI 需实现 XAML | 大部分原型 UI 可沿用 |
| Windows 取词与钩子 | 同一技术栈调用 COM / Win32 | 通常由 helper 提供，增加进程通信 |
| 浮窗和设置 | 原生窗口控制集中 | Electron 窗口能力 + 必要原生补充 |
| 常驻开销 | 建议预期更适合轻量工具，必须实测 | 带 Chromium，内存与包体需实测 |
| 分发和更新 | .NET 与 MSIX/安装器管理 | Electron、原生 helper、OCR 一起维护 |
| Android 复用 | 复用契约和数据 | HTML 不会自动获得 Android 系统权限能力 |

**事实：** WPF 只运行于 Windows；Electron 提供 `showInactive`、`setAlwaysOnTop` 等窗口 API。[WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)、[Electron BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window)
**判断：** 本项目最难的部分是跨应用取词、捕获、焦点和权限。若没有必须复用 Web UI 的团队约束，WPF 更直接。
Electron 仍是可行备选，不能只加壳就宣称获得了系统级划译；选择它应接受 helper、IPC 和打包的额外维护。
Electron 路线须按官方安全建议隔离 renderer 与系统能力、限制 IPC 来源，不加载不可信远程页面。[Electron 安全](https://www.electronjs.org/docs/latest/tutorial/security)

**建议的最小模块：** `WindowsInput`、`SelectionReader`、`ScreenCapture`、`OcrEngine`、`TranslationClient`、`PopupPresenter`、`SettingsStore`。
模块先在一个解决方案中组织；只有 UIA 阻塞或 Electron IPC 等明确需求出现时再增加 helper 进程。
API Key 在 Windows 使用当前用户范围的 DPAPI 加密保存；设置导出排除密钥，日志不写原文和 Key。[.NET 数据保护](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)
若选 Electron，可使用主进程 `safeStorage`；它在 Windows 使用系统密码学，但不等于能防同一用户上下文中的所有恶意进程。[safeStorage](https://www.electronjs.org/docs/latest/api/safe-storage)

## 6. 分阶段实施与验收

以下是建议门槛，时间和性能数值需要在指定设备、网络、模型及样本上记录，不能作为尚未实测的宣传指标。

| 阶段 | 交付 | 完成依据 |
| --- | --- | --- |
| A：模型链路 | 可配置 DeepSeek 格式 API，真正翻译任意输入 | 英俄各 20 条自备样本；无 Key、鉴权失败、超时、限流、取消和乱序结果均有正确状态 |
| B：原生取词验证 | WPF 小工具 + UIA 取词 + 不抢焦点浮窗 | 在确定版本的浏览器、记事本、Word、PDF 阅读器记录逐项结果；不支持项有证据 |
| C：完整日用闭环 | 自动划译 + 热键框译 + 本地英俄 OCR + 托盘 | 覆盖网页、文本文件、扫描 PDF、图片；切窗、滚动、新选择、退出均无残留浮窗或请求 |
| D：可安装 Windows 测试版 | 签名或明确测试证书流程的安装包、设置持久化、帮助 | 干净 Windows 机器安装/升级/卸载；缺 OCR 语言包可处理；用户不需 Node 或开发终端 |
| E：真实阅读试用 | 兼容矩阵与体验缺陷修复 | 连续阅读 2 小时，记录误触发、取词失败、OCR 错误、模型延迟、资源占用 |
| F：Android 首版 | 选词菜单/分享 + 模型翻译，再加框译 | 真机验证权限拒绝、旋转、后台终止、取消投屏、英俄 OCR；与 Windows 使用相同翻译样本 |

阶段 B 应先验证系统能力，再大规模迁移界面；网页原型测试无法代替该阶段。
建议输入松开到出现“正在翻译”控制在 200 ms 内；UIA 取词目标 P95 小于 300 ms，均需本机测量确认。
模型请求单独记录首字时间、总时间和错误率，不把网络延迟算成 UIA 失败，不把 OCR 错误算成模型翻译错误。
用固定截图样本评估英语和俄语字符错误率；小字号、深色背景、混排、标点和多栏分别统计。
正式验收要求“关键应用中的取词成功或可用 OCR 退路”，不以单个网页样例通过代替跨应用完成。

## 7. Android 后续如何接入

### 7.1 从系统文本菜单开始

**事实：** Android `ACTION_PROCESS_TEXT` 接收 `EXTRA_PROCESS_TEXT` 文本，并用 `EXTRA_PROCESS_TEXT_READONLY` 表达只读条件。[Intent 文本处理](https://developer.android.com/reference/kotlin/android/content/Intent#ACTION_PROCESS_TEXT:kotlin.String)
**建议：** Android 第一批功能采用“长按选词 → 翻译”与系统分享入口；展示译文卡片，不默认替换源应用文字。
这条路径不需要为所有日常翻译都开启无障碍或悬浮窗；菜单是否出现取决于来源应用的文本选择实现，仍需兼容实测。

### 7.2 截屏和悬浮窗是独立授权

**事实：** MediaProjection 每次会话都需要用户同意；一次会话对应一次 `createVirtualDisplay()`，授权 token 不能重复使用。
目标 Android 14 及以上还需要声明 `mediaProjection` 前台服务类型及相应权限。[MediaProjection](https://developer.android.com/media/grow/media-projection)
**建议：** 用户点“开始屏幕翻译”后申请一次捕获会话，保留可见停止入口；结束后重新开始应重新授权。
同一有效会话可处理后续截图，不能描述为每识别一张图都要授权，也不能描述为一次授权后永久后台截图。
拒绝权限时继续保留文本菜单/分享翻译；处理旋转、选定应用窗口尺寸变化和系统撤销捕获。

**事实：** `TYPE_APPLICATION_OVERLAY` 需要 `SYSTEM_ALERT_WINDOW`；悬浮窗位于普通 Activity 之上、关键系统窗口之下，系统可调整其可见性。
用户通过系统权限页面授权，应用用 `Settings.canDrawOverlays()` 检查。[Overlay 窗口](https://developer.android.com/reference/android/view/WindowManager.LayoutParams#TYPE_APPLICATION_OVERLAY)、[悬浮窗权限](https://developer.android.com/reference/android/Manifest.permission#SYSTEM_ALERT_WINDOW)
**建议：** 悬浮按钮和译文卡片分别设计触摸区域，不用全屏透明窗长期拦截触摸；权限未授予时用应用内结果页。
安全窗口的内容捕获可能被系统拒绝；无障碍截图 API 也有明确的 secure-window 错误。[AccessibilityService 截图限制](https://developer.android.com/reference/android/accessibilityservice/AccessibilityService#ERROR_TAKE_SCREENSHOT_SECURE_WINDOW)

### 7.3 无障碍自动取词不是默认前提

**事实：** AccessibilityService 能在配置 `canRetrieveWindowContent` 后读取可访问 UI 层级，仍依赖目标应用暴露的节点。[创建无障碍服务](https://developer.android.com/guide/topics/ui/accessibility/service)
**事实：** Google Play 允许多类应用使用该 API，但普通翻译工具不能仅为降低授权成本宣称自己是面向残障用户的 accessibility tool。
非该类工具需要应用内突出披露、主动同意及 Play Console 声明/审核，说明读取的数据和用途。[Google Play AccessibilityService 政策](https://support.google.com/googleplay/android-developer/answer/10964491?hl=en)
**建议：** 无障碍能力放在 Android 后续增强阶段，先证明菜单/分享/框译无法满足的具体场景，再设计授权与发布材料。
仅订阅确有必要的事件；自动读取整屏、持续上传或让模型操作其他应用均不是本项目翻译功能的前提。

### 7.4 俄文 OCR 与两端复用

**事实：** 当前 ML Kit Android 接入文档列出的 OCR 依赖和识别器选项为拉丁文、中文、天城文、日文、韩文；没有列出俄文专用接入选项。[ML Kit Android OCR](https://developers.google.com/ml-kit/vision/text-recognition/v2/android)
**判断：** 不能据此方案承诺默认 ML Kit 已满足俄文 OCR；语言总表中的零散西里尔项目也不足以证明 Android SDK 可用俄文识别器。
**建议：** 把 Tesseract `eng` / `rus` 或其他明确支持俄文的本地引擎列为候选，做 Android 原生绑定、包体和小字号实机验证后再定型。
先保证文本菜单拿到的俄文能直接翻译；OCR 能力不足时给出真实错误，不把未识别内容交给模型补写。

**建议：** Android 使用 Kotlin + 原生 Activity/Service，界面可用 Jetpack Compose；官方 Android 开发路线也是 Kotlin-first。[Android Kotlin-first](https://developer.android.com/kotlin/first)
共享 `TranslationRequest/Result` 字段、错误码、英俄到中文提示词规范、术语表 JSON、语言标识和测试样本。
Windows HWND/UIA/屏幕像素与 Android Intent/Accessibility/投屏会话分别留在平台层；不把这些能力伪装成完全等价的跨平台接口。
第一版不引入账户系统和云同步；Windows 与 Android 都允许用户自行配置提供方，未来确有需求时再设计跨设备同步。
