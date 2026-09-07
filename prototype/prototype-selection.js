/* Native text selection with an explicit local-demo or DeepSeek translation engine. */
(() => {
  'use strict';
  const $ = selector => document.querySelector(selector);
  const reader = $('#reader');
  const popup = $('#selection-popover');
  const sourceSelector = '.paragraph-text, .article-title';
  const escape = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'}[c]));
  const tokenize = text => [...text.matchAll(/[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*|\d+/gu)].map(match => ({text:match[0], key:match[0].toLowerCase().replace(/’/g, "'"), start:match.index, end:match.index + match[0].length}));
  const key = text => tokenize(text).map(token => token.key).join(' ');
  const dictionaries = Object.fromEntries(['en', 'ru'].map(language => {
    const data = window.SELECTION_TRANSLATIONS[language];
    const phrases = [...window.TRANSLATION_SAMPLES[language].paragraphs, ...data.phrases].map(phrase => ({...phrase, tokens:tokenize(phrase.text).map(token => token.key)}));
    return [language, { words:data.words, exact:new Map(phrases.map(phrase => [key(phrase.text), phrase.translation])), phrases:phrases.sort((a,b) => b.tokens.length-a.tokens.length) }];
  }));
  let active = null;
  let selectingText = false;
  let generation = 0;
  let positionFrame = 0;

  function resolve(text, language) {
    const dictionary = dictionaries[language];
    const exact = dictionary.exact.get(key(text));
    if (exact) return {kind:'exact', translation:exact};
    const tokens = tokenize(text);
    if (tokens.length === 1 && dictionary.words[tokens[0].key]) return {kind:'word', translation:dictionary.words[tokens[0].key]};
    const pieces = [];
    for (let index = 0; index < tokens.length;) {
      const phrase = dictionary.phrases.find(candidate => candidate.tokens.length > 1 && candidate.tokens.every((token, offset) => token === tokens[index+offset]?.key));
      const count = phrase?.tokens.length || 1;
      const source = text.slice(tokens[index].start, tokens[index+count-1].end);
      pieces.push({source, translation:phrase?.translation || dictionary.words[tokens[index].key] || (/^\d+$/.test(source) ? source : null), sentence:!!phrase && /[.!?。！？]$/.test(phrase.text)});
      index += count;
    }
    if (pieces.length && pieces.every(piece => piece.sentence)) return {kind:'exact', translation:pieces.map(piece => piece.translation).join('\n')};
    return {kind:'reference', pieces, translation:pieces.filter(piece => piece.translation).map(piece => `${piece.source}：${piece.translation}`).join('\n')};
  }

  function dismiss() {
    generation += 1;
    selectingText = false;
    if (active) { clearTimeout(active.timer); active.controller?.abort(); }
    active = null;
    popup.hidden = true;
    document.body.classList.remove('has-selection-popover');
  }

  function selectedSources(range) {
    return [...reader.querySelectorAll(sourceSelector)].flatMap(element => {
      if (!range.intersectsNode(element)) return [];
      const part = document.createRange();
      part.selectNodeContents(element);
      if (range.compareBoundaryPoints(Range.START_TO_START, part) > 0) part.setStart(range.startContainer, range.startOffset);
      if (range.compareBoundaryPoints(Range.END_TO_END, part) < 0) part.setEnd(range.endContainer, range.endOffset);
      const text = part.toString().trim();
      return text && tokenize(text).length ? [{text, range:part}] : [];
    });
  }

  function position() {
    if (!active) return;
    if (!reader.contains(active.anchor.range.commonAncestorContainer)) { dismiss(); return; }
    const rects = [...active.anchor.range.getClientRects()].filter(rect => rect.width && rect.height);
    const anchor = rects[Math.min(active.anchor.line, rects.length-1)];
    const width = document.documentElement.clientWidth;
    const height = window.innerHeight;
    if (!anchor || anchor.bottom < 0 || anchor.top > height || anchor.right < 0 || anchor.left > width) {
      popup.hidden = true;
      document.body.classList.remove('has-selection-popover');
      return;
    }
    popup.hidden = false;
    document.body.classList.add('has-selection-popover');
    popup.style.maxHeight = `${Math.min(450, height-24)}px`;
    popup.style.left = '12px';
    popup.style.top = '12px';
    const box = popup.getBoundingClientRect();
    const switcher = $('.prototype-switcher').getBoundingClientRect();
    const bottom = switcher.top > box.height + 24 ? Math.min(height-12, switcher.top-12) : height-12;
    const gap = 10;
    const clamp = (value, min, max) => Math.max(min, Math.min(value, Math.max(min,max)));
    let left = clamp(anchor.left, 12, width-box.width-12);
    let top;
    let placement;
    if (anchor.bottom+gap+box.height <= bottom) { top=anchor.bottom+gap; placement='below'; }
    else if (anchor.top-gap-box.height >= 12) { top=anchor.top-gap-box.height; placement='above'; }
    else if (anchor.right+gap+box.width <= width-12) { left=anchor.right+gap; top=clamp(anchor.top,12,bottom-box.height); placement='right'; }
    else if (anchor.left-gap-box.width >= 12) { left=anchor.left-gap-box.width; top=clamp(anchor.top,12,bottom-box.height); placement='left'; }
    else { top=clamp(anchor.bottom+gap,12,bottom-box.height); placement='clamped'; }
    popup.style.left = `${Math.round(left)}px`;
    popup.style.top = `${Math.round(top)}px`;
    popup.dataset.placement = placement;
  }

  function schedulePosition() {
    cancelAnimationFrame(positionFrame);
    positionFrame = requestAnimationFrame(position);
  }

  function show(parts, point) {
    const language = reader.lang;
    if (!dictionaries[language]) return;
    const text = parts.map(part => part.text).join('\n');
    const engine = window.TranslationService?.config.mode || 'demo';
    const result = engine === 'deepseek' ? {kind:'loading',translation:''} : resolve(text, language);
    let anchor;
    let nearest = Infinity;
    parts.forEach(part => [...part.range.getClientRects()].forEach((rect,line) => {
      const distance = Math.hypot(Math.max(rect.left-point.x, 0, point.x-rect.right), Math.max(rect.top-point.y, 0, point.y-rect.bottom));
      if (distance < nearest) { nearest = distance; anchor = {range:part.range, line}; }
    }));
    if (!anchor) return;
    active = {text, result, anchor,language,engine};
    $('#selection-language').textContent = `${language === 'en' ? '英语' : '俄语'} → 简体中文`;
    $('#selection-source').textContent = text;
    $('#selection-source').lang = language;
    renderActive();
    if (engine === 'deepseek') requestModel(active);
  }

  function renderActive() {
    if (!active) return;
    const {result,language,engine}=active;
    $('#selection-feedback').textContent = engine === 'deepseek' ? `DeepSeek · ${window.TranslationService.config.model}` : '本页示例 · 预置译文';
    $('#selection-copy').disabled = !result.translation;
    if (result.kind === 'loading') {
      $('#selection-result').innerHTML='<div class="selection-result-label">正在翻译</div><p class="selection-translation">正在请求 DeepSeek…</p><p class="selection-reference-note">继续划选或关闭浮窗可取消本次等待。</p>';
    } else if (result.kind === 'error') {
      $('#selection-result').innerHTML=`<div class="selection-result-label">翻译未完成</div><p class="selection-error">${escape(result.message)}</p><button class="secondary-button" id="selection-retry">重试翻译</button>`;
      $('#selection-retry').addEventListener('click',()=>requestModel(active));
    } else if (result.kind === 'reference') {
      $('#selection-result').innerHTML = `<div class="selection-result-label">词义参考</div><dl class="selection-meanings">${result.pieces.map(piece => `<div><dt lang="${language}">${escape(piece.source)}</dt><dd${!piece.translation ? ' class="selection-missing"' : ''}>${escape(piece.translation || '未收录此片段，请选中完整单词')}</dd></div>`).join('')}</dl><p class="selection-reference-note">这个组合暂无整句预置译文，以上按选中文字显示词义。</p>`;
    } else {
      $('#selection-result').innerHTML = `<div class="selection-result-label">${result.kind === 'word' ? '中文词义' : '中文译文'}</div><p class="selection-translation">${escape(result.translation)}</p>`;
    }
    position();
  }

  function requestModel(current) {
    if (!current || active!==current) return;
    clearTimeout(current.timer);
    current.controller?.abort();
    current.controller=new AbortController();
    current.result={kind:'loading',translation:''};
    renderActive();
    current.timer=setTimeout(async()=>{
      try {
        const result=await window.TranslationService.translate(current.text,current.language,{signal:current.controller.signal});
        if(active!==current || current.controller.signal.aborted) return;
        current.result={kind:'model',translation:result.text};
      } catch(error) {
        if(active!==current || current.controller.signal.aborted) return;
        current.result={kind:'error',translation:'',message:error.message};
      }
      renderActive();
    },150);
  }

  document.addEventListener('pointerdown', event => {
    if (popup.contains(event.target)) return;
    if (event.button !== 0) return;
    dismiss();
    if (!$('#modal-backdrop').hidden || document.body.classList.contains('is-selecting')) return;
    selectingText = !!event.target.closest(sourceSelector) && reader.contains(event.target);
    if (selectingText) document.dispatchEvent(new Event('prototype:text-selection-start'));
  }, true);

  document.addEventListener('pointerup', event => {
    if (!selectingText || event.button !== 0) return;
    selectingText = false;
    const request = generation;
    const point = {x:event.clientX, y:event.clientY};
    requestAnimationFrame(() => {
      if (request !== generation || document.body.classList.contains('is-selecting') || !$('#modal-backdrop').hidden) return;
      const selection = window.getSelection();
      if (!selection?.rangeCount || selection.isCollapsed) return;
      const parts = selectedSources(selection.getRangeAt(0));
      if (parts.length) show(parts, point);
    });
  });
  document.addEventListener('pointercancel', () => { selectingText=false; });
  document.addEventListener('prototype:selection-clear', dismiss);
  document.addEventListener('translation:config-changed',dismiss);
  document.addEventListener('keydown', event => { if (event.key === 'Escape') dismiss(); });
  document.addEventListener('scroll', event => { if (!popup.contains(event.target)) schedulePosition(); }, true);
  window.addEventListener('resize', schedulePosition);
  $('#selection-close').addEventListener('click', dismiss);
  $('#selection-copy').addEventListener('click', async () => {
    if (!active?.result.translation) return;
    const current = active;
    try {
      await navigator.clipboard.writeText(current.result.translation);
      if (active === current) $('#selection-feedback').textContent = '中文已复制';
    } catch (_) {
      if (active === current) {
        $('#selection-feedback').textContent = '请手动选择中文后按 Ctrl C';
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents($('#selection-result'));
        selection.removeAllRanges();
        selection.addRange(range);
      }
    }
  });
})();
