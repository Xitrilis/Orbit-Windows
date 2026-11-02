(()=>{
 'use strict';
 const format=(value,currency)=>{
  if(!Number.isFinite(value)||value<0||! /^[A-Z]{3}$/.test(currency||''))return '—';
  try{return new Intl.NumberFormat('ru-RU',{style:'currency',currency,currencyDisplay:'narrowSymbol',minimumFractionDigits:0,maximumFractionDigits:2}).format(value);}catch{return '—';}
 };
 const equivalents=o=>o&&!o.free?['RUB','KZT'].filter(c=>c!==o.currency&&Number.isFinite(o[c==='RUB'?'rub':'kzt']))
  .map(c=>'≈ '+(o.from?'от ':'')+format(o[c==='RUB'?'rub':'kzt'],c)).join(' · '):'';
 window.OrbitMoney={format,equivalents};
})();
