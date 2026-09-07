# 随译 Windows 初版（0.1.0-alpha.1）

英语、俄语 → 简体中文。Windows x64 原生程序；随包包含 .NET Runtime 和英俄 OCR 模型，无需启动 Node 或浏览器服务。当前是持续开发中的首个 alpha 版本，功能和界面还会调整，尚未最终定版。

## 开始使用

1. 完整解压程序包，双击最外层的 `随译.exe`。保留旁边的整个 `app` 文件夹；实际程序、DLL、运行时、OCR 模型和许可证都在其中。
2. 在「API 配置」填写自己的 DeepSeek Key，点击「测试连接」，成功后「保存并应用」。默认地址为 `https://api.deepseek.com`，模型为 `deepseek-v4-flash`。
3. 在网页、文档或其他软件里选中文字，按窗口顶部显示的「选中文字」快捷键。
4. 图片、扫描 PDF 或无法直接取词的窗口，使用「框译」快捷键，拖动框选文字；Esc 取消。
5. 结果浮窗可复制译文；「编辑原文」回到主窗口，可修正 OCR 文字后再次翻译。

Key 只保存在本次程序运行的内存中，退出后需要重新填写。原生程序与网页原型不共享配置。连接测试发送固定句子 `A new day begins.`；翻译发送选中的文字，按自己的 DeepSeek 账户计费。截图在本地识别，不发送截图。

## 快捷键和托盘

| 功能 | 默认快捷键 |
|---|---|
| 翻译选中文字 | Ctrl + Alt + T |
| 框选 OCR 翻译 | Ctrl + Alt + Q |
| 开启／暂停自动划译 | Ctrl + Alt + P |

遇到占用时，程序先尝试增加 Shift，再尝试 Ctrl + Alt + F8 / F9 / F10。**以窗口顶部、使用方法和托盘显示的实际快捷键为准。** 本次开发机器实际为取词 `Ctrl+Alt+T`、框译 `Ctrl+Alt+Shift+Q`、暂停 `Ctrl+Alt+Shift+P`。

自动划译默认关闭，可在「翻译」页或托盘开启。开启后，拖动选中的文字会发送给模型。关闭主窗口后继续在托盘运行；点击「退出随译」才结束程序并清空 Key。

## 应用覆盖

直接划译取决于目标软件是否提供 UI Automation 文本选区；失败时使用框译。框译只读取屏幕上的可见文字，不解析整份文件，也不修改原文件。

受保护画面、管理员权限差异及部分独占全屏程序可能无法取词或截取；程序不会自动提权或绕过限制。外部窗口滚动时，当前浮窗不会自动跟随原文位置，可收起后重新选取。

模型协议、英俄本地 OCR、热键、图标和界面渲染由自动自测覆盖。真实 DeepSeek 账户、各软件跨窗口操作与多屏 DPI 仍需实测，当前结果见仓库中的 [验收记录](https://github.com/yx222yx/transit/blob/main/docs/windows-acceptance.md)。

## OCR 运行库

若无法加载 OCR，先确认完整解压，再安装微软官方 [Visual C++ v14 x64 运行库](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)。此前开发机器已成功加载，无需额外安装。许可证在 `app/licenses` 中。

## 源码构建

仓库根目录运行：

```powershell
powershell -File .\windows\build.ps1 -Publish
```

需要 .NET 10 SDK，脚本优先使用仓库的 `.local/dotnet/dotnet.exe`，否则使用 PATH 中的 dotnet。发布后在仓库根目录生成 `随译.exe` 启动器及 `app/` 运行目录。

加上 `-Package` 会生成 `dist/Transit-Windows-x64-v0.1.0-alpha.1.zip`，供完整解压运行。构建输出不提交 Git；源码、图标资源和构建脚本由 [transit 仓库](https://github.com/yx222yx/transit) 管理，开发与版本规则见 [开发说明](https://github.com/yx222yx/transit/blob/main/docs/development.md)。

首次构建需要恢复 NuGet 依赖；语言数据为官方 `tessdata_fast` 的 `4.1.0`，随源码提供 eng 和 rus。
