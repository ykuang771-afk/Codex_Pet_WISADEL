"""Local stdio MCP server. No network, API keys, raw input text or chat storage."""
import json, sys
from common import ROOT, counts, set_preferences, start

URI='ui://wisadel-companion/dashboard-v1.html'
EMPTY={'type':'object','properties':{},'additionalProperties':False}
TOOLS=[
 {'name':'get_stats','description':'Read Wisadel keyboard and mouse aggregate totals, today counts, and companion health. Does not capture input text.','inputSchema':EMPTY,'annotations':{'readOnlyHint':True,'openWorldHint':False}},
 {'name':'show_dashboard','description':'Show the interactive Wisadel keyboard and mouse statistics panel.','inputSchema':EMPTY,'annotations':{'readOnlyHint':True,'openWorldHint':False},'_meta':{'ui':{'resourceUri':URI},'openai/outputTemplate':URI}},
 {'name':'set_preferences','description':'Set counting pause, hover badge visibility or automatic startup. Keyboard/mouse display remains lifetime total. Token and USD display use the local 04:00 daily window. Counts are preserved.','inputSchema':{'type':'object','properties':{'paused':{'type':'boolean'},'overlay_enabled':{'type':'boolean'},'autostart':{'type':'boolean'},'display_period':{'type':'string','enum':['total']}},'additionalProperties':False,'minProperties':1},'annotations':{'readOnlyHint':False,'destructiveHint':False,'idempotentHint':True,'openWorldHint':False}}
]
def result_data(data):return {'content':[{'type':'text','text':json.dumps(data,ensure_ascii=False)}],'structuredContent':data,'isError':False}
def dispatch(method,params):
    if method=='initialize':return {'protocolVersion':'2024-11-05' if params.get('protocolVersion')=='2024-11-05' else '2025-03-26','capabilities':{'tools':{},'resources':{}},'serverInfo':{'name':'wisadel-companion','version':'0.1.0'}}
    if method=='ping':return {}
    if method=='tools/list':return {'tools':TOOLS}
    if method=='resources/list':return {'resources':[{'uri':URI,'name':'Wisadel statistics','mimeType':'text/html;profile=mcp-app'}]}
    if method=='resources/templates/list':return {'resourceTemplates':[]}
    if method=='resources/read':
        if params.get('uri')!=URI:raise ValueError('Unknown resource')
        return {'contents':[{'uri':URI,'mimeType':'text/html;profile=mcp-app','text':(ROOT/'ui/dashboard.html').read_text(encoding='utf-8'),'_meta':{'ui':{'prefersBorder':True,'csp':{'connectDomains':[],'resourceDomains':[]}},'openai/widgetPrefersBorder':True}}]}
    if method=='tools/call':
        name=params.get('name');args=params.get('arguments') or {}
        try:
            if name in ('get_stats','show_dashboard'):
                if args:raise ValueError('This tool takes no arguments')
                return result_data(counts())
            if name=='set_preferences':return result_data(set_preferences(args))
            raise ValueError('Unknown tool')
        except (ValueError,OSError) as exc:return {'content':[{'type':'text','text':str(exc)}],'isError':True}
    raise ValueError('Unsupported method')

def main():
    sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
    try:start()
    except OSError as exc:print('Companion startup failed: '+str(exc),file=sys.stderr)
    for line in sys.stdin:
        request=None
        try:
            request=json.loads(line)
            if 'id' not in request:continue
            response={'jsonrpc':'2.0','id':request['id'],'result':dispatch(request['method'],request.get('params') or {})}
        except Exception as exc:response={'jsonrpc':'2.0','id':request.get('id') if isinstance(request,dict) else None,'error':{'code':-32602,'message':str(exc)}}
        print(json.dumps(response,ensure_ascii=False),flush=True)
if __name__=='__main__':main()
