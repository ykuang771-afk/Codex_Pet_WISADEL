"""Persistent, local-only Codex token ledger; no prompts or responses are stored.

Design informed by CC Switch's MIT-licensed session_usage_codex.rs: exact last
usage, duplicate snapshots, cumulative fallback, ordered fork replay exclusion.
This is an independent Python implementation. See ../THIRD_PARTY_NOTICES.md.
"""
from __future__ import annotations
import argparse
from collections import defaultdict
from datetime import datetime, timezone, timedelta
from decimal import Decimal, ROUND_HALF_UP
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import sys
import threading
import time
from speech_events import emit as emit_speech

ROOT = Path(__file__).resolve().parents[1]
DATA = Path(os.environ.get('WISADEL_DATA', str(Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'WisadelCompanion')))
CODEX = Path(os.environ.get('WISADEL_CODEX_HOME', os.environ.get('CODEX_HOME', str(Path.home()/'.codex'))))
FIELDS = ('input_tokens','cached_input_tokens','cache_write_input_tokens','output_tokens','reasoning_output_tokens','total_tokens')

def packed(value): return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=True)
def digest(value): return hashlib.sha256(packed(value).encode()).hexdigest()
def utc(): return datetime.now(timezone.utc).isoformat()
def usage_day_bounds(now=None):
    local=now if now is not None else datetime.now()
    start=local.replace(hour=4,minute=0,second=0,microsecond=0)
    if local<start:start-=timedelta(days=1)
    end=start+timedelta(days=1)
    return (start.astimezone() if start.tzinfo is None else start,
            end.astimezone() if end.tzinfo is None else end)
def normalize_model(value):
    value=(value or 'unknown').lower().strip()
    return re.sub(r'-\d{4}-?\d{2}-?\d{2}$','',value)

def counters(value):
    if not isinstance(value,dict) or not any(k in value for k in FIELDS): return None
    value=dict(value)
    if 'cached_input_tokens' not in value: value['cached_input_tokens']=value.get('cache_read_input_tokens',0)
    return [max(0,int(value.get(k) or 0)) for k in FIELDS]

