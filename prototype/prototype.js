/* Reading prototype: optional model translation; screen capture remains a page demo. */
(() => {
  'use strict';
  const $ = (selector) => document.querySelector(selector);
  const samples = window.TRANSLATION_SAMPLES;
  const modes = { A: '悬浮译窗', B: '左右对照', C: '沉浸阅读' };
  const initialVariant = new URLSearchParams(location.search).get('variant');
  const state = { variant: modes[initialVariant] ? initialVariant : 'A', language: 'en', phase: 'ready', ids: [samples.en.paragraphs[1].id], result: null, error: '', pinned: false, closed: false, history: [], notice: '已载入英语示例。左键划选原文即可翻译，也可点击「框选翻译」。' };
  let job = 0;
  let pendingController = null;
  let manualController = null;
  let manualJob = 0;
  let drag = null;
  let toastTimer;
  let modalTrigger;
  const esc = (value) => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const icon = (name) => `<svg class="icon" aria-hidden="true"><use href="#i-${name}"/></svg>`;
  const selected = () => samples[state.language].paragraphs.filter(p => state.ids.includes(p.id));
  const isModelMode = () => window.TranslationService?.config.mode === 'deepseek';
  const languageLabel = language => samples[language]?.languageLabel || '自动识别';
  const resultLabel = result => result.mode === 'deepseek' ? `模型 · ${result.model || 'DeepSeek'}` : '预置示例';
  const phaseNames = { idle: '等待翻译', selecting: '正在框选', recognizing: '选取示例文字', translating: '正在翻译', ready: '译文已就绪', error: '翻译失败' };
  const wait = delay => new Promise(resolve => setTimeout(resolve, delay));
  const demoResult = () => ({ text: selected().map(p => p.translation).join('\n\n'), source: selected().map(p => p.text).join('\n\n'), language: state.language, mode: 'demo', model: '' });
  state.result = demoResult();

  function toast(message) {
    clearTimeout(toastTimer);
    $('#toast').textContent = message;
    $('#toast').hidden = false;
    toastTimer = setTimeout(() => { $('#toast').hidden = true; }, 3200);
  }

  function renderArticle() {
    const sample = samples[state.language];
    $('#sample-address').textContent = sample.site;
    $('#reader').lang = state.language;
    const lastSelected = selected().at(-1)?.id;
    $('#reader').innerHTML = `<div class="article-meta"><span>${esc(sample.eyebrow)}</span><span>${esc(sample.meta)}</span></div><h2 class="article-title">${esc(sample.title)}</h2><p class="article-deck">${esc(sample.description)}</p><div class="article-rule"></div>${sample.paragraphs.map((p, i) => {
      let inline = '';
      if (state.result && state.ids.includes(p.id) && state.phase === 'ready' && !state.closed) {
        if (state.result.mode === 'demo') inline = `<div class="inline-translation" lang="zh-CN"><span>中文译文 · 预置示例</span><p>${esc(p.translation)}</p></div>`;
        else if (p.id === lastSelected) inline = `<div class="inline-translation" lang="zh-CN"><span>选区译文 · ${esc(resultLabel(state.result))}</span><p style="white-space:pre-wrap">${esc(state.result.text)}</p></div>`;
      }
      return `<div class="paragraph${state.ids.includes(p.id) ? ' selected' : ''}" data-id="${esc(p.id)}"><span class="paragraph-number" aria-hidden="true">0${i + 1}</span><p class="paragraph-text">${esc(p.text)}</p>${inline}</div>`;
    }).join('')}<div class="article-end">— &nbsp; ${esc(sample.languageCode)} / 阅读示例 &nbsp; —</div>`;
  }

  function renderResult() {
    const items = selected();
    const pending = ['recognizing', 'translating'].includes(state.phase);
    $('#translation-panel').hidden = state.closed;
    $('#reopen-result').hidden = !state.closed;
    $('#translation-panel').classList.toggle('pinned', state.pinned);
    $('#pin-button').setAttribute('aria-pressed', String(state.pinned));
    $('#pin-button').title = state.pinned ? '取消固定译窗（仅本页）' : '固定译窗（仅本页）';
    $('#pin-button').setAttribute('aria-label', $('#pin-button').title);
    $('#translation-subtitle').textContent = state.pinned ? '译窗已固定 · 仅本页有效' : '读懂，然后继续。';
    if (pending) {
      $('#translation-body').innerHTML = `<div class="result-language">${esc(samples[state.language].languageLabel)} ${icon('arrow')} 简体中文</div><div class="loading-state"><span class="loading-dots">•••</span><h3>${state.phase === 'recognizing' ? '正在选取示例文字' : '正在生成中文译文'}</h3><p>${isModelMode() ? '正在调用已配置的模型 API…' : '正在载入预置示例译文…'}</p></div>`;
    } else if (state.phase === 'error') {
      $('#translation-body').innerHTML = `<div class="empty-state"><h3>翻译未完成</h3><p role="alert">${esc(state.error)}</p><p>可重新框选重试，或在 API 设置中检查配置。</p></div>`;
    } else if (!state.result) {
      $('#translation-body').innerHTML = `<div class="empty-state">${icon('scan')}<h3>选一段，让阅读继续</h3><p>点击「框选翻译」，在左侧示例正文中拖动，或直接点击一个段落。</p></div>`;
    } else {
      const result = state.result;
      $('#translation-body').innerHTML = `<div class="result-language"><span>${esc(languageLabel(result.language))}</span>${icon('arrow')}<strong>简体中文</strong><span class="result-sample-label">${esc(resultLabel(result))}</span></div><div class="result-title">${icon('check')} ${items.length ? `已翻译 ${items.length} 段选区文字` : '文本译文'}</div><p class="translated-paragraph" style="white-space:pre-wrap">${esc(result.text)}</p><details class="source-details"><summary>查看原文 <span>${esc(result.language.toUpperCase())}</span></summary><div class="source-preview" lang="${esc(result.language)}"><p style="white-space:pre-wrap">${esc(result.source)}</p></div></details>`;
    }
    const hasResult = !!state.result && state.phase === 'ready';
    $('#copy-button').disabled = !hasResult;
    $('#immersive-copy').disabled = !hasResult;
    $('#immersive-toggle').disabled = !hasResult || !items.length;
    $('#immersive-toggle').textContent = state.closed ? '展开中文' : '收起中文';
    const note = $('.translation-note');
    if (note) note.textContent = state.result && hasResult ? (state.result.mode === 'deepseek' ? `译文来自 ${state.result.model || '已配置模型'}，请结合原文阅读。` : '预置示例译文，用于体验阅读布局。') : (isModelMode() ? '选中文字会发送至已配置的模型 API。' : '当前为预置示例模式，可在 API 设置中启用真实翻译。');
  }

  function render() {
    document.dispatchEvent(new Event('prototype:selection-clear'));
    document.body.dataset.variant = state.variant;
    document.body.classList.toggle('is-selecting', state.phase === 'selecting');
    $('#reading-workspace').classList.toggle('selecting', state.phase === 'selecting');
    $('#capture-button').innerHTML = `${icon(state.phase === 'selecting' ? 'close' : 'scan')}<span>${state.phase === 'selecting' ? '取消框选' : '框选翻译'}</span><kbd>${state.phase === 'selecting' ? 'Esc' : 'Alt Q'}</kbd>`;
    $('#status-text').innerHTML = `<i class="status-dot"></i>${esc(state.notice)}`;
    $('#mode-label').textContent = modes[state.variant];
    $('#history-count').textContent = String(state.history.length);
    document.querySelectorAll('[data-language]').forEach(button => {
      const active = button.dataset.language === state.language;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    document.querySelectorAll('.variant-button').forEach(button => {
      const active = button.dataset.variant === state.variant;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    $('#prototype-state').textContent = `体验状态：${modes[state.variant]} / ${samples[state.language].languageLabel} → 中文 / ${phaseNames[state.phase]} / ${state.ids.length} 段 / ${state.closed ? '译文已收起' : '译文已展开'} / ${state.pinned ? '已固定' : '未固定'} · ${isModelMode() ? '模型 API 翻译' : '预置示例'}；本页划译和段落框选，文本翻译可粘贴内容`;
    renderArticle();
    renderResult();
  }

  function stopPending() {
    job += 1;
    pendingController?.abort();
    pendingController = null;
    drag = null;
    $('#selection-box').hidden = true;
    state.phase = state.result ? 'ready' : 'idle';
  }

  function startCapture() {
    if (state.phase === 'selecting') { cancelCapture(); return; }
    stopPending();
    state.phase = 'selecting';
    state.notice = '在示例正文上拖动框选，或点击一段文字；按 Esc 取消。';
    render();
    $('#reading-workspace').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function cancelCapture() {
    stopPending();
    state.notice = '已取消框选，保留上次的阅读位置。';
    render();
  }

  function addHistory(result, ids = []) {
    state.history.unshift({ id: `${Date.now()}-${++historySequence}`, language: result.language, ids: [...ids], result: { ...result }, time: new Date().toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }) });
    $('#history-count').textContent = String(state.history.length);
  }

  let historySequence = 0;

  async function translate(ids) {
    stopPending();
    const request = ++job;
    const controller = new AbortController();
    pendingController = controller;
    state.ids = [...ids];
    state.result = null;
    state.error = '';
    state.closed = false;
    const language = state.language;
    const source = selected().map(p => p.text).join('\n\n');
    state.phase = 'translating';
    state.notice = '正在准备翻译…';
    render();
    try {
      await window.TranslationService?.ready;
      if (job !== request || controller.signal.aborted) return;
      const modelMode = isModelMode();
      state.phase = modelMode ? 'translating' : 'recognizing';
      state.notice = modelMode ? `已选取 ${ids.length} 段原文，正在请求模型翻译…` : `已框选 ${ids.length} 段，正在选取示例文字…`;
      render();
      let result;
      if (modelMode) {
        const response = await window.TranslationService.translate(source, language, { signal: controller.signal });
        result = { text: response.text, source, language, mode: 'deepseek', model: response.model };
      } else {
        await wait(360);
        if (job !== request || controller.signal.aborted) return;
        state.phase = 'translating';
        state.notice = '原文已选取，正在载入预置中文译文…';
        render();
        await wait(420);
        result = demoResult();
      }
      if (job !== request || controller.signal.aborted) return;
      state.result = result;
      state.phase = 'ready';
      state.notice = modelMode ? '已显示模型中文译文。可以继续框选下一段。' : '已显示预置中文译文。可以继续框选下一段。';
      addHistory(result, ids);
      render();
    } catch (error) {
      if (job !== request || controller.signal.aborted) return;
      state.phase = 'error';
      state.error = error.message || '暂时无法完成翻译，请稍后重试。';
      state.notice = '模型翻译未完成，请查看错误提示。';
      render();
    } finally {
      if (pendingController === controller) pendingController = null;
    }
  }

  function changeLanguage(language) {
    if (language === state.language) return;
    stopPending();
    state.language = language;
    state.ids = [];
    state.result = null;
    state.error = '';
    state.phase = 'idle';
    state.closed = false;
    state.notice = `已切换${samples[language].languageLabel}示例。左键划选原文，松开即显示中文。`;
    render();
  }

  function setVariant(variant, writeUrl = true) {
    if (!modes[variant]) return;
    stopPending();
    state.variant = variant;
    state.notice = variant === 'C' ? '沉浸阅读：框选按段落对照；左键划选文字，译窗自动出现在旁边。' : `${modes[variant]}：左键划选文字即可翻译，也可点击「框选翻译」。`;
    if (writeUrl) {
      const url = new URL(location.href);
      url.searchParams.set('variant', variant);
      try { history.replaceState(null, '', url); } catch (_) { /* file:// can restrict history updates. */ }
    }
    render();
  }

  function moveVariant(offset) {
    const variants = Object.keys(modes);
    setVariant(variants[(variants.indexOf(state.variant) + offset + variants.length) % variants.length]);
  }

  function openModal(title, content) {
    document.dispatchEvent(new Event('prototype:selection-clear'));
    if (state.phase === 'selecting') cancelCapture();
    if (!$('#modal-backdrop').hidden) document.dispatchEvent(new Event('prototype:modal-closed'));
    else modalTrigger = document.activeElement;
    $('#modal-title').textContent = title;
    $('#modal-body').innerHTML = content;
    $('#modal-backdrop').hidden = false;
    document.body.classList.add('modal-open');
    $('#close-modal').focus();
  }

  function closeModal() {
    document.dispatchEvent(new Event('prototype:modal-closed'));
    $('#modal-backdrop').hidden = true;
    $('#modal-body').replaceChildren();
    document.body.classList.remove('modal-open');
    modalTrigger?.focus();
  }

  document.addEventListener('prototype:open-modal', event => openModal(event.detail.title, event.detail.html));
  document.addEventListener('prototype:close-modal', closeModal);
  document.addEventListener('prototype:modal-closed', () => {
    manualJob += 1;
    manualController?.abort();
    manualController = null;
  });

  function openManual() {
    openModal('文本翻译', `<p class="modal-intro">粘贴英文或俄文，阅读对应的中文译文。</p><div class="notice" id="manual-mode-note">${isModelMode() ? '使用已配置的模型 API。提交的文字会发送给该服务。' : '当前使用预置示例。可加载示例，或在 API 设置中启用任意文本翻译。'}</div><label class="field-label" for="manual-text">待翻译文本 <span>英语 / 俄语 → 简体中文</span></label><textarea id="manual-text" rows="6" placeholder="粘贴英文、俄文，或点击下方按钮加载示例…"></textarea><div class="sample-buttons"><span>试试看</span><button class="text-button" data-snippet="en">加载英语示例</button><button class="text-button" data-snippet="ru">加载俄语示例</button></div><p class="field-message" id="manual-message" role="status" aria-live="polite"></p><div id="manual-result" hidden><label class="field-label" for="manual-translation">中文译文 <span id="manual-result-model"></span></label><textarea id="manual-translation" rows="6" readonly></textarea><button class="text-button" id="manual-copy" disabled>复制译文</button></div><div class="form-actions"><button class="secondary-button" id="manual-cancel">关闭</button><button class="primary-action" id="manual-submit">翻译成中文 ${icon('arrow')}</button></div>`);
    let currentResult = null;
    const clearManualResult = () => {
      manualJob += 1;
      manualController?.abort();
      manualController = null;
      currentResult = null;
      $('#manual-result').hidden = true;
      $('#manual-translation').value = '';
      $('#manual-copy').disabled = true;
      $('#manual-submit').disabled = false;
      $('#manual-submit').textContent = '翻译成中文';
      $('#manual-message').textContent = '';
    };
    $('#manual-text').addEventListener('input', clearManualResult);
    document.querySelectorAll('[data-snippet]').forEach(button => button.addEventListener('click', () => {
      clearManualResult();
      $('#manual-text').value = samples[button.dataset.snippet].paragraphs[2].text;
      $('#manual-message').textContent = `已加载${samples[button.dataset.snippet].languageLabel}示例。`;
    }));
    $('#manual-cancel').addEventListener('click', closeModal);
    $('#manual-copy').addEventListener('click', async () => {
      if (!currentResult) return;
      try { await navigator.clipboard.writeText(currentResult.text); toast('中文译文已复制。'); }
      catch (_) { $('#manual-translation')?.select(); toast('请按 Ctrl C 复制选中的译文。'); }
    });
    $('#manual-submit').addEventListener('click', async () => {
      const normalize = str => str.trim().replace(/\s+/g, ' ');
      const text = $('#manual-text').value.trim();
      if (!text) { $('#manual-message').textContent = '请先输入文字，或加载一段示例。'; return; }
      clearManualResult();
      const preparation = ++manualJob;
      $('#manual-message').textContent = '正在读取 API 配置…';
      try { await window.TranslationService?.ready; }
      catch (error) {
        if (preparation === manualJob && $('#manual-message')) $('#manual-message').textContent = error.message || '无法读取 API 配置，请重试。';
        return;
      }
      if (preparation !== manualJob || !$('#manual-text')) return;
      if (!isModelMode()) {
        for (const language of ['en', 'ru']) {
          const match = samples[language].paragraphs.find(p => normalize(p.text) === normalize(text));
          if (match) {
            closeModal();
            state.language = language;
            translate([match.id]);
            return;
          }
        }
        $('#manual-message').textContent = '这段文字没有预置译文。请加载示例，或在 API 设置中启用模型翻译。';
        return;
      }
      clearManualResult();
      const request = ++manualJob;
      const controller = new AbortController();
      manualController = controller;
      $('#manual-message').textContent = '正在请求模型翻译…';
      $('#manual-submit').disabled = true;
      $('#manual-submit').textContent = '正在翻译…';
      try {
        const response = await window.TranslationService.translate(text, 'auto', { signal: controller.signal });
        if (request !== manualJob || controller.signal.aborted || !$('#manual-text')) return;
        const language = /[А-Яа-яЁё]/.test(text) ? 'ru' : 'en';
        currentResult = { text: response.text, source: text, language, mode: 'deepseek', model: response.model };
        $('#manual-translation').value = response.text;
        $('#manual-result-model').textContent = resultLabel(currentResult);
        $('#manual-result').hidden = false;
        $('#manual-copy').disabled = false;
        $('#manual-message').textContent = '译文已生成，可复制或修改原文后重新翻译。';
        addHistory(currentResult);
      } catch (error) {
        if (request !== manualJob || controller.signal.aborted || !$('#manual-message')) return;
        $('#manual-message').textContent = error.message || '翻译失败，请检查 API 设置后重试。';
      } finally {
        if (manualController === controller) manualController = null;
        if (request === manualJob && $('#manual-submit')) {
          $('#manual-submit').disabled = false;
          $('#manual-submit').textContent = '重新翻译';
        }
      }
    });
  }

  function openHistory() {
    openModal('本次翻译记录', `<p class="modal-intro">这个页面里的阅读片段，刷新后自动清空。</p>${state.history.length ? `<div class="history-list">${state.history.map(item => {
      return `<button class="history-item" data-history="${esc(item.id)}"><span class="history-meta"><strong>${esc(languageLabel(item.language))} → 中文</strong><span>${esc(resultLabel(item.result))} · ${item.time}</span></span><span class="history-source">${esc(item.result.source)}</span><span class="history-translation">${esc(item.result.text)}</span></button>`;
    }).join('')}</div><div class="form-actions"><button class="text-button" id="clear-history">清空本次记录</button></div>` : `<div class="empty-state">${icon('history')}<h3>阅读从第一段开始</h3><p>完成一次框选翻译后，记录会出现在这里。</p></div>`}`);
    document.querySelectorAll('[data-history]').forEach(button => button.addEventListener('click', () => {
      const item = state.history.find(entry => entry.id === button.dataset.history);
      if (!item.ids.length) {
        openModal('文本翻译记录', `<p class="modal-intro">${esc(languageLabel(item.language))} → 中文 · ${esc(resultLabel(item.result))}</p><label class="field-label" for="history-source">原文</label><textarea id="history-source" rows="5" readonly>${esc(item.result.source)}</textarea><label class="field-label" for="history-result">中文译文</label><textarea id="history-result" rows="6" readonly>${esc(item.result.text)}</textarea><div class="form-actions"><button class="secondary-button" id="history-back">返回记录</button><button class="primary-action" id="history-copy">复制译文</button></div>`);
        $('#history-back').addEventListener('click', openHistory);
        $('#history-copy').addEventListener('click', async () => {
          try { await navigator.clipboard.writeText(item.result.text); toast('中文译文已复制。'); }
          catch (_) { $('#history-result')?.select(); toast('请按 Ctrl C 复制选中的译文。'); }
        });
        return;
      }
      stopPending();
      state.language = item.language;
      state.ids = [...item.ids];
      state.result = { ...item.result };
      state.error = '';
      state.phase = 'ready';
      state.closed = false;
      state.notice = '已恢复这次翻译的原文和中文。';
      closeModal();
      render();
    }));
    $('#clear-history')?.addEventListener('click', () => { state.history = []; render(); openHistory(); });
  }

  function openHelp() {
    openModal('把注意力留给内容', `<p class="modal-intro">这个原型用于比较：翻译出现在什么位置，最不打断你的阅读？</p><div class="help-layouts"><p><strong>A · 悬浮译窗</strong><br>译文贴近阅读区域，适合偶尔查看一小段。</p><p><strong>B · 左右对照</strong><br>原文与中文各占一侧，适合反复核对。</p><p><strong>C · 沉浸阅读</strong><br>中文嵌入选区下方，模型多段翻译作为一个整体显示。</p></div><div class="notice"><strong>当前可体验的范围</strong><p>英语、俄语 → 简体中文；本页划译与段落框选、译窗收起与固定、原文对照、复制译文和本次记录。</p><p>在 API 设置中选择 DeepSeek 并填写配置后，划译、框选和文本输入会调用模型；文本输入支持任意英文或俄文。预置模式仍可离线体验示例。</p><p>框选和快捷键仅作用于本页。尚未接入系统屏幕截图、OCR 和全局取词；模型模式下仅提交所选或所输入的文字到已配置服务。</p></div><p class="help-shortcuts"><kbd>Alt Q</kbd> 开始框选　<kbd>Esc</kbd> 取消 / 关闭　<kbd>←</kbd> <kbd>→</kbd> 切换布局</p>`);
  }

  $('#capture-button').addEventListener('click', startCapture);
  $('#recapture-button').addEventListener('click', startCapture);
  $('#nav-workspace').addEventListener('click', () => { $('#reader').scrollIntoView({ behavior: 'smooth', block: 'start' }); });
  $('#nav-manual').addEventListener('click', openManual);
  $('#nav-history').addEventListener('click', openHistory);
  $('#nav-help').addEventListener('click', () => {
    openHelp();
    $('#modal-body').insertAdjacentHTML('afterbegin', `<div class="notice"><strong>划译已默认开启</strong><p>在标题或原文中按住左键划选，松开后自动在选区附近显示中文。三种布局均可使用；滚动时跟随选区，点击其他位置或按 Esc 收起。</p><p>${isModelMode() ? '当前使用已配置的模型 API 翻译选中文字。' : '当前预置模式使用示例单词、短语和整句译文；其他组合显示词义参考。可在 API 设置中启用模型。'}</p></div>`);
  });
  $('#close-modal').addEventListener('click', closeModal);
  $('#modal-backdrop').addEventListener('click', event => { if (event.target === $('#modal-backdrop')) closeModal(); });
  $('#pin-button').addEventListener('click', () => { state.pinned = !state.pinned; render(); toast(state.pinned ? '译窗已固定，点击文章留白时保持展开。' : '已取消固定，点击文章留白可收起译窗。'); });
  $('#close-result').addEventListener('click', () => { state.closed = true; render(); });
  $('#reopen-result').addEventListener('click', () => { state.closed = false; render(); });
  async function copyTranslation() {
    if (!state.result || state.phase !== 'ready') return;
    const text = state.result.text;
    try { await navigator.clipboard.writeText(text); toast('中文译文已复制。'); }
    catch (_) { openModal('复制译文', `<p class="modal-intro">浏览器未允许自动复制，请选择下方文字后按 Ctrl C。</p><textarea id="copy-fallback" rows="8" readonly>${esc(text)}</textarea>`); $('#copy-fallback').select(); }
  }
  $('#copy-button').addEventListener('click', copyTranslation);
  $('#immersive-copy').addEventListener('click', copyTranslation);
  $('#immersive-toggle').addEventListener('click', () => { state.closed = !state.closed; render(); });
  $('#reset-button').addEventListener('click', () => { stopPending(); state.ids = isModelMode() ? [] : [samples[state.language].paragraphs[1].id]; state.result = isModelMode() ? null : demoResult(); state.error = ''; state.phase = state.result ? 'ready' : 'idle'; state.closed = false; state.pinned = false; state.notice = '已重置阅读位置；本次翻译记录仍保留。'; render(); });
  document.querySelectorAll('[data-language]').forEach(button => button.addEventListener('click', () => changeLanguage(button.dataset.language)));
  document.querySelectorAll('.variant-button').forEach(button => button.addEventListener('click', () => setVariant(button.dataset.variant)));
  $('#previous-variant').addEventListener('click', () => moveVariant(-1));
  $('#next-variant').addEventListener('click', () => moveVariant(1));
  window.addEventListener('popstate', () => setVariant(new URLSearchParams(location.search).get('variant') || 'A', false));
  document.addEventListener('prototype:text-selection-start', () => {
    // Keep the original DOM stable while the browser is creating a native text Range.
    if (['recognizing', 'translating'].includes(state.phase)) {
      job += 1;
      pendingController?.abort();
      pendingController = null;
      state.phase = state.result ? 'ready' : 'idle';
      state.notice = '已取消上次框选请求，正在划选原文。';
      $('#status-text').textContent = state.notice;
      renderResult();
    }
  });

  $('#reader').addEventListener('pointerdown', event => {
    if (state.phase !== 'selecting' || event.button !== 0) return;
    event.preventDefault();
    const bounds = $('#reading-workspace').getBoundingClientRect();
    drag = { x: event.clientX, y: event.clientY, bounds };
    $('#reader').setPointerCapture(event.pointerId);
    $('#selection-box').hidden = false;
    Object.assign($('#selection-box').style, { left: `${event.clientX - bounds.left}px`, top: `${event.clientY - bounds.top}px`, width: '0px', height: '0px' });
  });
  $('#reader').addEventListener('pointermove', event => {
    if (!drag) return;
    Object.assign($('#selection-box').style, { left: `${Math.min(event.clientX, drag.x) - drag.bounds.left}px`, top: `${Math.min(event.clientY, drag.y) - drag.bounds.top}px`, width: `${Math.abs(event.clientX - drag.x)}px`, height: `${Math.abs(event.clientY - drag.y)}px` });
  });
  $('#reader').addEventListener('pointerup', event => {
    if (!drag) return;
    const area = { left: Math.min(drag.x, event.clientX), right: Math.max(drag.x, event.clientX), top: Math.min(drag.y, event.clientY), bottom: Math.max(drag.y, event.clientY) };
    const tiny = area.right - area.left < 6 && area.bottom - area.top < 6;
    const hits = [...document.querySelectorAll('.paragraph-text')].filter(paragraph => {
      const box = paragraph.getBoundingClientRect();
      return tiny ? event.clientX >= box.left && event.clientX <= box.right && event.clientY >= box.top && event.clientY <= box.bottom : area.right > box.left && area.left < box.right && area.bottom > box.top && area.top < box.bottom;
    }).map(p => p.parentElement.dataset.id);
    drag = null;
    $('#selection-box').hidden = true;
    if (hits.length) translate(hits);
    else toast('这个区域没有示例正文，请重新框选文字。');
  });
  $('#reader').addEventListener('pointercancel', () => { drag = null; $('#selection-box').hidden = true; });
  $('#reader').addEventListener('click', event => {
    if (!window.getSelection()?.isCollapsed && window.getSelection()?.toString().trim()) return;
    if (state.phase === 'ready' && !state.pinned && !event.target.closest('.paragraph') && !state.closed) { state.closed = true; render(); }
  });

  document.addEventListener('keydown', event => {
    const modalOpen = !$('#modal-backdrop').hidden;
    if (event.key === 'Escape') {
      if (modalOpen) closeModal();
      else if (state.phase === 'selecting') cancelCapture();
      return;
    }
    if (modalOpen) {
      if (event.key === 'Tab') {
        const focusable = [...$('#modal').querySelectorAll('button:not([disabled]),textarea,input,select,a[href]')];
        const first = focusable[0]; const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
      return;
    }
    if (event.target.closest('input,textarea,select,[contenteditable="true"]')) return;
    if (event.altKey && event.code === 'KeyQ') { event.preventDefault(); startCapture(); }
    else if (event.key === 'ArrowLeft') { event.preventDefault(); moveVariant(-1); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); moveVariant(1); }
  });

  function resetForConfigChange() {
    stopPending();
    manualJob += 1;
    manualController?.abort();
    manualController = null;
    state.ids = [];
    state.result = null;
    state.error = '';
    state.phase = 'idle';
    state.closed = false;
    state.notice = isModelMode() ? '模型翻译已启用。请重新划选或框选文字，也可粘贴任意英文、俄文。' : '已切换为预置示例模式。请重新选择一段示例文字。';
    $('#manual-text')?.dispatchEvent(new Event('input'));
    if ($('#manual-mode-note')) $('#manual-mode-note').textContent = isModelMode() ? '使用已配置的模型 API。提交的文字会发送给该服务。' : '当前使用预置示例。可加载示例，或在 API 设置中启用任意文本翻译。';
    render();
  }

  document.addEventListener('translation:config-changed', resetForConfigChange);
  render();
  Promise.resolve(window.TranslationService?.ready).then(() => {
    if (isModelMode() && state.result?.mode === 'demo') resetForConfigChange();
  }).catch(() => {
    state.notice = 'API 配置暂未就绪，请在 API 设置中检查。';
    $('#status-text').textContent = state.notice;
  });
})();
