from datetime import datetime, timezone, timedelta
import json
from pathlib import Path
import tempfile
import unittest
from speech_events import emit
from usage_ledger import Ledger

class SpeechEventTests(unittest.TestCase):
    def test_queue_privacy_freshness_and_dedup(self):
        with tempfile.TemporaryDirectory() as temp:
            now=datetime.now(timezone.utc)
            for _ in range(2):emit(temp,'Stop',now.isoformat(),'same-identity')
            emit(temp,'Stop',(now-timedelta(minutes=1)).isoformat(),'old')
            emit(temp,'unknown',now.isoformat(),'bad')
            files=list((Path(temp)/'speech-events').glob('*.json'))
            self.assertEqual(len(files),1)
            self.assertEqual(set(json.loads(files[0].read_text())),{'id','event','time'})
    def test_live_root_events_only_without_old_replay(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);sessions=root/'codex'/'sessions';sessions.mkdir(parents=True)
            now=datetime.now(timezone.utc).isoformat()
            for i,source in enumerate(['vscode',{'subagent':{'other':'guardian'}},{'subagent':{'thread_spawn':{}}}]):
                ident=f'{i:08d}-1111-4111-8111-111111111111'
                records=[{'type':'session_meta','payload':{'id':ident,'source':source}},
                    {'type':'event_msg','timestamp':now,'payload':{'type':'task_started','prompt':'PRIVATE'}},
                    {'type':'event_msg','timestamp':now,'payload':{'type':'task_complete','last_agent_message':'PRIVATE'}}]
                (sessions/f'rollout-{ident}.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in records),encoding='utf-8')
            ledger=Ledger(root/'data',root/'codex')
            try:
                ledger.scan();ledger.scan()
                files=list((root/'data'/'speech-events').glob('*.json'))
                self.assertEqual(len(files),2)
                self.assertEqual({json.loads(p.read_text())['event'] for p in files},{'UserPromptSubmit','Stop'})
                self.assertTrue(all('PRIVATE' not in p.read_text() for p in files))
            finally:ledger.close()

if __name__=='__main__':unittest.main()
