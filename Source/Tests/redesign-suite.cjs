const {spawnSync}=require('node:child_process'),path=require('node:path');
// Current 0.18 checks supersede 0.11 shortcut and startup assumptions.
const checks=['redesign-ui','form-audit-ui','startup-ui'];
let failures=0;
for(const test of checks){const r=spawnSync(process.execPath,[path.join(__dirname,test+'.cjs')],{encoding:'utf8',timeout:90000});console.log(`${r.status===0?'PASS':'FAIL'} ${test}`);if(r.status!==0){failures++;console.log((r.stdout||'')+(r.stderr||'')+(r.error||''));}}
if(failures)process.exit(1);console.log('PASS redesign suite');