def atomic_json(path, value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_name(path.name+f'.{os.getpid()}.tmp')
    with temp.open('w',encoding='utf-8') as f:
        json.dump(value,f,ensure_ascii=False);f.flush();os.fsync(f.fileno())
    os.replace(temp,path)

class Ledger:
    def __init__(self,data,codex,pricing=None):
        self.data=Path(data);self.codex=Path(codex);self.data.mkdir(parents=True,exist_ok=True)
        self.price_doc=pricing or json.loads((ROOT/'pricing.json').read_text(encoding='utf-8'))
        self.prices=self.price_doc['models'];self.price_id=digest(self.price_doc)
        database=self.data/'usage.sqlite3'
        if not database.exists():
            backups=sorted((self.data/'backups').glob('usage-*.sqlite3'),key=lambda p:p.stat().st_mtime,reverse=True)
            for backup in backups:
                check=sqlite3.connect(backup.as_uri()+'?mode=ro',uri=True)
                try:valid=check.execute('PRAGMA quick_check').fetchone()[0]=='ok'
                finally:check.close()
                if valid:shutil.copy2(backup,database);break
            if not database.exists() and (self.data/'usage-summary.json').exists():
                previous=json.loads((self.data/'usage-summary.json').read_text(encoding='utf-8'))
                if previous.get('total_tokens',0)>0:raise RuntimeError('Missing ledger and backup; existing totals preserved')
        self.db=sqlite3.connect(database,timeout=15)
        self.db.row_factory=sqlite3.Row
        if self.db.execute('PRAGMA quick_check').fetchone()[0]!='ok':raise RuntimeError('Ledger integrity failed; database preserved')
        self.db.executescript('''
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS files(
              rollout TEXT PRIMARY KEY, thread TEXT, parent TEXT, started TEXT,
              path TEXT, size INTEGER, mtime INTEGER, offset INTEGER, head TEXT,
              state TEXT, finalized INTEGER DEFAULT 0);
            CREATE TABLE IF NOT EXISTS snapshots(
              rollout TEXT, seq INTEGER, sig TEXT, stamp TEXT, model TEXT,
              input INTEGER, cached INTEGER, writes INTEGER, output INTEGER,
              reasoning INTEGER, total INTEGER, exact INTEGER,
              PRIMARY KEY(rollout,seq));
            CREATE INDEX IF NOT EXISTS snapshot_order ON snapshots(rollout,seq);
            CREATE TABLE IF NOT EXISTS events(
              id TEXT PRIMARY KEY, thread TEXT, rollout TEXT, stamp TEXT, model TEXT,
              input INTEGER,cached INTEGER,writes INTEGER,output INTEGER,reasoning INTEGER,total INTEGER,
              usd_nanos INTEGER, long_usd_nanos INTEGER, long_context INTEGER,
              price_id TEXT, exact INTEGER);
            CREATE INDEX IF NOT EXISTS event_model_thread ON events(model,thread);
            CREATE INDEX IF NOT EXISTS event_timestamp ON events(julianday(stamp));
            CREATE TABLE IF NOT EXISTS price_versions(id TEXT PRIMARY KEY,document TEXT);
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT);
        ''')
        self.db.execute('INSERT OR IGNORE INTO price_versions VALUES(?,?)',(self.price_id,packed(self.price_doc)))
        self.db.commit()
        self.apply_estimate_overrides()

    def model_price(self,model):
        target=self.price_doc.get('estimate_overrides',{}).get(model,model)
        return self.prices.get(target,{})

    def apply_estimate_overrides(self):
        overrides=self.price_doc.get('estimate_overrides',{})
        if not overrides:return
        policy=digest({model:{'target':target,'price':self.prices[target]} for model,target in overrides.items()})
        marker='estimate_override:'+policy
        if self.db.execute('SELECT 1 FROM meta WHERE key=?',(marker,)).fetchone():return
        # Keep an independent consistent pre-migration database, including old prices.
        folder=self.data/'backups';folder.mkdir(exist_ok=True)
        target=folder/('before-estimate-'+policy[:16]+'.sqlite3')
        if not target.exists():
            dest=sqlite3.connect(target)
            try:self.db.backup(dest)
            finally:dest.close()
        with self.db:
            self.db.execute('''CREATE TABLE IF NOT EXISTS price_adjustments(
                event_id TEXT, policy TEXT, old_price_id TEXT, new_price_id TEXT,
                old_usd_nanos INTEGER, new_usd_nanos INTEGER, changed_at TEXT,
                PRIMARY KEY(event_id,policy))''')
            for model in overrides:
                p=self.model_price(model)
                if p.get('long_scope')!='request':raise ValueError('Estimate override requires per-request pricing')
                for r in self.db.execute('SELECT * FROM events WHERE model=?',(model,)).fetchall():
                    long=r['input']>p.get('long_threshold',10**20)
                    args=(model,r['input'],r['cached'],r['writes'],r['output'])
                    cost=self.price(*args,long=long,exact=bool(r['exact']))
                    long_cost=self.price(*args,long=True,exact=bool(r['exact']))
                    self.db.execute('INSERT INTO price_adjustments VALUES(?,?,?,?,?,?,?)',
                        (r['id'],policy,r['price_id'],self.price_id,r['usd_nanos'],cost,utc()))
                    self.db.execute('UPDATE events SET usd_nanos=?,long_usd_nanos=?,long_context=?,price_id=? WHERE id=?',
                        (cost,long_cost,int(long),self.price_id,r['id']))
            self.db.execute('INSERT INTO meta VALUES(?,?)',(marker,utc()))

    def parse_file(self,path):
        stat=path.stat();rollout=path.stem[-36:]
        if not re.fullmatch(r'[0-9a-fA-F-]{36}',rollout):return None
        old=self.db.execute('SELECT * FROM files WHERE rollout=?',(rollout,)).fetchone()
        if old and old['size']==stat.st_size and old['mtime']==stat.st_mtime_ns:return rollout
        with path.open('rb') as f:
            first=f.readline();head=hashlib.sha256(first).hexdigest()
            try:meta=json.loads(first)
            except (ValueError,UnicodeError):return None
            if meta.get('type')!='session_meta':return None
            payload=meta.get('payload') or {}
            thread=payload.get('id') or rollout
            source=payload.get('source');spawn={}
            if isinstance(source,dict) and isinstance(source.get('subagent'),dict):spawn=source['subagent'].get('thread_spawn') or {}
            parent=payload.get('forked_from_id') or spawn.get('parent_thread_id')
            started=meta.get('timestamp') or payload.get('timestamp') or ''
            reset=not old or old['head']!=head or stat.st_size<old['offset']
            state={'model':'unknown','high':None,'sources':{},'last_sig':None,'seq':0} if reset else json.loads(old['state'])
            offset=0 if reset else old['offset'];f.seek(offset)
            if reset:self.db.execute('DELETE FROM snapshots WHERE rollout=?',(rollout,))
            while True:
                position=f.tell();line=f.readline()
                if not line:break
                # Keep a partial tail unread so a later append can complete it.
                if not line.endswith(b'\n'):
                    f.seek(position);break
                offset=f.tell()
                if not any(k in line for k in (b'"turn_context"',b'"token_count"',b'"task_started"',b'"task_complete"',b'"turn_aborted"')):continue
                try:record=json.loads(line)
                except (ValueError,UnicodeError):continue
                p=record.get('payload') or {};kind=record.get('type')
                speech={'task_started':'UserPromptSubmit','task_complete':'Stop','turn_aborted':'Interrupt'}.get(p.get('type'))
                if kind=='event_msg' and speech and not (isinstance(source,dict) and source.get('subagent')):
                    try:emit_speech(self.data,speech,record.get('timestamp') or '',rollout+':'+str(position))
                    except OSError:pass
                if kind=='turn_context':
                    if p.get('model'):state['model']=normalize_model(p['model'])
                    continue
                if kind!='event_msg' or p.get('type')!='token_count':continue
                info=p.get('info') or {};total=counters(info.get('total_token_usage'));last=counters(info.get('last_token_usage'))
                if total is None and last is None:continue
                model=info.get('model') or info.get('model_name') or p.get('model')
                if model:state['model']=normalize_model(model)
                sig=digest([total,last]);bucket=(p.get('rate_limits') or {}).get('limit_id') or ''
                duplicate=total is not None and (state['sources'].get(bucket)==sig or state['last_sig']==sig)
                if total is not None:state['sources'][bucket]=sig
                state['last_sig']=sig
                delta=[0]*6 if duplicate else (last[:] if last is not None else [max(0,a-b) for a,b in zip(total,state['high'] or [0]*6)])
                if total is not None:state['high']=[max(a,b) for a,b in zip(total,state['high'] or [0]*6)]
                delta[1]=min(delta[1],delta[0]);delta[2]=min(delta[2],delta[0]-delta[1])
                # Reasoning is already part of output; cache is already part of input.
                delta[5]=delta[0]+delta[3]
                state['seq']+=1
                self.db.execute('INSERT OR REPLACE INTO snapshots VALUES(?,?,?,?,?,?,?,?,?,?,?,?)',
                    (rollout,state['seq'],sig,record.get('timestamp') or '',state['model'],*delta[:5],delta[5],int(last is not None)))
            finalized=0 if reset else old['finalized']
            self.db.execute('INSERT OR REPLACE INTO files VALUES(?,?,?,?,?,?,?,?,?,?,?)',
                (rollout,thread,parent,started,str(path),stat.st_size,stat.st_mtime_ns,offset,head,packed(state),finalized))
        return rollout

    def price(self,model,input,cached,writes,output,long=False,exact=True):
        p=self.model_price(model)
        if not p or (writes and p.get('write') is None):return None
        # Aggregated fallback cannot establish which requests crossed the limit.
        if not exact and input>p.get('long_threshold',10**20):return None
        multiplier=Decimal(2) if long else Decimal(1)
        cost=(Decimal(input-cached-writes)*Decimal(p['input'])+Decimal(cached)*Decimal(p['cached'])+Decimal(writes)*Decimal(p.get('write') or '0'))*multiplier
        cost+=Decimal(output)*Decimal(p['output'])*(Decimal('1.5') if long else Decimal(1))
        return int((cost*1000).quantize(Decimal(1),rounding=ROUND_HALF_UP))

    def finalize(self,rollout):
        f=self.db.execute('SELECT * FROM files WHERE rollout=?',(rollout,)).fetchone()
        if not f:return True
        if json.loads(f['state'])['seq']<=f['finalized']:return True
        prefix=0
        if f['parent']:
            # A reverted thread can have several physical rollouts sharing its
            # metadata thread ID. CC Switch indexes explicit parents by the
            # trailing physical rollout UUID, not by all of those replacements.
            parents=self.db.execute('SELECT rollout FROM files WHERE rollout=?',(f['parent'],)).fetchall()
            if not parents or not f['started']:return False
            timelines=[]
            for parent in parents:
                timelines.append([r[0] for r in self.db.execute('SELECT sig FROM snapshots WHERE rollout=? AND stamp<=? ORDER BY seq',(parent[0],f['started']))])
            # Ambiguous parent histories are deferred, never guessed or doubled.
            if any(t!=timelines[0] for t in timelines[1:]):return False
            parent=timelines[0];position=0
            for r in self.db.execute('SELECT sig FROM snapshots WHERE rollout=? ORDER BY seq',(rollout,)):
                try:position=parent.index(r[0],position)+1;prefix+=1
                except ValueError:break
        rows=self.db.execute('SELECT * FROM snapshots WHERE rollout=? AND seq>? ORDER BY seq',(rollout,f['finalized'])).fetchall()
        for r in rows:
            if r['seq']<=prefix or r['total']==0:continue
            if not r['stamp']:continue
            key=digest([f['thread'],r['stamp'],r['sig']])
            p=self.model_price(r['model'])
            long=r['input']>p.get('long_threshold',10**20)
            if p.get('long_scope')=='session':
                long=long or bool(self.db.execute('SELECT 1 FROM events WHERE thread=? AND model=? AND long_context=1 LIMIT 1',(f['thread'],r['model'])).fetchone())
            args=(r['model'],r['input'],r['cached'],r['writes'],r['output'])
            cost=self.price(*args,long=long,exact=bool(r['exact']))
            long_cost=self.price(*args,long=True,exact=bool(r['exact']))
            self.db.execute('INSERT OR IGNORE INTO events VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)',
                (key,f['thread'],rollout,r['stamp'],r['model'],r['input'],r['cached'],r['writes'],r['output'],r['reasoning'],r['total'],cost,long_cost,int(long),self.price_id,r['exact']))
            if long and p.get('long_scope')=='session':
                self.db.execute('UPDATE events SET usd_nanos=long_usd_nanos,long_context=1 WHERE thread=? AND model=? AND long_context=0',(f['thread'],r['model']))
        last=self.db.execute('SELECT MAX(seq) FROM snapshots WHERE rollout=?',(rollout,)).fetchone()[0] or 0
        self.db.execute('UPDATE files SET finalized=? WHERE rollout=?',(last,rollout))
        return True

    def scan(self):
        paths=[]
        for name in ('sessions','archived_sessions'):
            paths.extend((self.codex/name).rglob('rollout-*.jsonl'))
        errors=0
        for path in paths:
            try:
                with self.db:self.parse_file(path)
            except (OSError,ValueError,TypeError):errors+=1
        pending=0
        for row in self.db.execute('SELECT rollout FROM files').fetchall():
            with self.db:
                if not self.finalize(row[0]):pending+=1
        summary=self.summary();summary.update(updated=utc(),status='ok',scan_errors=errors,pending_files=pending,files_seen=len(paths))
        atomic_json(self.data/'usage-summary.json',summary)
        self.backup()
        return summary

    def summary(self,now=None):
        row=self.db.execute('SELECT COUNT(*),COALESCE(SUM(total),0),COALESCE(SUM(usd_nanos),0),COALESCE(SUM(CASE WHEN usd_nanos IS NULL THEN total ELSE 0 END),0) FROM events').fetchone()
        start,end=usage_day_bounds(now)
        daily=self.db.execute('SELECT COUNT(*),COALESCE(SUM(total),0),COALESCE(SUM(usd_nanos),0),COALESCE(SUM(CASE WHEN usd_nanos IS NULL THEN total ELSE 0 END),0) FROM events WHERE julianday(stamp)>=julianday(?) AND julianday(stamp)<julianday(?)',(start.isoformat(),end.isoformat())).fetchone()
        models=[dict(r) for r in self.db.execute('SELECT model,SUM(total) AS tokens,COALESCE(SUM(usd_nanos),0) AS usd_nanos,COUNT(*) AS requests FROM events GROUP BY model ORDER BY tokens DESC')]
        return {'ready':True,'total_tokens':row[1],'usd_estimate':format(Decimal(row[2])/10**9,'.9f'),'unpriced_tokens':row[3],
                'requests':row[0],'price_basis':'standard_api_equivalent','scope':'local_codex_history','models':models,
                'estimate_overrides':self.price_doc.get('estimate_overrides',{}),
                'daily_tokens':daily[1],'daily_usd_estimate':format(Decimal(daily[2])/10**9,'.9f'),
                'daily_unpriced_tokens':daily[3],'daily_requests':daily[0],
                'daily_start':start.isoformat(),'daily_end':end.isoformat(),'usage_display_period':'local_day_04',
                'pricing_verified_at':self.price_doc['verified_at'],'database':str(self.data/'usage.sqlite3')}

    def backup(self,force=False):
        now=time.time();row=self.db.execute("SELECT value FROM meta WHERE key='last_backup'").fetchone()
        if not force and row and now-float(row[0])<3600:return
        folder=self.data/'backups';folder.mkdir(exist_ok=True)
        target=folder/('usage-'+datetime.now().strftime('%G-W%V')+'.sqlite3');temp=target.with_suffix('.tmp')
        dest=sqlite3.connect(temp)
        try:self.db.backup(dest)
        finally:dest.close()
        os.replace(temp,target)
        with self.db:self.db.execute("INSERT OR REPLACE INTO meta VALUES('last_backup',?)",(str(now),))

    def close(self):self.db.close()

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--once',action='store_true');args=parser.parse_args()
    DATA.mkdir(parents=True,exist_ok=True)
    # Independent of the plugin version; single writer even if startup overlaps.
    lock=(DATA/'usage.lock').open('a+b');lock.seek(0);lock.write(b'0');lock.flush();lock.seek(0)
    if os.name=='nt':
        import msvcrt
        try:msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        except OSError:return
    stop=threading.Event()
    if not args.once:
        def until_closed():
            try:sys.stdin.buffer.read()
            finally:stop.set()
        threading.Thread(target=until_closed,daemon=True).start()
    ledger=None
    try:
        ledger=Ledger(DATA,CODEX)
        while True:
            result=ledger.scan()
            if args.once:
                print(json.dumps(result,ensure_ascii=False));break
            if stop.wait(5):break
    except Exception as exc:
        # Preserve the last good totals and database; never replace them with zero.
        atomic_json(DATA/'usage-error.json',{'error_type':type(exc).__name__,'updated':utc()})
        if args.once:raise
    finally:
        if ledger:
            ledger.backup(force=True);ledger.close()
        lock.close()

if __name__=='__main__':main()
