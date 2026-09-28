"""Only retain event type/time; discard prompt, transcript and session fields."""
import sys,json
from datetime import datetime,timezone
from common import DATA,start,write
from speech_events import emit

def main():
    try:
        payload=json.load(sys.stdin)
        event=payload.get('hook_event_name')
        if event not in ['SessionStart','UserPromptSubmit','Stop','Interrupt']:return
        stamp=datetime.now(timezone.utc).isoformat()
        write(DATA/'hook-event.json',{'event':event,'time':stamp})
        emit(DATA,event,stamp)
        start()
    except Exception as exc:
        # Hooks are advisory; never interrupt the user's Codex task.
        print('Wisadel companion: '+str(exc),file=sys.stderr)
if __name__=='__main__':main()
