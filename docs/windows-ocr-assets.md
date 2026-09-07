# Windows OCR 模型来源

下载并校验日期：英文、俄文为 2026-09-07；简体中文为 2026-09-08。官方项目：[`tesseract-ocr/tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0)，固定版本 `4.1.0`。

| 文件 | 字节数 | Git blob SHA-1 | SHA-256 |
| --- | ---: | --- | --- |
| [eng.traineddata](https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata) | 4113088 | `bbef4675053b5b468cdb477053e28b1c698ba08e` | `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2` |
| [rus.traineddata](https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/rus.traineddata) | 3861738 | `b146cb2263acbc6383f8e92ea0ce759537687bb8` | `e16e5e036cce1d9ec2b00063cf8b54472625b9e14d893a169e2b0dedeb4df225` |
| [chi_sim.traineddata](https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/chi_sim.traineddata) | 2469156 | `388bac276d033d06e5ed5ba7a7ad14ae58f97dab` | `a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730` |

简体中文模型用于识别选区内已有中文，帮助覆盖翻译保留原文；翻译目标仍为英文／俄文 → 简体中文。

模型保存在 `windows/Suiyi.Windows/tessdata/`；下载数据经过大小检查、与官方 GitHub API 元数据一致的 Git blob 哈希检查后才写入文件。
使用官方 HTTPS 来源，未禁用 TLS 证书验证。许可证为 [Apache-2.0](https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/LICENSE)，分发时保留同目录 `LICENSE`。
