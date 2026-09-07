// Manual browser verification only. Does not call DeepSeek; never use as the app launcher.
const {createPrototypeServer}=require('../prototype-server.cjs');
const server=createPrototypeServer({translate:async({text,config,signal})=>{
  if(config.apiKey==='test-invalid-key') throw Object.assign(new Error('API Key 无效或已失效，请重新配置。'),{status:401,code:'INVALID_CREDENTIALS'});
  await new Promise((resolve,reject)=>{
    const timer=setTimeout(resolve,text.includes('slow') ? 1500 : 250);
    signal.addEventListener('abort',()=>{clearTimeout(timer);reject(Object.assign(new Error('已取消'),{status:499,code:'REQUEST_ABORTED'}));},{once:true});
  });
  return {text:`【本机联调模拟响应】${text==='A new day begins.' ? '新的一天开始了。' : text==='familiar street' ? '熟悉的街道' : text.includes('Hello') ? '你好，很高兴认识你。' : '这段文字已经通过本机翻译接口。'}`,model:config.model};
}});
server.listen(4318,'127.0.0.1',()=>console.log('UI verification fixture (MOCK ONLY): http://127.0.0.1:4318/prototype.html'));
