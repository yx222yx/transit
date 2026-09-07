// Local UI and DeepSeek gateway. Credentials live only in this process.
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const { randomBytes } = require('node:crypto');
const { DEFAULT_CONFIG, validateConfig, translateWithDeepSeek } = require('./translation-provider.cjs');
const files = {
  '/': ['prototype.html', 'text/html; charset=utf-8'],
  '/prototype.html': ['prototype.html', 'text/html; charset=utf-8'],
  '/prototype.css': ['prototype.css', 'text/css; charset=utf-8'],
  '/prototype.js': ['prototype.js', 'text/javascript; charset=utf-8'],
  '/prototype-api.js': ['prototype-api.js', 'text/javascript; charset=utf-8'],
  '/prototype-samples.js': ['prototype-samples.js', 'text/javascript; charset=utf-8'],
  '/prototype-selection-data.js': ['prototype-selection-data.js', 'text/javascript; charset=utf-8'],
  '/prototype-selection.js': ['prototype-selection.js', 'text/javascript; charset=utf-8'],
};

function createPrototypeServer({ translate = translateWithDeepSeek } = {}) {
  const token = randomBytes(32).toString('hex');
  let settings = { ...DEFAULT_CONFIG, apiKey:'', mode:'demo', revision:0 };
  const activeRequests = new Set();
  const publicConfig = () => ({ mode:settings.mode, baseUrl:settings.baseUrl, model:settings.model, timeoutMs:settings.timeoutMs, hasKey:!!settings.apiKey, storage:'session', revision:settings.revision });
  const json = (res, status, value) => {
    if (res.destroyed || res.writableEnded) return;
    res.writeHead(status, { 'Content-Type':'application/json; charset=utf-8', 'Cache-Control':'no-store', 'X-Content-Type-Options':'nosniff' });
    res.end(JSON.stringify(value));
  };
  const fail = (status,code,message) => Object.assign(new Error(message),{status,code});
  async function body(req) {
    if (!req.headers['content-type']?.startsWith('application/json')) throw fail(415,'JSON_REQUIRED','请求需使用 JSON 格式。');
    let size = 0;
    const chunks = [];
    for await (const chunk of req) {
      size += chunk.length;
      if (size > 70000) throw fail(413,'BODY_TOO_LARGE','请求过长，请缩小选区。');
      chunks.push(chunk);
    }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
    catch { throw fail(400,'INVALID_JSON','请求格式不正确。'); }
  }
  function candidate(input) {
    if (!input || typeof input !== 'object' || Array.isArray(input)) throw fail(400,'INVALID_CONFIG','配置格式不正确。');
    const apiKey = typeof input.apiKey === 'string' && input.apiKey.trim() ? input.apiKey.trim() : settings.apiKey;
    return validateConfig({ baseUrl:input.baseUrl ?? settings.baseUrl, model:input.model ?? settings.model, timeoutMs:input.timeoutMs ?? settings.timeoutMs, apiKey });
  }
  async function runTranslation(req,res,params) {
    const controller = new AbortController();
    activeRequests.add(controller);
    const abort = () => { if (!res.writableEnded) controller.abort(); };
    res.once('close',abort);
    try { return await translate({...params,signal:controller.signal}); }
    finally { res.off('close',abort); activeRequests.delete(controller); }
  }
  const server = http.createServer(async (req,res) => {
    try {
      const port = server.address()?.port;
      const hosts = [`127.0.0.1:${port}`,`localhost:${port}`];
      if (!hosts.includes(req.headers.host)) return json(res,403,{code:'HOST_REJECTED',message:'请从本机地址打开随译。'});
      const url = new URL(req.url, `http://${req.headers.host}`);
      if (url.pathname.startsWith('/api/')) {
        if (req.headers['x-suiyi-session'] !== token || (req.headers.origin && req.headers.origin !== `http://${req.headers.host}`) || req.headers['sec-fetch-site'] === 'cross-site') {
          return json(res,403,{code:'SESSION_REJECTED',message:'本机服务已重启或页面会话失效，请刷新后重试。'});
        }
        if (url.pathname === '/api/config' && req.method === 'GET') return json(res,200,publicConfig());
        if (url.pathname === '/api/config' && req.method === 'POST') {
          const input = await body(req);
          if (!['demo','deepseek'].includes(input?.mode)) throw fail(400,'INVALID_MODE','请选择示例或 DeepSeek 翻译。');
          const config = input.mode === 'deepseek' ? candidate(input) : {baseUrl:settings.baseUrl,model:settings.model,timeoutMs:settings.timeoutMs,apiKey:settings.apiKey};
          for (const request of activeRequests) request.abort();
          settings = {...config,mode:input.mode,revision:settings.revision+1};
          return json(res,200,publicConfig());
        }
        if (url.pathname === '/api/config' && req.method === 'DELETE') {
          for (const request of activeRequests) request.abort();
          settings = {...DEFAULT_CONFIG,apiKey:'',mode:'demo',revision:settings.revision+1};
          return json(res,200,publicConfig());
        }
        if (url.pathname === '/api/test' && req.method === 'POST') {
          const config = candidate(await body(req));
          const result = await runTranslation(req,res,{config,text:'A new day begins.',sourceLanguage:'en'});
          return json(res,200,{...result,sourceText:'A new day begins.'});
        }
        if (url.pathname === '/api/translate' && req.method === 'POST') {
          if (settings.mode !== 'deepseek' || !settings.apiKey) throw fail(409,'NOT_CONFIGURED','请先配置并启用 DeepSeek 翻译。');
          const input = await body(req);
          const revision = settings.revision;
          const result = await runTranslation(req,res,{config:settings,text:input?.text,sourceLanguage:input?.sourceLanguage || 'auto'});
          if (revision !== settings.revision) throw fail(409,'CONFIG_CHANGED','翻译配置已更改，请重新翻译。');
          return json(res,200,{...result,provider:'deepseek',revision});
        }
        return json(res,404,{code:'NOT_FOUND',message:'接口不存在。'});
      }
      if (!['GET','HEAD'].includes(req.method)) return json(res,405,{message:'不支持的请求方式。'});
      const file = files[url.pathname];
      if (!file) return json(res,404,{message:'页面不存在。'});
      let data = await fs.readFile(path.join(__dirname,file[0]));
      if (file[0] === 'prototype.html') data = Buffer.from(data.toString('utf8').replace('<head>',`<head>\n  <meta name="suiyi-session" content="${token}">`));
      res.writeHead(200,{'Content-Type':file[1],'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'"});
      res.end(req.method === 'HEAD' ? undefined : data);
    } catch (error) {
      const known = Number.isInteger(error.status) && typeof error.code === 'string';
      json(res,known ? error.status : 500,{code:known ? error.code : 'LOCAL_ERROR',message:known ? error.message : '本机翻译服务暂时不可用，请重试。'});
    }
  });
  server.on('close',()=>{for(const request of activeRequests) request.abort(); settings.apiKey='';});
  return server;
}
if (require.main === module) {
  const port = Number(process.env.PORT || 4317);
  const server = createPrototypeServer();
  server.on('error',error=>{console.error(`无法启动随译服务：${error.code || 'UNKNOWN'}`);process.exitCode=1;});
  server.listen(port,'127.0.0.1',()=>console.log(`随译：http://127.0.0.1:${port}/prototype.html\nAPI Key 仅在本次服务运行中保留。按 Ctrl+C 停止。`));
}
module.exports = {createPrototypeServer};
