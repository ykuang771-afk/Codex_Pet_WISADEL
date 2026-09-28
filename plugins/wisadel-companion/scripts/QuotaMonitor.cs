using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

public class QuotaWindow {
    public double? usedPercent;
    public long? resetsAt;
    public int? windowDurationMins;
    public double? Remaining { get { return usedPercent.HasValue ? (double?)Math.Max(0,Math.Min(100,100-usedPercent.Value)) : null; } }
    public string ResetText() {
        if(!resetsAt.HasValue)return "Reset —";
        try { return "Reset " + new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(resetsAt.Value).ToLocalTime().ToString("MM-dd HH:mm",CultureInfo.InvariantCulture); }
        catch { return "Reset —"; }
    }
}
public class QuotaSnapshot {
    public QuotaWindow fiveHour,week;
    public DateTime fetchedUtc;
    public string status="loading";
    public bool Fresh { get { return status=="ok" && (DateTime.UtcNow-fetchedUtc).TotalSeconds<=90; } }
    public string StatusText { get {
        if(status=="loading")return "读取额度中…";
        if(status!="ok")return "额度暂不可用 · 自动重试";
        if(!Fresh)return "额度已过期 · 正在更新";
        return "本机时间 · 更新于 " + fetchedUtc.ToLocalTime().ToString("HH:mm:ss");
    } }
    public string Percent(QuotaWindow w) {
        if(!Fresh||w==null||!w.Remaining.HasValue)return "—";
        if(w.resetsAt.HasValue && w.resetsAt.Value<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())return "更新中";
        return w.Remaining.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%";
    }
    public string Key { get { return Percent(fiveHour)+"|"+Percent(week)+"|"+(fiveHour==null?"":fiveHour.ResetText())+"|"+(week==null?"":week.ResetText())+"|"+StatusText; } }
}
public class LowQuotaAlertState {
    public long lastReset;
}
public static class LowQuotaAlert {
    public static bool Due(QuotaSnapshot quota,LowQuotaAlertState state){
        var w=quota.fiveHour;
        return quota.Fresh&&w!=null&&w.Remaining.HasValue&&w.Remaining.Value<5
            &&w.resetsAt.HasValue&&w.resetsAt.Value>DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            &&state.lastReset!=w.resetsAt.Value;
    }
    public static string Message(QuotaSnapshot quota){
        return "5h 剩余额度仅 "+quota.fiveHour.Remaining.Value.ToString("0.#",CultureInfo.InvariantCulture)
            +"%。\n重置："+quota.fiveHour.ResetText().Replace("Reset ","")+"（本机时间）";
    }
}
public class QuotaMonitor : IDisposable {
    public volatile QuotaSnapshot Latest=new QuotaSnapshot();
    volatile bool stopped;
    readonly ManualResetEvent wake=new ManualResetEvent(false);
    Thread worker;
    public static QuotaSnapshot Parse(string json) {
        var serializer=new JavaScriptSerializer();
        var result=serializer.Deserialize<Dictionary<string,object>>(json);
        object bucket=null,map;
        if(result.TryGetValue("rateLimitsByLimitId",out map) && map is Dictionary<string,object>)
            ((Dictionary<string,object>)map).TryGetValue("codex",out bucket);
        if(bucket==null)result.TryGetValue("rateLimits",out bucket);
        var snapshot=new QuotaSnapshot{status="unavailable",fetchedUtc=DateTime.UtcNow};
        var fields=bucket as Dictionary<string,object>;
        if(fields==null)return snapshot;
        object limit;
        if(fields.TryGetValue("limitId",out limit)&&limit!=null&&(string)limit!="codex")return snapshot;
        foreach(string key in new[]{"primary","secondary"}) {
            object value;if(!fields.TryGetValue(key,out value)||value==null)continue;
            var window=serializer.Deserialize<QuotaWindow>(serializer.Serialize(value));
            if(window.windowDurationMins==300)snapshot.fiveHour=window;
            if(window.windowDurationMins==10080)snapshot.week=window;
        }
        snapshot.status=(snapshot.fiveHour!=null||snapshot.week!=null)?"ok":"unavailable";
        return snapshot;
    }
    public static string FindCodex() {
        string custom=Environment.GetEnvironmentVariable("WISADEL_CODEX_PATH");
        if(!String.IsNullOrEmpty(custom)&&File.Exists(custom))return custom;
        foreach(string name in new[]{"ChatGPT","Codex"})foreach(var app in Process.GetProcessesByName(name)) {
            using(app)try {
                string exe=Path.Combine(Path.GetDirectoryName(app.MainModule.FileName),"resources","codex.exe");
                if(File.Exists(exe))return exe;
            }catch{}
        }
        throw new FileNotFoundException("Running Codex app executable not found");
    }
    public void Start(){worker=new Thread(Run){IsBackground=true,Name="WisadelQuota"};worker.Start();}
    void Publish(QuotaSnapshot value){Latest=value;try{CompanionFiles.Write("quota.json",value);}catch{}}
    void Run() {
        while(!stopped) {
            try { Connect(); } catch { if(!stopped)Publish(new QuotaSnapshot{status="unavailable"}); }
            if(!stopped)wake.WaitOne(30000);
        }
    }
    void Connect() {
        using(var messages=new BlockingCollection<string>())
        using(var process=new Process()) {
            process.StartInfo=new ProcessStartInfo(FindCodex(),"app-server") {
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory
            };
            process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)try{messages.Add(e.Data);}catch{}};
            // Drain diagnostics without retaining possible account or configuration details.
            process.ErrorDataReceived+=(s,e)=>{};
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            var serializer=new JavaScriptSerializer();
            try {
                Send(process,new {id=1,method="initialize",@params=new {clientInfo=new {name="wisadel_companion",version="0.2.0"}}});
                WaitReply(messages,process,1,serializer);
                Send(process,new {method="initialized"});
                int id=2;
                while(!stopped) {
                    Send(process,new {id=id,method="account/rateLimits/read",@params=new {}});
                    var reply=WaitReply(messages,process,id++,serializer);
                    Publish(Parse(serializer.Serialize(reply["result"])));
                    var next=DateTime.UtcNow.AddSeconds(30);
                    while(!stopped&&DateTime.UtcNow<next) {
                        string line;
                        if(messages.TryTake(out line,200))HandleNotification(line,serializer);
                        if(process.HasExited)throw new IOException("Quota source closed");
                    }
                }
            } finally {
                try{process.StandardInput.Close();if(!process.WaitForExit(1000))process.Kill();}catch{}
            }
        }
    }
    Dictionary<string,object> WaitReply(BlockingCollection<string> messages,Process process,int id,JavaScriptSerializer serializer) {
        var until=DateTime.UtcNow.AddSeconds(20);
        while(!stopped&&DateTime.UtcNow<until) {
            string line;
            if(messages.TryTake(out line,200)) {
                var reply=serializer.Deserialize<Dictionary<string,object>>(line);
                object value;
                if(reply.TryGetValue("id",out value)&&Convert.ToString(value,CultureInfo.InvariantCulture)==id.ToString(CultureInfo.InvariantCulture)) {
                    if(reply.ContainsKey("error")||!reply.ContainsKey("result"))throw new IOException("Quota source unavailable");
                    return reply;
                }
                HandleNotification(line,serializer);
            }
            if(process.HasExited)throw new IOException("Quota source closed");
        }
        throw new TimeoutException();
    }
    void HandleNotification(string line,JavaScriptSerializer serializer) {
        var message=serializer.Deserialize<Dictionary<string,object>>(line);
        object method,parameters;
        if(!message.TryGetValue("method",out method))return;
        if((string)method=="account/rateLimits/updated"&&message.TryGetValue("params",out parameters))
            Publish(Parse(serializer.Serialize(parameters)));
        if((string)method=="account/updated")Publish(new QuotaSnapshot{status="loading"});
    }
    static void Send(Process process,object data) {
        process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(data));process.StandardInput.Flush();
    }
    public void Dispose(){stopped=true;wake.Set();if(worker!=null)worker.Join(1800);}
}
