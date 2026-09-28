using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

public class UsageSnapshot {
    public bool ready;
    public long total_tokens,unpriced_tokens;
    public long? daily_tokens,daily_unpriced_tokens;
    public string daily_usd_estimate,daily_start,daily_end;
    public string usd_estimate,updated,status;
    public int pending_files,scan_errors;
    public bool Fresh {get{DateTimeOffset parsed;return ready&&DateTimeOffset.TryParse(updated,out parsed)&&(DateTimeOffset.UtcNow-parsed).TotalSeconds<30;}}
    public bool DailyCurrent {get{DateTimeOffset end;return ready&&daily_tokens.HasValue&&DateTimeOffset.TryParse(daily_end,out end)&&DateTimeOffset.UtcNow<end;}}
    public string Tokens {get{return DailyCurrent?TokenText.Compact(daily_tokens.Value):"—";}}
    public string Dollars {get{decimal value;return DailyCurrent&&Decimal.TryParse(daily_usd_estimate,NumberStyles.Number,CultureInfo.InvariantCulture,out value)?value.ToString("N4",CultureInfo.InvariantCulture):"—";}}
    public string Note {get{
        if(!ready)return "正在同步本机历史…";
        if(!Fresh)return "累计已保存 · 等待同步";
        if(pending_files>0)return "API 折算 · 部分历史待核实";
        return daily_unpriced_tokens>0?"API 折算 · 今日含未定价模型":"API 折算 · 今日 04:00 起";
    }}
    public string Key {get{return Tokens+"|"+Dollars+"|"+Note;}}
}
public static class TokenText {
    public static string Compact(long count){
        if(count<0)return "—";
        if(count>=10000){
            long tenThousands=count/10000+(count%10000>=5000?1:0);
            long hundredMillions=tenThousands/10000,rest=tenThousands%10000;
            if(hundredMillions==0)return tenThousands.ToString(CultureInfo.InvariantCulture)+"万";
            return hundredMillions.ToString(CultureInfo.InvariantCulture)+"亿"+
                (rest==0?"":rest.ToString(CultureInfo.InvariantCulture)+"万");
        }
        return count.ToString("N0",CultureInfo.InvariantCulture);
    }
}
public class UsageMonitor : IDisposable {
    Process child;
    DateTime nextStart=DateTime.MinValue;
    public UsageSnapshot Latest=new UsageSnapshot();
    public void Tick(){
        Latest=CompanionFiles.Read("usage-summary.json",Latest);
        if(DateTime.UtcNow<nextStart)return;
        try{if(child!=null&&!child.HasExited)return;}catch{if(child!=null)child.Dispose();child=null;}
        nextStart=DateTime.UtcNow.AddSeconds(30);
        try {
            if(child!=null)child.Dispose();
            string python=Environment.GetEnvironmentVariable("WISADEL_PYTHON_PATH");
            if(String.IsNullOrEmpty(python))python=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache","codex-runtimes","codex-primary-runtime","dependencies","python","python.exe");
            string script=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","scripts","usage_ledger.py"));
            var start=new ProcessStartInfo(python,"-X utf8 \""+script+"\""){
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory
            };
            start.EnvironmentVariables["WISADEL_DATA"]=CompanionFiles.Data;
            child=new Process{StartInfo=start};child.OutputDataReceived+=(s,e)=>{};child.ErrorDataReceived+=(s,e)=>{};
            child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
        }catch{nextStart=DateTime.UtcNow.AddSeconds(30);}
    }
    public void Dispose(){if(child==null)return;try{child.StandardInput.Close();if(!child.WaitForExit(2000))child.Kill();}catch{}child.Dispose();}
}
