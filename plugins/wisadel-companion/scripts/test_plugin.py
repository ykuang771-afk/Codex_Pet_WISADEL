import json, os, subprocess, sys, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def run():
    with tempfile.TemporaryDirectory() as td:
        p=Path(td);(p/'settings.json').write_text(json.dumps({'autostart':False}))
        fixture={'Version':1,'Total':{'Keyboard':100,'Left':20,'Right':2,'Middle':1,'Side':3},'Days':{}}
        stats=p/'stats.json';stats.write_text(json.dumps(fixture));before=stats.read_bytes()
        env={**os.environ,'WISADEL_DATA':td,'WISADEL_STATS_PATH':str(stats)}
        requests=[{'id':1,'method':'initialize','params':{'protocolVersion':'2025-03-26'}},
            {'method':'notifications/initialized'}, {'id':2,'method':'tools/list'},
            {'id':3,'method':'tools/call','params':{'name':'get_stats'}},
            {'id':4,'method':'resources/read','params':{'uri':'ui://wisadel-companion/dashboard-v1.html'}},
            {'id':5,'method':'tools/call','params':{'name':'set_preferences','arguments':{'paused':True}}},
            {'id':6,'method':'tools/call','params':{'name':'set_preferences','arguments':{'paused':'false'}}},
            {'id':7,'method':'tools/call','params':{'name':'show_dashboard'}},
            {'id':8,'method':'tools/call','params':{'name':'set_preferences','arguments':{'display_period':'today'}}}]
        completed=subprocess.run([sys.executable,str(ROOT/'scripts/server.py')],input=''.join(json.dumps({'jsonrpc':'2.0',**r})+'\n' for r in requests),text=True,encoding='utf-8',capture_output=True,env=env,timeout=15,check=True)
        rows={r['id']:r for r in map(json.loads,completed.stdout.splitlines())}
        assert len(rows)==8 and rows[1]['result']['capabilities']['resources']=={}
        assert len(rows[2]['result']['tools'])==3
        assert rows[3]['result']['structuredContent']['total']['Mouse']==26
        assert 'ui/initialize' in rows[4]['result']['contents'][0]['text']
        assert rows[5]['result']['structuredContent']['settings']['paused'] is True
        assert rows[6]['result']['isError'] is True
        assert rows[7]['result']['structuredContent']['total']['Keyboard']==100
        assert rows[8]['result']['isError'] is True
        assert stats.read_bytes()==before
        subprocess.run([sys.executable,str(ROOT/'scripts/hook.py')],input=json.dumps({'hook_event_name':'Stop','prompt':'DO-NOT-STORE','transcript_path':'SECRET-PATH'}),text=True,capture_output=True,env=env,check=True,timeout=10)
        event=json.loads((p/'hook-event.json').read_text());assert set(event)=={'event','time'} and event['event']=='Stop'
        print('PASS: MCP initialization, tools, UI resource, stats, preference validation, unchanged totals, hook event privacy')
if __name__=='__main__':run()
