'use strict';
const API = 'https://delivery-api-gusanova.msk1.amvera.tech';
const $ = (id) => document.getElementById(id);
const money = (v, currency='RUB') => new Intl.NumberFormat('ru-RU',{style:'currency',currency,maximumFractionDigits:2}).format(v);
const num = (v) => Number(v).toLocaleString('ru-RU',{maximumFractionDigits:2});
const escapeHtml = (s) => String(s).replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function request(path, options={}) {
  const controller = new AbortController(); const timer=setTimeout(()=>controller.abort(),18000);
  try {
    const res = await fetch(API+path,{...options,signal:controller.signal,headers:{'Accept':'application/json',...(options.body?{'Content-Type':'application/json'}:{}),...options.headers}});
    const text=await res.text(); let data;
    try{data=JSON.parse(text)}catch{data={error:text||'Пустой ответ сервера'}}
    if(!res.ok){const first=data.errors?Object.values(data.errors).flat().join('; '):'';throw new Error(first||data.error||`HTTP ${res.status}`)}
    return data;
  } catch(e) { if(e.name==='AbortError') throw new Error('Превышено время ожидания ответа сервера.'); if(e instanceof TypeError) throw new Error('Нет соединения с API. Проверьте интернет и состояние Amvera.');throw e; }
  finally {clearTimeout(timer)}
}
async function loadCities(){
  try{const cities=await request('/api/delivery/cities');const names=cities.map(c=>c.name);for(const id of ['fromCity','toCity']){$(id).replaceChildren(...names.map(n=>{const opt=document.createElement('option');opt.value=n;opt.textContent=n;return opt}))} $('fromCity').value=names.includes('Москва')?'Москва':names[0];$('toCity').value=names.includes('Нижний Новгород')?'Нижний Новгород':names[1]||names[0];}
  catch(e){$('feedback').textContent='Не удалось загрузить города: '+e.message;for(const id of ['fromCity','toCity']){$(id).innerHTML='<option value="">Сервис недоступен</option>'}}
}
const row=(key,value)=>`<div class="cost-row"><span>${key}</span><strong>${value}</strong></div>`;
function renderResult(data){
  const d=data.delivery,c=data.conversion;
  $('empty').hidden=true;$('computed').hidden=false;$('calc-status').textContent='Расчёт готов';$('calc-status').classList.add('ok');
  $('rub-amount').textContent=money(d.cost);$('distance').textContent=num(d.distanceKm)+' км';$('days').textContent=d.days+' дн.';$('chargeable').textContent=num(d.chargeableWeight)+' кг';$('delivery-type').textContent=d.type==='express'?'Экспресс':'Стандарт';
  $('foreign-amount').textContent=c.status==='ok'&&c.amount!==null?money(c.amount,c.currency):c.status==='not_requested'?money(d.cost):'Недоступно';
  $('exchange-status').textContent=c.status==='ok'?'Курс получен':c.status==='not_requested'?'Без конвертации':'Нет соединения';
  $('cost-list').innerHTML=row('Базовая ставка',money(d.baseFee))+row('Обработка веса',money(d.weightFee))+row('Расстояние',money(d.distanceFee))+row('Коэффициент доставки','× '+num(d.typeCoefficient))+row('Скидка','− '+money(d.discount))+row('Страхование','+ '+money(d.insuranceCost));
  $('rate-details').textContent=c.status==='ok'?`1 ${c.currency} = ${num(c.rate)} ₽ · дата курса: ${c.rateDate} · источник: ${c.source}`:'Доставка рассчитывается независимо от доступности курсов валют.';
  $('rate-warning').hidden=c.status!=='unavailable';$('rate-warning').textContent=c.warning||'';
  $('step-ui').textContent='●';$('step-ui').className='pipe-status good';$('step-api').textContent='●';$('step-api').className='pipe-status good';$('step-cbr').textContent=c.status==='ok'?'●':c.status==='unavailable'?'!':'—';$('step-cbr').className='pipe-status '+(c.status==='unavailable'?'warn':'good');
}
$('delivery-form').addEventListener('submit',async(e)=>{
  e.preventDefault();$('feedback').textContent='';const btn=$('submit');btn.disabled=true;btn.firstChild.textContent='Выполняется запрос… ';
  const payload={fromCity:$('fromCity').value,toCity:$('toCity').value,weight:Number($('weight').value),length:Number($('length').value||0),width:Number($('width').value||0),height:Number($('height').value||0),type:$('type').value,insurance:$('insurance').checked,currency:$('currency').value};
  try{const data=await request('/api/integration/quote',{method:'POST',body:JSON.stringify(payload)});renderResult(data)}catch(err){$('feedback').textContent=err.message;$('step-api').className='pipe-status warn';$('step-api').textContent='!';}finally{btn.disabled=false;btn.firstChild.textContent='Рассчитать доставку ';}
});
$('reload-history').addEventListener('click',async()=>{const area=$('history-content');area.textContent='Загрузка истории…';try{const list=await request('/api/delivery/history');if(!list.length){area.textContent='История пока пуста.';return}area.innerHTML='<table class="history-table"><thead><tr><th>Маршрут</th><th>Тип</th><th>Дата</th><th>Стоимость</th></tr></thead><tbody>'+list.slice(-15).reverse().map(x=>`<tr><td>${escapeHtml(x.fromCity)} → ${escapeHtml(x.toCity)}</td><td>${x.type==='express'?'Экспресс':'Стандарт'}</td><td>${new Date(x.calculatedAt).toLocaleString('ru-RU')}</td><td>${money(x.cost)}</td></tr>`).join('')+'</tbody></table>'}catch(e){area.textContent='Ошибка: '+e.message}});
loadCities();
