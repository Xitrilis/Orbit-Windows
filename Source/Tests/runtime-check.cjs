const {spawnSync}=require('node:child_process');
const result=spawnSync(process.env.ORBIT_PYTHON||'python',['-c',"import sys; sys.path.insert(0,'Source'); import Package; Package.verify(); print('PASS runtime integrity')"],{stdio:'inherit'});
if(result.error)throw result.error;
process.exit(result.status??1);
