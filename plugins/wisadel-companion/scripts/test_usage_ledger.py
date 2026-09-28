import json
from pathlib import Path
import shutil
import tempfile
import unittest
from datetime import datetime,timezone,timedelta
from usage_ledger import Ledger,usage_day_bounds

P='11111111-1111-4111-8111-111111111111'
C='22222222-2222-4222-8222-222222222222'
def meta(id=P,parent=None,stamp='2026-01-01T00:00:00Z'):
    return {'type':'session_meta','timestamp':stamp,'payload':{'id':id,'forked_from_id':parent}}
def turn(model='gpt-6-sol'):return {'type':'turn_context','payload':{'model':model}}
def token(n=1,stamp=None,model=None,last=True,bucket='codex'):
    usage={'input_tokens':1000*n,'cached_input_tokens':800*n,'cache_write_input_tokens':0,'output_tokens':100*n,'reasoning_output_tokens':50*n,'total_tokens':1100*n}
    info={'total_token_usage':usage}
    if last:info['last_token_usage']={k:int(v/n) for k,v in usage.items()}
    if model:info['model']=model
    return {'type':'event_msg','timestamp':stamp or f'2026-01-01T00:00:{n:02d}Z','payload':{'type':'token_count','info':info,'rate_limits':{'limit_id':bucket}}}
def append(path,rows):
    with path.open('a',encoding='utf-8') as f:
        for row in rows:f.write(json.dumps(row)+'\n')

class LedgerTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
        self.codex=self.root/'codex';self.sessions=self.codex/'sessions';self.sessions.mkdir(parents=True)
        self.path=self.sessions/f'rollout-test-{P}.jsonl';self.data=self.root/'data'
        self.ledger=Ledger(self.data,self.codex)
    def tearDown(self):self.ledger.close();self.tmp.cleanup()
    def test_restart_archive_deletion_and_backup(self):
        append(self.path,[meta(),turn(),token()]);a=self.ledger.scan()
        self.assertEqual(a['total_tokens'],1100);self.assertEqual(a['usd_estimate'],'0.001560000')
        self.ledger.close();self.ledger=Ledger(self.data,self.codex)
        self.assertEqual(self.ledger.scan()['total_tokens'],1100)
        archived=self.codex/'archived_sessions';archived.mkdir();moved=archived/self.path.name
        shutil.move(self.path,moved);self.assertEqual(self.ledger.scan()['total_tokens'],1100)
        append(moved,[token(2)]);self.assertEqual(self.ledger.scan()['total_tokens'],2200)
        moved.unlink();self.assertEqual(self.ledger.scan()['total_tokens'],2200)
        self.ledger.backup(force=True);self.assertTrue(list((self.data/'backups').glob('*.sqlite3')))
    def test_fork_replay_with_new_timestamps(self):
        append(self.path,[meta(),turn(),token(),token(2)])
        child=self.sessions/f'rollout-child-{C}.jsonl'
        append(child,[meta(C,P,'2026-01-01T00:00:10Z'),turn(),token(1,'2026-01-01T00:00:11Z'),token(2,'2026-01-01T00:00:11Z'),token(3,'2026-01-01T00:00:12Z')])
        self.assertEqual(self.ledger.scan()['total_tokens'],3300)
        self.assertEqual(self.ledger.scan()['total_tokens'],3300)
    def test_missing_parent_deferred(self):
        append(self.path,[meta(P,C),turn(),token()]);a=self.ledger.scan()
        self.assertEqual(a['total_tokens'],0);self.assertEqual(a['pending_files'],1)
    def test_parent_uses_physical_id_not_reverted_replacement(self):
        append(self.path,[meta(),turn(),token()])
        replacement=self.sessions/f'rollout-revert-{P}_{C}.jsonl'
        append(replacement,[meta(),turn(),token(),token(2)])
        childid='33333333-3333-4333-8333-333333333333'
        child=self.sessions/f'rollout-child-{childid}.jsonl'
        append(child,[meta(childid,P,'2026-01-01T00:00:10Z'),turn(),token(1,'2026-01-01T00:00:11Z'),token(2,'2026-01-01T00:00:12Z')])
        summary=self.ledger.scan()
        self.assertEqual(summary['pending_files'],0);self.assertEqual(summary['total_tokens'],3300)
    def test_missing_database_restores_backup(self):
        append(self.path,[meta(),turn(),token()]);self.ledger.scan();self.ledger.backup(force=True);self.ledger.close()
        (self.data/'usage.sqlite3').unlink();self.path.unlink()
        self.ledger=Ledger(self.data,self.codex)
        self.assertEqual(self.ledger.scan()['total_tokens'],1100)
    def test_duplicates_model_switch_and_unpriced(self):
        append(self.path,[meta(),turn(),token(),token(1,bucket='other'),turn('gpt-6-luna'),token(2),turn('unknown-internal-model'),token(3)])
        a=self.ledger.scan();self.assertEqual(a['total_tokens'],3300);self.assertEqual(a['unpriced_tokens'],1100)
        self.assertEqual(a['usd_estimate'],'0.001638000')
    def test_partial_line_completed(self):
        append(self.path,[meta(),turn()]);raw=json.dumps(token())
        with self.path.open('a') as f:f.write(raw[:len(raw)//2])
        self.assertEqual(self.ledger.scan()['total_tokens'],0)
        with self.path.open('a') as f:f.write(raw[len(raw)//2:]+'\n')
        self.assertEqual(self.ledger.scan()['total_tokens'],1100)
    def test_user_override_backfills_history_once_and_prices_new_usage(self):
        pricing=json.loads(json.dumps(self.ledger.price_doc));pricing.pop('estimate_overrides',None)
        self.ledger.close();self.data=self.root/'legacy-data';self.ledger=Ledger(self.data,self.codex,pricing=pricing)
        append(self.path,[meta(),turn(),token(),turn('codex-auto-review'),token(2)])
        before=self.ledger.scan();self.assertEqual(before['unpriced_tokens'],1100)
        original=dict(self.ledger.db.execute("SELECT * FROM events WHERE model='gpt-6-sol'").fetchone())
        self.ledger.close();self.ledger=Ledger(self.data,self.codex)
        after=self.ledger.scan()
        self.assertEqual(after['total_tokens'],before['total_tokens'])
        self.assertEqual(after['unpriced_tokens'],0);self.assertEqual(after['usd_estimate'],'0.003320000')
        self.assertEqual(dict(self.ledger.db.execute("SELECT * FROM events WHERE model='gpt-6-sol'").fetchone()),original)
        review=self.ledger.db.execute("SELECT * FROM events WHERE model='codex-auto-review'").fetchone()
        self.assertEqual(review['usd_nanos'],1760000)
        self.assertEqual(self.ledger.db.execute('SELECT COUNT(*) FROM price_adjustments').fetchone()[0],1)
        self.assertTrue(list((self.data/'backups').glob('before-estimate-*.sqlite3')))
        self.ledger.close();self.ledger=Ledger(self.data,self.codex)
        self.assertEqual(self.ledger.scan()['usd_estimate'],'0.003320000')
        self.assertEqual(self.ledger.db.execute('SELECT COUNT(*) FROM price_adjustments').fetchone()[0],1)
        append(self.path,[token(3)])
        self.assertEqual(self.ledger.scan()['usd_estimate'],'0.005080000')
        self.assertEqual(self.ledger.price('codex-auto-review',300000,100000,10000,1000,long=True),868000000)
    def test_cumulative_fallback_and_rescan(self):
        append(self.path,[meta(),turn(),token(last=False),token(2,last=False)])
        self.assertEqual(self.ledger.scan()['total_tokens'],2200)
        self.assertEqual(self.ledger.scan()['total_tokens'],2200)
    def test_cache_write_long_context_and_no_double_reasoning(self):
        self.assertEqual(self.ledger.price('gpt-6-sol',1000,600,100,100),1970000)
        short=self.ledger.price('gpt-6-sol',300000,0,0,1000)
        long=self.ledger.price('gpt-6-sol',300000,0,0,1000,long=True)
        self.assertEqual(short,610000000);self.assertEqual(long,1215000000)
        self.assertIsNone(self.ledger.price('unknown',1,0,0,1))
    def test_gpt55_session_long_context_reprices_prior_usage(self):
        append(self.path,[meta(),turn('gpt-5.5'),token()]);self.ledger.scan()
        t=token(2);t['payload']['info']['last_token_usage']['input_tokens']=300000
        append(self.path,[t]);self.ledger.scan()
        rows=self.ledger.db.execute('SELECT long_context,usd_nanos,long_usd_nanos FROM events').fetchall()
        self.assertEqual(len(rows),2)
        self.assertTrue(all(r[0]==1 and r[1]==r[2] for r in rows))
    def test_daily_four_am_boundary_keeps_lifetime_history(self):
        zone=timezone(timedelta(hours=8))
        # Mixed timestamp offsets describe the same local boundary.
        append(self.path,[meta(),turn(),token(1,'2026-01-01T19:59:59Z'),
            token(2,'2026-01-02T04:00:00+08:00'),token(3,'2026-01-02T19:59:59Z'),
            token(4,'2026-01-03T04:00:00+08:00')])
        self.ledger.scan()
        before=self.ledger.summary(datetime(2026,1,2,3,59,59,tzinfo=zone))
        after=self.ledger.summary(datetime(2026,1,2,4,0,0,tzinfo=zone))
        midnight=self.ledger.summary(datetime(2026,1,3,0,0,0,tzinfo=zone))
        nextday=self.ledger.summary(datetime(2026,1,3,4,0,0,tzinfo=zone))
        self.assertEqual(before['daily_tokens'],1100)
        self.assertEqual(after['daily_tokens'],2200)
        self.assertEqual(midnight['daily_tokens'],after['daily_tokens'])
        self.assertEqual(nextday['daily_tokens'],1100)
        self.assertEqual(after['daily_usd_estimate'],'0.003120000')
        self.assertEqual(after['daily_start'],'2026-01-02T04:00:00+08:00')
        self.assertEqual(after['daily_end'],'2026-01-03T04:00:00+08:00')
        self.assertTrue(all(x['total_tokens']==4400 for x in [before,after,midnight,nextday]))
        empty=self.ledger.summary(datetime(2026,1,4,12,tzinfo=zone))
        self.assertEqual(empty['daily_tokens'],0);self.assertEqual(empty['daily_usd_estimate'],'0.000000000')
        self.ledger.close();self.ledger=Ledger(self.data,self.codex)
        self.assertEqual(self.ledger.summary(datetime(2026,1,2,12,tzinfo=zone))['daily_tokens'],2200)

if __name__=='__main__':unittest.main()
