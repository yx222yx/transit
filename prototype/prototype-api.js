/* Client-side settings and transport. API keys are never stored in browser storage. */
(() => {
  'use strict';
  const service = {config:{mode:'demo',baseUrl:'https://api.deepseek.com',model:'deepseek-v4-flash',timeoutMs:30000,hasKey:false,storage:'session'}};
  const token = document.querySelector('meta[name="suiyi-session"]')?.content;
  const escape = value => String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  async function request(path,method='GET',value,signal) {
    if (!token || !/^https?:$/.test(location.protocol)) throw new Error('请运行 npm run prototype，并通过本机网址打开页面后配置 API。');
    let response;
    try { response=await fetch(path,{method,headers:{'Content-Type':'application/json','X-Suiyi-Session':token},body:value===undefined ? undefined : JSON.stringify(value),signal}); }
    catch(error) { if(error.name==='AbortError') throw error; throw new Error('无法连接本机翻译服务，请确认服务正在运行。'); }
    const result=await response.json().catch(()=>({message:'本机服务返回了无法读取的响应。'}));
    if(!response.ok) throw Object.assign(new Error(result.message || '请求失败，请重试。'),{code:result.code});
    return result;
  }
  function publish(config) {
    service.config=config;
    const enabled=config.mode==='deepseek';
    const badge=document.querySelector('#engine-status');
    if(badge) badge.textContent=enabled ? `DeepSeek · ${config.model}` : '示例词库 · 离线';
    document.querySelector('#nav-api-label').textContent=enabled ? 'DeepSeek 已启用' : 'API 配置';
    document.body.dataset.engine=config.mode;
    document.dispatchEvent(new CustomEvent('translation:config-changed',{detail:config}));
  }
  service.ready = request('/api/config').then(publish).catch(()=>{});
  service.translate = async (text,sourceLanguage='auto',{signal}={}) => {
    await service.ready;
    return request('/api/translate','POST',{text,sourceLanguage},signal);
  };
  service.openSettings = async () => {
    await service.ready;
    const cfg=service.config;
    document.dispatchEvent(new CustomEvent('prototype:open-modal',{detail:{title:'翻译引擎 · DeepSeek',html:`<p class="modal-intro">选择示例词库，或配置 DeepSeek 使用真实模型翻译。</p><form id="api-form"><label class="field-label" for="api-mode">翻译方式</label><select id="api-mode"><option value="demo" ${cfg.mode==='demo'?'selected':''}>示例词库（离线）</option><option value="deepseek" ${cfg.mode==='deepseek'?'selected':''}>DeepSeek API</option></select><div class="api-fields"><label class="field-label" for="api-base">API Base URL</label><input id="api-base" value="${escape(cfg.baseUrl)}" spellcheck="false" autocomplete="off"><p class="field-hint">DeepSeek 官方模板，支持 https://api.deepseek.com 或 /v1。</p><label class="field-label" for="api-model">模型名称</label><input id="api-model" list="deepseek-models" value="${escape(cfg.model)}" spellcheck="false"><datalist id="deepseek-models"><option value="deepseek-v4-flash"><option value="deepseek-v4-pro"></datalist><p class="field-hint">默认 Flash；翻译使用非思考模式，减少等待。</p><label class="field-label" for="api-key">API Key <span>${cfg.hasKey?'已在本次服务中配置；留空保留原 Key':''}</span></label><input id="api-key" type="password" autocomplete="off" placeholder="在这里填写你的 DeepSeek API Key" spellcheck="false"><label class="field-label" for="api-timeout">请求超时（秒）</label><input id="api-timeout" type="number" min="5" max="45" value="${cfg.timeoutMs/1000}"></div><div class="notice"><strong>Key 仅在本次本机服务运行中保留。</strong><p>启用后，划选、框选或手动提交的原文会发送给 DeepSeek，并按你的账户计费。连接测试只发送固定示例句。</p></div><p class="field-message" id="api-feedback" role="status"></p><div class="form-actions"><button class="text-button" type="button" id="api-clear">清除 Key</button><button class="secondary-button" type="button" id="api-test">测试连接</button><button class="primary-action" type="submit">保存并应用</button></div></form>`}}));
    const form=document.querySelector('#api-form');
    if (!cfg.hasKey) form.querySelector('#api-mode').value='deepseek';
    const feedback=document.querySelector('#api-feedback');
    const controller=new AbortController();
    const read=()=>({mode:form.querySelector('#api-mode').value,baseUrl:form.querySelector('#api-base').value,model:form.querySelector('#api-model').value,apiKey:form.querySelector('#api-key').value,timeoutMs:Number(form.querySelector('#api-timeout').value)*1000});
    const busy=value=>form.querySelectorAll('button').forEach(button=>button.disabled=value);
    const clearInputs=()=>{controller.abort();form.querySelector('#api-key').value='';document.removeEventListener('prototype:modal-closed',clearInputs);};
    document.addEventListener('prototype:modal-closed',clearInputs);
    form.addEventListener('submit',async event=>{
      event.preventDefault();busy(true);feedback.textContent='正在保存配置…';
      try {publish(await request('/api/config','POST',read(),controller.signal));form.querySelector('#api-key').value='';document.dispatchEvent(new Event('prototype:close-modal'));}
      catch(error){if(error.name!=='AbortError') feedback.textContent=error.message;}
      finally{busy(false);}
    });
    document.querySelector('#api-test').addEventListener('click',async()=>{
      busy(true);feedback.textContent='正在用固定示例句测试 DeepSeek…';
      try {const result=await request('/api/test','POST',read(),controller.signal);feedback.textContent=`连接成功：${result.text}（${result.model}）`;}catch(error){if(error.name!=='AbortError') feedback.textContent=error.message;}finally{busy(false);}
    });
    document.querySelector('#api-clear').addEventListener('click',async()=>{
      busy(true);
      try {publish(await request('/api/config','DELETE',undefined,controller.signal));form.querySelector('#api-key').value='';form.querySelector('#api-mode').value='demo';feedback.textContent='Key 已清除，已切换为示例词库。';}catch(error){if(error.name!=='AbortError') feedback.textContent=error.message;}finally{busy(false);}
    });
  };
  window.TranslationService=service;
  document.querySelector('#nav-api').addEventListener('click',service.openSettings);
})();
