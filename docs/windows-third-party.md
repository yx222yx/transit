# Windows 包第三方许可证

核验日期：2026-09-07；2026-09-08 补充同一固定版本的简体中文语言数据。许可证文本放在 `windows/Suiyi.Windows/licenses/`，随自包含发布包一起复制；语言包目录原有的 `tessdata/LICENSE` 也应保留。

| 组件 | 本次版本依据 | 随包文本 |
| --- | --- | --- |
| Tesseract .NET wrapper | 本地 NuGet `Tesseract/5.2.0/tesseract.nuspec`，作者 Charles Weld，Apache-2.0 | `Tesseract-DotNet-5.2.0-LICENSE.txt`、固定版本 README 版权说明 |
| Tesseract OCR 引擎 | NuGet 中的 `tesseract50.dll`；wrapper 固定版本更新记录说明升级到 5.2 | `Tesseract-OCR-5.2.0-LICENSE.txt`，Apache-2.0 |
| Leptonica | NuGet 原生文件 `leptonica-1.82.0.dll` | `Leptonica-1.82.0-LICENSE.txt`，BSD-2-Clause |
| tessdata_fast 英文、俄文、简体中文语言数据 | 官方 `4.1.0` 标签的 `eng.traineddata`、`rus.traineddata`、`chi_sim.traineddata`；下载记录见 [OCR 模型来源](windows-ocr-assets.md) | `Tessdata-Fast-4.1.0-LICENSE.txt`，Apache-2.0 |
| InteropDotNet | wrapper `5.2.0` 固定源码内的版权行及 MIT 声明 | `InteropDotNet-MIT.txt`，保留 Andrey Akinshin 版权和标准 MIT 全文 |
| .NET Core runtime、Windows Desktop runtime | 本地自包含恢复包 `10.0.11`，两份 nuspec 均声明 MIT | `DotNet-Runtime-10.0.11-LICENSE.txt`、`DotNet-Runtime-10.0.11-THIRD-PARTY-NOTICES.txt`、`DotNet-WindowsDesktop-10.0.11-LICENSE.txt` |

上游来源：[wrapper 5.2.0](https://github.com/charlesw/tesseract/tree/5.2.0)、[引擎升级记录](https://github.com/charlesw/tesseract/blob/5.2.0/ChangeLog.md)、[Tesseract 引擎许可证](https://github.com/tesseract-ocr/tesseract/blob/5.2.0/LICENSE)、[Leptonica 许可证](https://github.com/DanBloomberg/leptonica/blob/1.82.0/leptonica-license.txt)、[tessdata_fast 许可证](https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/LICENSE)、[InteropDotNet 固定源码声明](https://github.com/charlesw/tesseract/blob/5.2.0/src/Tesseract/Internal/InteropDotNet/InteropRuntimeImplementer.cs)。

已检查上述四个 OCR 仓库固定标签的根目录 `NOTICE`、`NOTICE.txt`，均返回 404；wrapper 的完整固定版本源码压缩包也未包含独立 NOTICE。保留的 `SOURCES.txt` 是本项目整理的来源索引，不是伪造的上游 NOTICE。

.NET 许可证直接复制自 `.local/nuget/microsoft.netcore.app.runtime.win-x64/10.0.11/` 与 `.local/nuget/microsoft.windowsdesktop.app.runtime.win-x64/10.0.11/`。两份包元数据指向 [dotnet/dotnet 的固定提交](https://github.com/dotnet/dotnet/tree/e2f47b0110ed922f21a1522da67279133ce28f32)。已查看 SDK 根目录条款，但没有将 SDK 许可证代替实际 runtime 包许可证。

此清单覆盖这里列出的组件和上游附带的声明，不代表完整的二进制来源审计。本次没有下载、安装或打包 Visual C++ Redistributable 安装程序。
