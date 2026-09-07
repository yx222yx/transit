# DeepSeek 翻译接口与配置

核验日期：2026-09-07。本文说明 `prototype/` 中使用 Node.js `fetch` 的网页原型适配器，无需额外 SDK；页面配置、密钥存储和本机接口由原型服务处理。Windows 客户端使用独立的 HttpClient 实现，遵循同一翻译协议；具体实现与验收见 [实施计划](implementation-plan.md)。

## 配置模板

模板文件位于 [`config/deepseek-config.example.json`](../config/deepseek-config.example.json)。只提交占位值，真实 Key 在程序设置中填写。

```json
{
  "baseUrl": "https://api.deepseek.com",
  "model": "deepseek-v4-flash",
  "apiKey": "在设置界面填写你自己的 DeepSeek API Key",
  "timeoutMs": 30000
}
```

- `baseUrl` 默认官方地址。也接受 `https://api.deepseek.com/v1`，末尾斜杠会被移除；本版不接受代理、自建兼容端点、额外路径、端口、查询参数或 URL 中的凭据。官方示例分别提供了[根地址](https://api-docs.deepseek.com/)与 [`/v1/chat/completions`](https://api-docs.deepseek.com/quick_start/agent_integrations/workbuddy/) 的调用方式。
- `model` 默认 `deepseek-v4-flash`，可切换 `deepseek-v4-pro` 或填写合法模型短名；填写不代表账户可用，最终以 API 结果为准。选择 Flash 是本项目为快速阅读所作的默认决策，依据官方对其较快响应的说明，实际延迟仍需账户实测。[V4 发布说明](https://api-docs.deepseek.com/news/news260424/)
- 当前官方列出的模型包括 V4 Flash、V4 Pro 和视觉实验版本。旧的 `deepseek-chat`、`deepseek-reasoner` 已被官方公告列入 2026-07-24 停用范围，因此新模板不采用它们。[首次调用](https://api-docs.deepseek.com/)、[更新记录](https://api-docs.deepseek.com/updates/)
- `apiKey` 是用户自己的密钥。适配器只在发送请求时将其放入 `Authorization: Bearer …`；不会写日志或从函数结果返回。不要把真实密钥提交到代码、示例或测试中。
- `timeoutMs` 默认 30000 毫秒，最大 45000 毫秒，覆盖连接和完整响应读取；调用方也可以用 `AbortSignal` 取消。

## 请求与结果

发送 `POST {baseUrl}/chat/completions`，JSON 包含 `model`、`messages`、`stream: false`、`thinking: { "type": "disabled" }`。当前模型默认开启思考，因此翻译请求显式关闭；本文不将关闭思考等同于确定的延迟保证。[Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)、[思考模式](https://api-docs.deepseek.com/guides/thinking_mode/)

语言参数只接受 `en`、`ru`、`auto`，目标固定为简体中文。输入为 1–12000 个 Unicode 码点，纯空白不发送；`auto` 由模型识别英文或俄文，本地不使用词库猜测语言。固定系统消息要求忠实翻译、保留段落/格式/专名/代码，把源文本里的指令作为原文翻译；这属于提示约束，不能保证模型每次都完美遵循，需要真实语料验收。

```js
const { DEFAULT_CONFIG, validateConfig, translateWithDeepSeek } = require('./prototype/translation-provider.cjs');
const config = validateConfig({ ...DEFAULT_CONFIG, apiKey: process.env.DEEPSEEK_API_KEY });
const result = await translateWithDeepSeek({ config, text: 'Hello, world.', sourceLanguage: 'en' });
// { text: '你好，世界。', model: 'deepseek-v4-flash', usage?: { prompt_tokens, completion_tokens, total_tokens, ... } }
```

`usage` 仅保留非负整数用量字段。不会向 UI 透传上游整包 JSON、请求标识、思考内容、原始错误或异常 cause。`model` 回传本次配置的请求模型名称。

## 失败行为

适配器抛出 `TranslationError`，含 `code`、HTTP 风格 `status` 与可展示的中文 `message`。HTTP 错误仅按状态映射，不读取可能包含原文或密钥的错误 body；不自动重试，以免划译重复计费。

| 情况 | 应用错误 | 处理提示 |
| --- | --- | --- |
| 401 | `INVALID_CREDENTIALS` | 重新配置密钥 |
| 402 | `INSUFFICIENT_BALANCE` | 检查 DeepSeek 余额 |
| 404 | `MODEL_NOT_FOUND` | 检查模型名 |
| 429 | `RATE_LIMITED` | 稍后重试 |
| 5xx | `PROVIDER_UNAVAILABLE` | 服务暂时不可用 |
| 超时 / 主动取消 | `TRANSLATION_TIMEOUT` / `REQUEST_ABORTED` | 缩短内容或重新划选 |
| 空译文 / 非法 JSON | `EMPTY_TRANSLATION` / `INVALID_RESPONSE` | 不当作成功译文 |
| 内容过滤 / 明确拒绝 | `TRANSLATION_REFUSED` | 展示未提供译文状态 |
| `finish_reason: length` | `TRANSLATION_TRUNCATED` | 不展示截断内容为完整译文 |

HTTP 状态含义依据[官方错误码](https://api-docs.deepseek.com/quick_start/error_codes/)；结束原因依据[Chat Completions 响应定义](https://api-docs.deepseek.com/api/create-chat-completion/)。普通文本形式的拒答并无稳定机器标志，仍需通过语料验收检查。

## 验证边界

在仓库根目录运行 `npm --prefix prototype test` 执行网页原型测试，或用 `node --test --test-isolation=none prototype/tests/translation-provider.test.cjs` 只测模型适配器。测试使用注入的 `fetchImpl` 模拟响应，覆盖英俄请求格式、官方 URL 限制、输入边界、错误脱敏、拒绝/截断、连接与读 body 超时、取消。测试不使用真实密钥，也不向 DeepSeek 发送任何请求。

历史验证使用 Node.js 24.16.0。由于当时沙箱禁止测试运行器派生子进程（`spawn EPERM`），使用 `--test-isolation=none` 在同一进程执行，适配器 10 项测试通过。此记录是整理目录前的验证证据；当前版本的复测结果见 [验收记录](windows-acceptance.md)。

目前尚未完成真实账户联调；仍需用户配置自己的密钥，验证账户权限、余额、模型可用性、英俄翻译质量、实际速度和计费。Mock 通过只证明请求构造与失败处理，不能证明真实服务可用或翻译质量达标。
