using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Web.Script.Serialization;

public class SpeechEvent {public string id,@event,time;}
public class SpeechEngine {
    public const string StartLine="Prompt都是废纸，我清楚你要干什么。";
    public const string EndLine="哈吉马路呦~";
    public static readonly string[] RandomLines={"真是危险呢，这也要我出马？","天真的笨蛋，还好没死绝","在我面前你都睡得着?","把我派到这里，就是你费劲想出来的万全计划？"};
    public static readonly string[] JumpLines={StartLine,EndLine,RandomLines[0],RandomLines[1],RandomLines[2],RandomLines[3]};
    readonly Random random;
    readonly HashSet<string> seen=new HashSet<string>();
    string lastKind;DateTime lastEvent=DateTime.MinValue;
    int lastRandom=-1,lastJump=-1;
    public string Text{get;private set;}
    public DateTime Until{get;private set;}
    public DateTime NextRandom{get;private set;}
    public SpeechEngine(DateTime now,int seed){random=new Random(seed);Schedule(now);}
    void Schedule(DateTime now){NextRandom=now.AddSeconds(random.Next(180,421));}
    public void Accept(SpeechEvent ev,DateTime now){
        DateTimeOffset stamp;
        if(ev==null||String.IsNullOrEmpty(ev.id)||!DateTimeOffset.TryParse(ev.time,out stamp))return;
        if((now-stamp.UtcDateTime).TotalSeconds>20||(now-stamp.UtcDateTime).TotalSeconds< -5||!seen.Add(ev.id))return;
        if(seen.Count>512){seen.Clear();seen.Add(ev.id);}
        if(ev.@event==lastKind&&(now-lastEvent).TotalSeconds<8)return;
        if(ev.@event!="UserPromptSubmit"&&ev.@event!="Stop"&&ev.@event!="Interrupt")return;
        lastKind=ev.@event;lastEvent=now;Schedule(now);
        if(ev.@event=="Interrupt"){Text=null;Until=now;return;}
        Text=ev.@event=="Stop"?EndLine:StartLine;Until=now.AddSeconds(ev.@event=="Stop"?4:6);
    }
    public void Poll(DateTime now){
        string folder=Path.Combine(CompanionFiles.Data,"speech-events");
        if(!Directory.Exists(folder))return;
        var events=new List<SpeechEvent>();
        foreach(string path in Directory.GetFiles(folder,"*.json"))try{
            events.Add(new JavaScriptSerializer().Deserialize<SpeechEvent>(File.ReadAllText(path)));
        }catch{}
        events.Sort((a,b)=>String.CompareOrdinal(a==null?"":a.time,b==null?"":b.time));
        foreach(var ev in events)Accept(ev,now);
    }
    public void Tick(DateTime now,bool canShow){
        if(now>=Until)Text=null;
        if(now<NextRandom)return;
        if(!canShow||Text!=null){NextRandom=now.AddSeconds(30);return;}
        int next=random.Next(RandomLines.Length-(lastRandom>=0?1:0));
        if(lastRandom>=0&&next>=lastRandom)next++;
        lastRandom=next;Text=RandomLines[next];Until=now.AddSeconds(6);Schedule(now);
    }
    public void TriggerJump(DateTime now){
        int next=random.Next(JumpLines.Length-(lastJump>=0?1:0));
        if(lastJump>=0&&next>=lastJump)next++;
        lastJump=next;Text=JumpLines[next];Until=now.AddSeconds(6);Schedule(now);
    }
}
public class SpeechBubble:CountBadge {
    static readonly QuotaSnapshot EmptyQuota=new QuotaSnapshot();
    static readonly UsageSnapshot EmptyUsage=new UsageSnapshot();
    static Font SpeechFont(){return new Font("Noto Serif SC",12,FontStyle.Regular,GraphicsUnit.Pixel);}
    static string WrapPhrase(string text){int comma=text.IndexOf('，');return text.Length>18&&comma>=0?text.Insert(comma+1,"\n"):text;}
    public static Size MeasureText(string text,double dpi){
        text=WrapPhrase(text);
        using(var b=new Bitmap(1,1))using(var g=Graphics.FromImage(b))using(var font=SpeechFont()){
            float width=Math.Max(100,Math.Min(202,g.MeasureString(text,font).Width));
            var measured=g.MeasureString(text,font,new SizeF(width,1000));
            return new Size((int)Math.Ceiling((width+24)*dpi),(int)Math.Ceiling((measured.Height+22)*dpi));
        }
    }
    public static Rectangle Place(Rectangle pet,Rectangle toolbar,Rectangle badge,Rectangle area,Size size,double dpi){
        int gap=(int)Math.Ceiling(10*dpi);
        bool right=pet.Left+pet.Width/2<area.Left+area.Width/2;
        int x=right?pet.Right+gap:pet.Left-size.Width-gap;
        var candidates=new List<Rectangle>{new Rectangle(x,pet.Top,size.Width,size.Height)};
        if(!badge.IsEmpty){candidates.Add(new Rectangle(x,badge.Top-gap-size.Height,size.Width,size.Height));candidates.Add(new Rectangle(x,badge.Bottom+gap,size.Width,size.Height));}
        candidates.Add(new Rectangle(pet.Left+(pet.Width-size.Width)/2,pet.Top-gap-size.Height,size.Width,size.Height));
        candidates.Add(new Rectangle(pet.Left+(pet.Width-size.Width)/2,Math.Max(pet.Bottom,toolbar.Bottom)+gap,size.Width,size.Height));
        foreach(var candidate in candidates){
            var r=candidate;r.X=Math.Max(area.Left,Math.Min(r.X,area.Right-size.Width));r.Y=Math.Max(area.Top,Math.Min(r.Y,area.Bottom-size.Height));
            if(area.Contains(r)&&!r.IntersectsWith(pet)&&!r.IntersectsWith(toolbar)&&!r.IntersectsWith(badge))return r;
        }
        return Rectangle.Empty;
    }
    public void PresentText(string text,Rectangle bounds,double dpi){Present(text,bounds,dpi,EmptyQuota,EmptyUsage);}
    protected override Bitmap DrawContent(string text,Size size,double dpi,QuotaSnapshot q,UsageSnapshot u){return RenderSpeech(text,size,dpi);}
    public static Bitmap RenderSpeech(string text,Size size,double dpi){
        var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bitmap)){
            g.ScaleTransform((float)dpi,(float)dpi);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            float w=(float)(size.Width/dpi),h=(float)(size.Height/dpi);
            using(var shape=Round(2,2,w-4,h-4,12)){
                using(var fill=new LinearGradientBrush(new PointF(0,0),new PointF(0,h),Color.FromArgb(245,39,40,44),Color.FromArgb(247,26,27,30)))g.FillPath(fill,shape);
                using(var border=new Pen(Color.FromArgb(90,180,186,195),1))g.DrawPath(border,shape);
            }
            using(var font=SpeechFont())using(var ink=new SolidBrush(Color.FromArgb(241,243,245)))
                g.DrawString(WrapPhrase(text),font,ink,new RectangleF(12,10,w-24,h-18));
        }
        return bitmap;
    }
}
public static class SpeechTests {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(){
        var now=DateTime.UtcNow;var engine=new SpeechEngine(now,7);
        string eventFolder=Path.Combine(CompanionFiles.Data,"speech-events");Directory.CreateDirectory(eventFolder);
        File.WriteAllText(Path.Combine(eventFolder,"test.json"),new JavaScriptSerializer().Serialize(new SpeechEvent{id="disk-event",@event="UserPromptSubmit",time=now.ToString("o")}));
        var diskEngine=new SpeechEngine(now,1);diskEngine.Poll(now);Check(diskEngine.Text==SpeechEngine.StartLine,"disk queue deserialization");
        Check((engine.NextRandom-now).TotalSeconds>=180&&(engine.NextRandom-now).TotalSeconds<=420,"random interval bounds");
        engine.Accept(new SpeechEvent{id="old",@event="Stop",time=now.AddMinutes(-1).ToString("o")},now);
        Check(engine.Text==null,"old events not replayed");
        engine.Accept(new SpeechEvent{id="start",@event="UserPromptSubmit",time=now.ToString("o")},now);
        Check(engine.Text==SpeechEngine.StartLine,"task start line");
        var until=engine.Until;
        engine.Accept(new SpeechEvent{id="hook-duplicate",@event="UserPromptSubmit",time=now.ToString("o")},now.AddSeconds(3));
        Check(engine.Until==until,"hook/log coalescing");
        engine.Accept(new SpeechEvent{id="stop",@event="Stop",time=now.ToString("o")},now.AddSeconds(1));
        Check(engine.Text==SpeechEngine.EndLine,"completion takes priority");
        engine.Tick(now.AddSeconds(6),true);Check(engine.Text==null,"automatic dismissal");
        var next=engine.NextRandom;engine.Tick(next,true);var first=engine.Text;
        Check(Array.IndexOf(SpeechEngine.RandomLines,first)>=0,"random only from remaining four lines");
        next=engine.NextRandom;engine.Tick(next,true);Check(engine.Text!=first,"no consecutive random repeat");
        engine.Accept(new SpeechEvent{id="cancel",@event="Interrupt",time=next.ToString("o")},next);
        Check(engine.Text==null,"interruption never celebrates completion");
        var jumpEngine=new SpeechEngine(now,11);
        jumpEngine.TriggerJump(now);var firstJump=jumpEngine.Text;
        Check(Array.IndexOf(SpeechEngine.JumpLines,firstJump)>=0&&jumpEngine.Until==now.AddSeconds(6),"jump uses one of all six lines once");
        jumpEngine.TriggerJump(now.AddSeconds(12));
        Check(jumpEngine.Text!=firstJump,"consecutive jumps do not repeat the same line");
        var area=new Rectangle(0,0,1920,1080);var pet=new Rectangle(900,420,150,160);var toolbar=new Rectangle(900,600,150,50);
        foreach(double dpi in new[]{1.0,1.5,2.0}){
            var size=SpeechBubble.MeasureText(SpeechEngine.StartLine,dpi);
            var badge=new BadgePlacement().Place(pet,toolbar,area,new Size((int)(210*dpi),(int)(166*dpi)),dpi);
            var target=SpeechBubble.Place(pet,toolbar,badge,area,size,dpi);
            Check(!target.IsEmpty&&area.Contains(target)&&!target.IntersectsWith(pet)&&!target.IntersectsWith(toolbar)&&!target.IntersectsWith(badge),"speech avoids pet, toolbar and stats panel");
            Check(size.Width<=226*dpi+1&&size.Height<=80*dpi,"compact wrapped speech");
        }
        var lines=new List<string>{SpeechEngine.StartLine,SpeechEngine.EndLine};lines.AddRange(SpeechEngine.RandomLines);
        using(var sheet=new Bitmap(720,360))using(var g=Graphics.FromImage(sheet)){
            g.Clear(Color.FromArgb(22,22,22));
            for(int i=0;i<lines.Count;i++)using(var bubble=SpeechBubble.RenderSpeech(lines[i],SpeechBubble.MeasureText(lines[i],1.5),1.5))g.DrawImageUnscaled(bubble,10+(i%2)*360,10+(i/2)*115);
            sheet.Save(Path.Combine(CompanionFiles.Data,"speech-preview.png"));
        }
        File.WriteAllText(Path.Combine(CompanionFiles.Data,"speech-tests.txt"),"PASS: event freshness, exact start/end text, hook/log coalescing, event priority, auto dismiss, random interval, jump speech from all six phrases without immediate repeat, interrupt handling, compact text wrapping and obstacle avoidance at 100/150/200% DPI.\n");
    }
}
