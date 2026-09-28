import json, os, subprocess, time, uuid, sys
from pathlib import Path
from datetime import datetime

ROOT=Path(__file__).resolve().parents[1]
DATA=Path(os.environ.get('WISADEL_DATA',str(Path(os.environ.get('LOCALAPPDATA',str(Path.home())))/'WisadelCompanion')))
STATS=Path(os.environ.get('WISADEL_STATS_PATH',str(DATA/'stats.json')))
DEFAULT={'paused':False,'overlay_enabled':True,'autostart':True,'display_period':'total'}

def read(path,default):
    try:return json.loads(Path(path).read_text(encoding='utf-8-sig'))
    except (OSError,ValueError):return default

def write(path,data):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
    temp.write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    os.replace(temp,path)

def preferences():return {**DEFAULT,**read(DATA/'settings.json',{}),'display_period':'total'}

def runtime():
    result=read(DATA/'runtime.json',{})
    try:age=time.time()-datetime.fromisoformat(result['updated'].replace('Z','+00:00')).timestamp()
    except (KeyError,ValueError):age=1e9
    result['running']=bool(result.get('pid')) and age<5
    return result

def start():
    if not preferences()['autostart'] or runtime()['running']:return
    exe=ROOT/'bin/WisadelCompanion.exe'
    env={**os.environ,'WISADEL_DATA':str(DATA),'WISADEL_STATS_PATH':str(STATS),'WISADEL_PYTHON_PATH':sys.executable}
    subprocess.Popen([str(exe)],env=env,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=subprocess.CREATE_NO_WINDOW|subprocess.DETACHED_PROCESS)

def counts():
    saved=read(STATS,None)
    if saved is None:saved=read(str(STATS)+'.bak',None)
    state=runtime()
    def clean(c):
        c=c or {};out={k:int(c.get(k,0)) for k in ['Keyboard','Left','Right','Middle','Side']}
        out['Mouse']=sum(out[k] for k in ['Left','Right','Middle','Side']);return out
    return {'today':clean((saved or {}).get('Days',{}).get(datetime.now().strftime('%Y-%m-%d'))),
            'total':clean((saved or {}).get('Total')),'session':clean(state.get('session')),
            'settings':preferences(),'runtime':state,'data_available':saved is not None,
            'quota':read(DATA/'quota.json',{}),
            'usage':read(DATA/'usage-summary.json',{}),
            'last_event':read(DATA/'hook-event.json',{}),'updated':datetime.now().isoformat(timespec='seconds')}

def set_preferences(values):
    if not values or set(values)-set(DEFAULT):raise ValueError('Unsupported preferences')
    for k,v in values.items():
        if k=='display_period':
            if v != 'total':raise ValueError('Keyboard/mouse display remains lifetime total; token and USD use a separate 04:00 daily window')
        elif not isinstance(v,bool):raise ValueError(k+' must be boolean')
    write(DATA/'settings.json',{**preferences(),**values})
    start()
    return counts()
