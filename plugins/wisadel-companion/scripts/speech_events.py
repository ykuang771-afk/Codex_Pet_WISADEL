"""Small, short-lived event queue. Never retain prompts or transcript content."""
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import time
import uuid

def emit(data,event,stamp,identity=None):
    if event not in ('UserPromptSubmit','Stop','Interrupt'):return
    try:age=(datetime.now(timezone.utc)-datetime.fromisoformat(stamp.replace('Z','+00:00'))).total_seconds()
    except (ValueError,TypeError):return
    if age < -5 or age > 20:return
    folder=Path(data)/'speech-events';folder.mkdir(parents=True,exist_ok=True)
    identifier=hashlib.sha256(identity.encode()).hexdigest() if identity else uuid.uuid4().hex
    path=folder/(identifier+'.json')
    if path.exists():return
    temp=folder/(identifier+'.'+uuid.uuid4().hex+'.tmp')
    temp.write_text(json.dumps({'id':identifier,'event':event,'time':stamp}),encoding='utf-8')
    os.replace(temp,path)
    for old in folder.glob('*.json'):
        try:
            if time.time()-old.stat().st_mtime>60:old.unlink()
        except OSError:pass
