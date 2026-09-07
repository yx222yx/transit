# 开发与版本约定

随译的 Git 仓库是 [yx222yx/transit](https://github.com/yx222yx/transit)，远程地址为 `https://github.com/yx222yx/transit.git`。产品当前处于 **0.1.0-alpha.1** 初始阶段，仍需根据试用反馈完善；该编号不代表功能最终定版。

此前 `Suiyi-Windows-x64-v0.3.0.zip` 属于本地开发验证包，编号不作为产品代际或稳定版本承诺。历史测试记录保留原始版本及路径，当前构建应单独验证。

## 目录

| 位置 | 用途 | Git 管理 |
|---|---|---|
| `随译.exe` | 最外层启动入口，由构建脚本生成 | 忽略 |
| `app/` | 实际 Windows 程序、DLL、运行时、OCR 数据及随包说明 | 忽略 |
| `windows/` | Windows 源码、启动器源码、构建脚本和依赖声明 | 提交 |
| `prototype/` | 网页原型、Node 网关、模型适配器及测试 | 提交 |
| `assets/app-icon/` | 图标源图及 Windows ICO | 提交 |
| `config/` | 无密钥的配置模板 | 提交 |
| `docs/` | 设计、研究、开发和验收文档 | 提交 |
| `scripts/` | 项目辅助脚本 | 提交 |
| `dist/` | 当前程序 ZIP 及历史程序包 | 忽略 |
| `.local/` | 本地 SDK、依赖缓存、自测产物 | 忽略 |

可执行文件必须与 `app/` 一起使用。整理目录或改名后，同步更新构建脚本和文档链接，不手工维护另一份源码。

## 常用命令

以下命令在仓库根目录执行：

```powershell
# Windows 编译、生成可运行目录、打包
powershell -File .\windows\build.ps1
powershell -File .\windows\build.ps1 -Publish
powershell -File .\windows\build.ps1 -Publish -Package

# 网页原型与自动测试
npm --prefix prototype run prototype
npm --prefix prototype test
```

Windows 发布入口为根目录 `随译.exe`；压缩包为 `dist/Transit-Windows-x64-v0.1.0-alpha.1.zip`。API Key 只在程序设置中填写，不加入配置示例、测试、日志或 Git 提交。

## Git 协作

`main` 保留可追溯的项目基线。后续独立功能使用简短的 `feature/<功能>` 分支，修复可用 `fix/<问题>`；每个普通 commit 尽量对应一项可理解的修改。合并前记录相关构建或测试结果，未验证内容明确标注。

不通过 force push 改写已有远程历史，不将构建目录、SDK、缓存、用户密钥或无关本地文件提交。发布版本需在对应提交验证后再打标签；提交源码、推送分支与创建公开 Release 是不同动作。

## 后续迭代

当前先完成英俄译中的实际 Windows 使用验收，收集取词、框译、焦点与翻译体验问题。alpha 阶段可继续使用 `0.1.0-alpha.2`、`0.1.0-alpha.3` 等预发布编号，按实际交付内容更新，不预先跳成稳定版。

第二版、第三版一定会继续新增和完善功能，但范围目前未定。应用覆盖改进、设置保存、阅读体验或 Android 客户端都可以进入候选讨论；这些是待确认方向，不是已经承诺到某一版的排期。每轮先确定具体需求及验收条件，再开发、验证并更新 [实施计划](implementation-plan.md)。Android 的平台约束继续参考 [平台研究](platform-research.md)。
