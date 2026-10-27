"""Rebuild the embedded manifest with the SDK or an ORBIT_CSC Roslyn compiler."""
import os, shutil, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parent.parent

def rebuild():
    import Package
    for row in (ROOT/'Notices/playnite-runtime.sha256').read_text().splitlines():
        digest,relative=row.split('  ',1)
        assert Package.sha(ROOT/'engine'/relative)==digest, 'Official runtime modified: '+relative
    paths=list((ROOT/'engine').rglob('*'))+[ROOT/'Orbit.exe.config',ROOT/'orbit.ico']
    rows=[Package.sha(p)+'\t'+str(p.stat().st_size)+'\t'+p.relative_to(ROOT).as_posix() for p in paths if Package.ship(p)]
    (ROOT/'RuntimeFiles.sha256').write_text('\n'.join(sorted(rows))+'\n',encoding='utf-8',newline='\n')
    output=ROOT/'qa/bootstrap-build'
    output.mkdir(parents=True,exist_ok=True)
    compiler=os.environ.get('ORBIT_CSC') or (str(ROOT/'qa/compiler/tasks/net472/csc.exe') if (ROOT/'qa/compiler/tasks/net472/csc.exe').exists() else None)
    project=ROOT/'Source/Orbit.Bootstrap.csproj'
    if compiler:
        xml=ET.parse(project)
        args=[compiler,'/nologo','/target:winexe','/platform:x64','/langversion:latest','/out:'+str(output/'Orbit.exe'),'/win32icon:'+str(ROOT/'orbit.ico'),'/resource:'+str(ROOT/'RuntimeFiles.sha256')+',Orbit.RuntimeFiles']
        args+=['/reference:'+r.attrib['Include']+'.dll' for r in xml.findall('.//Reference')]
        args+=[str(ROOT/'Source'/c.attrib['Include']) for c in xml.findall('.//Compile')]
        subprocess.run(args,cwd=ROOT,check=True)
    else:
        subprocess.run(['dotnet','build',str(project),'-c','Release','-o',str(output)],cwd=ROOT,check=True)
    built=output/'Orbit.exe'
    assert (ROOT/'RuntimeFiles.sha256').read_bytes() in built.read_bytes(), 'Missing embedded manifest'
    shutil.copy2(built,ROOT/'Orbit.exe.tmp')
    shutil.copy2(ROOT/'Orbit.exe',ROOT/'Orbit.exe.bak')
    os.replace(ROOT/'Orbit.exe.tmp',ROOT/'Orbit.exe')
    Package.verify()

if __name__=='__main__':
    rebuild()
