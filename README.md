# Transit · 随译

一个持续迭代的英俄屏幕翻译工具，将英语、俄语翻译为简体中文。

当前发布基线为 **0.1.0-alpha.1**，属于最初的 Alpha 阶段。Windows 为当前开发重点，Android 保留为后续方向。[选区覆盖翻译与分析侧栏 Spec](docs/specs/windows-continuous-translation.md) 已确认，[阶段 A：单选区覆盖翻译（手动刷新）](https://github.com/yx222yx/transit/issues/2) 已于 2026-09-09 按用户接受的当前初版范围结项。分析侧栏、配置保存和图片直传在后续阶段实现。用户明确确认定版前不生成新便携发布包。

最新开发版按试用反馈改为「选区覆盖」：开启时截取一次，之后点击「刷新选区」才更新，滚动和页面变化不自动截图。划译浮窗可拖动标题移动。请使用下方开发入口试用，具体操作见 [阶段 A 开发说明](docs/development.md#阶段-a-开发试用)。

项目仓库：[yx222yx/transit](https://github.com/yx222yx/transit)。

## 使用 Windows 工具

已构建的本地目录中，双击最外层的 **随译.exe**。它会启动 `app/` 内的实际程序；保持两者在同一目录即可。DLL、.NET Runtime、OCR 模型和许可证均放在 `app/` 内。

1. 在「API 配置」填写 DeepSeek Key，测试连接后保存。
2. 在其他软件中选中文字，使用界面显示的取词快捷键翻译。
3. 图片或不能直接取词的窗口，使用框译快捷键，通过本地 OCR 识别后翻译。
4. 需要连续阅读时开启自动划译；可从托盘暂停和退出。

Key 只在本次运行的内存中保留。自动划译默认关闭；截图在本地识别，模型调用发送选中的文字。网页原型与 Windows 程序不共享 API 配置。

跨软件取词及多屏操作继续按 [验收清单](docs/windows-acceptance.md) 完善；已验证的项目与待测项目分别记录。[Windows 使用说明](windows/README.md)。

## 目录

```text
随译.exe              本地生成的启动入口
README.md             项目入口说明
app/                  本地生成的完整 Windows 运行目录
windows/              Windows 源码、启动器和构建脚本
prototype/            网页原型、Node API 网关及测试
assets/app-icon/      图标 SVG、PNG 和多尺寸 ICO
config/               配置模板
docs/                 设计、研究、迭代与验收文档
scripts/              辅助脚本
dist/                 当前及历史压缩包
.local/               本机 SDK、依赖缓存与验证产物
```

Git 管理源码、资源和文档；启动 EXE、运行目录、SDK、缓存与压缩包由构建产生，不纳入源码提交。

## 开发与构建

需要 .NET 10 SDK。构建脚本优先使用本机项目内的 `.local/dotnet/dotnet.exe`，否则使用 PATH 中的 dotnet。根启动器使用 Windows 自带的 .NET Framework 编译器。

```powershell
# 当前开发试用：构建后打开开发版
powershell -File .\scripts\start-windows-dev.ps1

# 已完成构建时直接打开
powershell -File .\scripts\start-windows-dev.ps1 -SkipBuild
```

开发版从 `windows/Suiyi.Windows/bin/Release/net10.0-windows/win-x64/` 启动，标题标明开发试用，使用独立实例；完成构建后也可双击 `scripts/start-windows-dev.cmd` 打开。根目录的 `随译.exe` 和 `app/` 仍为已有发布版。源码仓库不包含生成的 EXE；从 GitHub 获取源码后运行上述开发试用命令。现有发布及打包命令见 [开发说明](docs/development.md)，明确确认定版后才执行。

本机开发构建的 `win-x64/Suiyi.exe` 现可直接查找项目内 `.local/dotnet/` 的 Desktop Runtime，无需全局安装 .NET。保持完整项目目录即可；推荐使用上述脚本入口，以便明确进入开发实例。

网页原型需要 Node.js 24 或更新版本：

```powershell
npm --prefix prototype run prototype
npm --prefix prototype test
```

打开 <http://127.0.0.1:4317/prototype.html>，可体验划译、段落框选、手动文本翻译和三种阅读布局。网页只读取本页文字；跨软件功能由 Windows 程序提供。

## 翻译配置与后续迭代

[DeepSeek 配置模板](config/deepseek-config.example.json) · [协议说明](docs/deepseek-api.md) · [开发与版本约定](docs/development.md) · [实施计划及 Android 路线](docs/implementation-plan.md)

默认使用 DeepSeek API；也保留网页离线示例词库。API 请求、取消、超时和错误由两个客户端分别实现。真实 Key 在运行时填写，不提交到仓库。

历史 `0.3.0` 为本地验证包编号。当前以 `0.1.0-alpha.1` 建立仓库的首次迭代基线，之后按实际交付内容更新预发布版本。
