using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Only aggregate counts are persisted. Raw key identities live briefly in
// Held, solely to suppress Windows typematic repeats; no text is reconstructed.
public class Counts {
    public long Keyboard, Left, Right, Middle, Side;
    public long Mouse { get { return Left + Right + Middle + Side; } }
    public void Add(int kind) {
        if (kind == 0) Keyboard++; else if (kind == 1) Left++;
        else if (kind == 2) Right++; else if (kind == 3) Middle++; else Side++;
    }
}
public class SavedStats {
    public int Version = 1;
    public Counts Total = new Counts();
    public Dictionary<string, Counts> Days = new Dictionary<string, Counts>();
    public int X = -1, Y = -1;
    public bool Topmost = true;
}
public class CounterModel {
    public SavedStats Data;
    public Counts Session = new Counts();
    public HashSet<int> Held = new HashSet<int>();
    public bool Paused;
    public Func<DateTime> Clock = () => DateTime.Now;
    public CounterModel(SavedStats data) { Data = data; }
    public Counts Today {
        get {
            string date = Clock().ToString("yyyy-MM-dd");
            if (!Data.Days.ContainsKey(date)) Data.Days[date] = new Counts();
            return Data.Days[date];
        }
    }
    public bool Keyboard(int identity, bool up) {
        if (up) { Held.Remove(identity); return false; }
        if (!Held.Add(identity) || Paused) return false;
        Add(0); return true;
    }
    public bool Mouse(ushort flags) {
        if (Paused) return false;
        bool added = false;
        ushort[] masks = { 1, 4, 16, 64, 256 };
        for (int i = 0; i < masks.Length; i++) if ((flags & masks[i]) != 0) {
            Add(Math.Min(i + 1, 4)); added = true;
        }
        return added;
    }
    void Add(int kind) { Today.Add(kind); Data.Total.Add(kind); Session.Add(kind); }
}
public static class StatsStore {
    static DateTime lastHistory=DateTime.MinValue;
    static string HistoryDirectory(){return Path.Combine(Environment.GetEnvironmentVariable("WISADEL_DATA")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WisadelCompanion"),"counter-backups");}
    public static SavedStats Load(string path) {
        Exception error = null;
        var candidates=new List<string>{path,path+".bak"};SavedStats best=null;decimal maximum=-1;
        string history=HistoryDirectory();
        if(Directory.Exists(history)){
            string[] files=Directory.GetFiles(history,"stats-*.json");Array.Sort(files);Array.Reverse(files);
            for(int i=0;i<Math.Min(3,files.Length);i++)candidates.Add(files[i]);
        }
        foreach (string file in candidates) {
            if (!File.Exists(file)) continue;
            try {
                SavedStats d = new JavaScriptSerializer().Deserialize<SavedStats>(File.ReadAllText(file));
                if (d == null || d.Version != 1 || d.Total == null || d.Days == null) throw new InvalidDataException("统计文件格式无效");
                foreach (Counts c in d.Days.Values) Validate(c);
                Validate(d.Total);
                decimal sum=(decimal)d.Total.Keyboard+d.Total.Left+d.Total.Right+d.Total.Middle+d.Total.Side;
                if(sum>maximum){maximum=sum;best=d;}
            } catch (Exception ex) { error = ex; }
        }
        if(best!=null)return best;
        if (error != null) throw new InvalidDataException("统计文件和备份无法读取，原文件已保留。", error);
        return new SavedStats();
    }
    static void Validate(Counts c) {
        if (c == null || c.Keyboard < 0 || c.Left < 0 || c.Right < 0 || c.Middle < 0 || c.Side < 0) throw new InvalidDataException("统计数据无效");
    }
    public static void Save(string path, SavedStats data) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Validate(data.Total);
        string temp = path + ".tmp";
        string json=new JavaScriptSerializer().Serialize(data);
        DurableWrite(temp,json);
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        if((DateTime.UtcNow-lastHistory).TotalMinutes>=5){
            string folder=HistoryDirectory();Directory.CreateDirectory(folder);
            string snapshot=Path.Combine(folder,"stats-"+DateTime.Now.ToString("yyyy-MM-dd")+".json"),pending=snapshot+".tmp";
            DurableWrite(pending,json);
            if(File.Exists(snapshot))File.Replace(pending,snapshot,null);else File.Move(pending,snapshot);
            lastHistory=DateTime.UtcNow;
        }
    }
    static void DurableWrite(string path,string text){
        byte[] bytes=new UTF8Encoding(false).GetBytes(text);
        using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){
            stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
    }
}
