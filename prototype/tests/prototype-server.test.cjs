'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createPrototypeServer } = require('../prototype-server.cjs');
const { DEFAULT_CONFIG, TranslationError } = require('../translation-provider.cjs');

const fakeKey = 'sk-local-gateway-test-not-real';
const configured = { ...DEFAULT_CONFIG, mode: 'deepseek', apiKey: fakeKey };

async function gateway(t, translate = async ({ config }) => ({ text: '测试译文', model: config.model })) {
  // All requests in this file target an ephemeral loopback server. The provider is always injected.
  const server = createPrototypeServer({ translate });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  t.after(() => new Promise((resolve, reject) => {
    server.close(error => error ? reject(error) : resolve());
    server.closeAllConnections();
  }));
  const base = `http://127.0.0.1:${server.address().port}`;
  const page = await fetch(`${base}/prototype.html`);
  assert.equal(page.status, 200);
  const html = await page.text();
  const token = html.match(/<meta\s+name=["']suiyi-session["']\s+content=["']([^"']+)["']/)?.[1];
  assert.match(token || '', /^[a-f0-9]{64}$/);

  async function request(route, { method = 'GET', body, session = token, headers = {} } = {}) {
    const requestHeaders = { ...headers };
    if (session !== null) requestHeaders['X-Suiyi-Session'] = session;
    if (body !== undefined) requestHeaders['Content-Type'] = 'application/json';
    const result = await fetch(`${base}${route}`, {
      method,
      headers: requestHeaders,
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
    const raw = await result.text();
    return { status: result.status, raw, data: JSON.parse(raw), headers: result.headers };
  }
  return { request, base, token };
}

function assertNoKey(result) {
  assert.equal(result.raw.includes(fakeKey), false);
  assert.equal(Object.hasOwn(result.data, 'apiKey'), false);
}

test('serves a session token, starts in demo, and refuses unconfigured translation', { timeout: 5000 }, async t => {
  let calls = 0;
  const { request } = await gateway(t, async () => { calls++; return { text: '不应调用' }; });
  const initial = await request('/api/config');
  assert.equal(initial.status, 200);
  assert.equal(initial.data.mode, 'demo');
  assert.equal(initial.data.hasKey, false);
  assert.equal(initial.data.storage, 'session');
  assert.equal(initial.data.baseUrl, DEFAULT_CONFIG.baseUrl);
  assertNoKey(initial);
  const result = await request('/api/translate', { method: 'POST', body: { text: 'Hello.', sourceLanguage: 'en' } });
  assert.equal(result.status, 409);
  assert.equal(result.data.code, 'NOT_CONFIGURED');
  assert.equal(calls, 0);
});

test('requires the page token and rejects mismatched origins or cross-site requests', { timeout: 5000 }, async t => {
  const { request, base } = await gateway(t);
  for (const options of [
    { session: null },
    { session: 'invalid-token' },
    { headers: { Origin: 'https://unrelated.example' } },
    { headers: { Origin: 'null' } },
    { headers: { 'Sec-Fetch-Site': 'cross-site' } }
  ]) {
    const denied = await request('/api/config', options);
    assert.equal(denied.status, 403);
    assert.equal(denied.data.code, 'SESSION_REJECTED');
  }
  const accepted = await request('/api/config', { headers: { Origin: base, 'Sec-Fetch-Site': 'same-origin' } });
  assert.equal(accepted.status, 200);
});

test('saves config without exposing the key, keeps an existing key, translates, and clears it', { timeout: 5000 }, async t => {
  const calls = [];
  const { request } = await gateway(t, async params => {
    calls.push(params);
    return { text: '你好，世界。', model: params.config.model, usage: { total_tokens: 12 } };
  });
  const saved = await request('/api/config', { method: 'POST', body: configured });
  assert.equal(saved.status, 200);
  assert.equal(saved.data.mode, 'deepseek');
  assert.equal(saved.data.hasKey, true);
  assert.equal(saved.data.revision, 1);
  assertNoKey(saved);
  const read = await request('/api/config');
  assert.equal(read.data.hasKey, true);
  assertNoKey(read);

  const retained = await request('/api/config', { method: 'POST', body: { mode: 'deepseek', model: 'deepseek-v4-pro', apiKey: '' } });
  assert.equal(retained.status, 200);
  assert.equal(retained.data.hasKey, true);
  assert.equal(retained.data.model, 'deepseek-v4-pro');
  assertNoKey(retained);
  const result = await request('/api/translate', { method: 'POST', body: { text: 'Привет, мир.', sourceLanguage: 'ru' } });
  assert.equal(result.status, 200);
  assert.equal(result.data.text, '你好，世界。');
  assert.equal(result.data.provider, 'deepseek');
  assert.equal(result.data.revision, retained.data.revision);
  assertNoKey(result);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].config.apiKey, fakeKey);
  assert.equal(calls[0].config.model, 'deepseek-v4-pro');
  assert.equal(calls[0].text, 'Привет, мир.');
  assert.equal(calls[0].sourceLanguage, 'ru');
  assert.ok(calls[0].signal instanceof AbortSignal);

  const omitted = await request('/api/config', { method: 'POST', body: { mode: 'deepseek' } });
  assert.equal(omitted.status, 200);
  assert.equal(omitted.data.hasKey, true);
  const cleared = await request('/api/config', { method: 'DELETE' });
  assert.equal(cleared.status, 200);
  assert.equal(cleared.data.mode, 'demo');
  assert.equal(cleared.data.hasKey, false);
  assertNoKey(cleared);
  assert.equal((await request('/api/config')).data.hasKey, false);
  const afterClear = await request('/api/translate', { method: 'POST', body: { text: 'Hello.' } });
  assert.equal(afterClear.status, 409);
  const noSavedKey = await request('/api/config', { method: 'POST', body: { mode: 'deepseek' } });
  assert.equal(noSavedKey.status, 400);
  assert.equal(noSavedKey.data.code, 'INVALID_API_KEY');
  assert.equal(calls.length, 1);
});

test('connection test always translates the fixed sample without saving candidate credentials', { timeout: 5000 }, async t => {
  const calls = [];
  const { request } = await gateway(t, async params => {
    calls.push(params);
    return { text: '新的一天开始了。', model: params.config.model };
  });
  const result = await request('/api/test', { method: 'POST', body: { ...configured, text: 'Do something else.', sourceLanguage: 'ru' } });
  assert.equal(result.status, 200);
  assert.equal(result.data.sourceText, 'A new day begins.');
  assert.equal(result.data.text, '新的一天开始了。');
  assertNoKey(result);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].text, 'A new day begins.');
  assert.equal(calls[0].sourceLanguage, 'en');
  assert.equal(calls[0].config.apiKey, fakeKey);
  const unchanged = await request('/api/config');
  assert.equal(unchanged.data.mode, 'demo');
  assert.equal(unchanged.data.hasKey, false);
  assert.equal(unchanged.data.revision, 0);
});

test('rejects invalid provider URLs for save and test before any provider call', { timeout: 5000 }, async t => {
  let calls = 0;
  const { request } = await gateway(t, async () => { calls++; return { text: '不应调用' }; });
  for (const route of ['/api/config', '/api/test']) {
    const denied = await request(route, { method: 'POST', body: { ...configured, baseUrl: 'https://unrelated.example' } });
    assert.equal(denied.status, 400);
    assert.equal(denied.data.code, 'INVALID_BASE_URL');
    assertNoKey(denied);
  }
  const unchanged = await request('/api/config');
  assert.equal(unchanged.data.revision, 0);
  assert.equal(unchanged.data.hasKey, false);
  assert.equal(calls, 0);
});

for (const change of ['save', 'clear']) {
  test(`configuration ${change} aborts an unfinished provider translation`, { timeout: 5000 }, async t => {
    let capturedSignal;
    let started;
    const invoked = new Promise(resolve => { started = resolve; });
    const { request } = await gateway(t, ({ signal }) => {
      capturedSignal = signal;
      return new Promise((resolve, reject) => {
        signal.addEventListener('abort', () => reject(new TranslationError('REQUEST_ABORTED', '本次翻译已取消。', 499)), { once: true });
        started();
      });
    });
    await request('/api/config', { method: 'POST', body: configured });
    const pending = request('/api/translate', { method: 'POST', body: { text: 'Still translating.', sourceLanguage: 'en' } });
    await invoked;
    assert.equal(capturedSignal.aborted, false);
    const changed = change === 'save'
      ? await request('/api/config', { method: 'POST', body: { mode: 'deepseek', model: 'deepseek-v4-pro' } })
      : await request('/api/config', { method: 'DELETE' });
    assert.equal(changed.status, 200);
    assert.equal(capturedSignal.aborted, true);
    const stopped = await pending;
    assert.equal(stopped.status, 499);
    assert.equal(stopped.data.code, 'REQUEST_ABORTED');
    assertNoKey(stopped);
  });
}

test('does not return an old translation when an injected provider ignores cancellation', { timeout: 5000 }, async t => {
  let release;
  let started;
  const invoked = new Promise(resolve => { started = resolve; });
  const { request } = await gateway(t, () => new Promise(resolve => {
    release = () => resolve({ text: '过期译文', model: DEFAULT_CONFIG.model });
    started();
  }));
  await request('/api/config', { method: 'POST', body: configured });
  const pending = request('/api/translate', { method: 'POST', body: { text: 'Old request.' } });
  await invoked;
  await request('/api/config', { method: 'POST', body: { mode: 'demo' } });
  release();
  const stale = await pending;
  assert.equal(stale.status, 409);
  assert.equal(stale.data.code, 'CONFIG_CHANGED');
  assert.equal(stale.raw.includes('过期译文'), false);
});

test('masks unexpected local errors without returning thrown key details', { timeout: 5000 }, async t => {
  const { request } = await gateway(t, async () => { throw new Error(`Private request detail: ${fakeKey}`); });
  await request('/api/config', { method: 'POST', body: configured });
  const failed = await request('/api/translate', { method: 'POST', body: { text: 'Hello.' } });
  assert.equal(failed.status, 500);
  assert.equal(failed.data.code, 'LOCAL_ERROR');
  assertNoKey(failed);
});
