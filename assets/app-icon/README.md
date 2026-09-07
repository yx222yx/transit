# 随译应用图标

以两张重叠的翻译卡片呈现外文与中文的对照关系：深绿 `#286957` 圆角底，薄荷 `#BFE6D4` 外文卡片，近白 `#F9FCF8` 中文卡片。字形使用几何路径绘制，不依赖系统字体。透明外角、平涂配色与粗线条用于桌面、任务栏和托盘等小尺寸场景。

- `source.svg`：可编辑的原生矢量源文件，64 × 64 设计坐标。
- `source.png`：由 SVG 渲染的 1024 × 1024 RGBA 图片，保留透明外角。
- `app.ico`：9 个独立 PNG 帧组成的 Windows ICO，包含 16、20、24、32、40、48、64、128、256 像素尺寸，32 位颜色。
- `../../scripts/icon/build-icon.cjs`：可重复生成 PNG 和 ICO 的脚本。

## 重新生成

需要 Node.js 与 `sharp`。在已提供 `sharp` 的环境中，从项目根目录运行：

```powershell
node scripts/icon/build-icon.cjs
```

若本机尚未安装该依赖，可将图标构建依赖独立安装在工作区的本地工具目录，再运行脚本：

```powershell
npm install --prefix .local/icon-tools sharp
$env:NODE_PATH = Join-Path (Get-Location) '.local/icon-tools/node_modules'
node scripts/icon/build-icon.cjs
```

脚本使用 sharp 渲染 SVG，通过 Lanczos3 缩小同一张 PNG，随后写入 ICO 文件头、9 个帧目录和相应 PNG 数据。转换不增加滤镜或改变图案。应用构建直接消费已生成的 `app.ico`，无须安装图标工具依赖。

SVG 是图标的可编辑源文件；PNG 和 ICO 均由它生成。修改图案后运行构建脚本，再重新发布 Windows 程序即可应用到启动程序、窗口及托盘。
