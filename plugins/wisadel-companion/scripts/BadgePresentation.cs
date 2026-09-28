using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class HoverDelay {
    readonly long delay;long since=-1;IntPtr window;
    public HoverDelay(long ms){delay=ms;}
    public bool Update(bool eligible,long now,IntPtr current){
        if(!eligible){since=-1;window=IntPtr.Zero;return false;}
        if(since<0||window!=current){since=now;window=current;}
        return now-since>=delay;
    }
}
public class OneShotHover {
    readonly long delay;long since=-1;IntPtr window;bool fired;
    public OneShotHover(long ms){delay=ms;}
    public bool Update(bool eligible,long now,IntPtr current){
        if(!eligible||current==IntPtr.Zero){since=-1;window=IntPtr.Zero;fired=false;return false;}
        if(since<0||window!=current){since=now;window=current;fired=false;}
        if(fired||now-since<delay)return false;
        fired=true;return true;
    }
}
public class BadgePlacement {
    public string Side{get;private set;}
    public Rectangle Place(Rectangle pet,Rectangle toolbar,Rectangle area,Size size,double dpi){
        int gap=(int)Math.Ceiling(10*dpi);
        // Reserve the native toolbar shell, including its padding, on either side.
        Rectangle occupied=pet;
        if(!toolbar.IsEmpty){toolbar.Inflate((int)Math.Ceiling(8*dpi),0);occupied=Rectangle.Union(pet,toolbar);}
        int right=occupied.Right+gap,left=occupied.Left-gap-size.Width;
        bool fitsRight=right+size.Width<=area.Right, fitsLeft=left>=area.Left;
        Side=pet.Left+pet.Width/2<area.Left+area.Width/2?"right":"left";
        // Face inward according to the pet's current monitor half; fall back only if needed.
        if(Side=="right"&&!fitsRight&&fitsLeft)Side="left";
        else if(Side=="left"&&!fitsLeft&&fitsRight)Side="right";
        int x=Side=="right"?right:left;
        // On a very narrow work area neither side can fit. Clamp without oscillating.
        x=Math.Max(area.Left,Math.Min(x,area.Right-size.Width));
        int y=Math.Max(area.Top,Math.Min(pet.Top+(pet.Height-size.Height)/2,area.Bottom-size.Height));
        return new Rectangle(x,y,size.Width,size.Height);
    }
}
public class CountBadge:Form {
    [StructLayout(LayoutKind.Sequential)]struct P{public int X,Y;public P(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)]struct S{public int W,H;public S(int w,int h){W=w;H=h;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Op,Flags,Alpha,Format;}
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dc,ref P dst,ref S size,IntPtr src,ref P source,uint key,ref Blend blend,uint flags);
    string oldText;Rectangle oldBounds;double oldDpi;
    public CountBadge(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;}
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x080800A0;return p;}}
    protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected static GraphicsPath Round(float x,float y,float width,float height,float radius){
        var p=new GraphicsPath();float d=radius*2;
        p.AddArc(x,y,d,d,180,90);p.AddArc(x+width-d,y,d,d,270,90);
        p.AddArc(x+width-d,y+height-d,d,d,0,90);p.AddArc(x,y+height-d,d,d,90,90);p.CloseFigure();return p;
    }
    static bool HasCjk(string text){foreach(char c in text)if(c>=0x2e80)return true;return false;}
    static void Label(Graphics g,string text,float x,float y,float size,Color color,bool right){
        using(var font=new Font(HasCjk(text)?"Noto Serif SC":SerifFamily,size,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(color))
        using(var format=new StringFormat{Alignment=right?StringAlignment.Far:StringAlignment.Near,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,x,y,format);
    }
    // Baskerville Old Face has lining figures; Georgia's oldstyle figures vary in height.
    const string SerifFamily="Baskerville Old Face";
    const float TokenUnitSize=10;
    const float MoneyMainSize=21;
    const float MoneyFractionSize=11;
    static Font Serif(float size){return new Font(SerifFamily,size,FontStyle.Regular,GraphicsUnit.Pixel);}
    static bool IsUnit(char c){return c=='亿'||c=='千'||c=='百'||c=='万';}
    static float RunWidth(Graphics g,string run,bool unit){
        using(var font=unit?new Font("Noto Serif SC",TokenUnitSize,FontStyle.Regular,GraphicsUnit.Pixel):Serif(14))
            return g.MeasureString(run,font,Int32.MaxValue,StringFormat.GenericTypographic).Width;
    }
    public static float TokenWidth(string text){
        if(text=="—")return NumberWidth(text);
        using(var bitmap=new Bitmap(1,1))using(var g=Graphics.FromImage(bitmap)){
            float width=0;int start=0;
            while(start<text.Length){bool unit=IsUnit(text[start]);int end=start+1;while(end<text.Length&&IsUnit(text[end])==unit)end++;
                width+=RunWidth(g,text.Substring(start,end-start),unit)+(end<text.Length?1.5f:0);start=end;}
            return width;
        }
    }
    static void Token(Graphics g,string text,float right,float y,Color color){
        if(text=="—"){Number(g,text,right,y,color);return;}
        float x=right-TokenWidth(text);int start=0;
        using(var ink=new SolidBrush(color))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
            while(start<text.Length){bool unit=IsUnit(text[start]);int end=start+1;while(end<text.Length&&IsUnit(text[end])==unit)end++;
                string run=text.Substring(start,end-start);
                using(var font=unit?new Font("Noto Serif SC",TokenUnitSize,FontStyle.Regular,GraphicsUnit.Pixel):Serif(14))
                    g.DrawString(run,font,ink,x,y,format);
                x+=RunWidth(g,run,unit)+(end<text.Length?1.5f:0);start=end;
            }
        }
    }
    public static float NumberWidth(string text,float size=14){
        using(var bitmap=new Bitmap(1,1))using(var g=Graphics.FromImage(bitmap))
        using(var font=Serif(size))
            return g.MeasureString(text,font,Int32.MaxValue,StringFormat.GenericTypographic).Width;
    }
    public static Size Measure(string text,UsageSnapshot usage,double dpi){
        float width=Math.Max(210,Math.Max(NumberWidth(text,16)+28,Math.Max(TokenWidth(usage.Tokens),MoneyWidth(usage.Dollars))+80));
        return new Size((int)Math.Ceiling(width*dpi),(int)Math.Ceiling(166*dpi));
    }
    static void Number(Graphics g,string text,float right,float y,Color color,float size=14){
        using(var font=Serif(size))
        using(var brush=new SolidBrush(color))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
            format.Alignment=StringAlignment.Far;
            format.FormatFlags|=StringFormatFlags.NoWrap;
            g.DrawString(text,font,brush,right,y,format);
        }
    }
    public static float MoneyWidth(string dollars){
        int dot=dollars.IndexOf('.');
        if(dollars=="—")return NumberWidth(dollars);
        return dot<0?NumberWidth("$"+dollars,MoneyMainSize):NumberWidth("$"+dollars.Substring(0,dot),MoneyMainSize)+NumberWidth(dollars.Substring(dot),MoneyFractionSize);
    }
    static void Money(Graphics g,string dollars,float right,float y,Color color){
        int dot=dollars.IndexOf('.');
        if(dollars=="—"){Number(g,dollars,right,y+4,color);return;}
        if(dot<0){Number(g,"$"+dollars,right,y,color,MoneyMainSize);return;}
        string whole="$"+dollars.Substring(0,dot),fraction=dollars.Substring(dot);
        float fractionWidth=NumberWidth(fraction,MoneyFractionSize);
        Number(g,whole,right-fractionWidth,y,color,MoneyMainSize);
        Number(g,fraction,right,y+10,Color.FromArgb(166,188,192),MoneyFractionSize);
    }
    static void WindowRow(Graphics g,string name,QuotaWindow w,QuotaSnapshot quota,float y,float width){
        Color cyan=Color.FromArgb(155,217,221),dim=Color.FromArgb(157,161,168);
        string percent=quota.Percent(w);
        bool available=percent.EndsWith("%");
        double remaining=available?w.Remaining.Value:0;
        Color lit=remaining<10?Color.FromArgb(236,145,136):cyan;
        Label(g,name,14,y,10,dim,false);
        Number(g,percent,width-14,y-2,available?lit:dim);
        Label(g,w==null?"Reset —":w.ResetText(),14,y+14,9,dim,false);
    }
    public static Bitmap Render(string text,Size size,double dpi){return Render(text,size,dpi,new QuotaSnapshot());}
    public static Bitmap Render(string text,Size size,double dpi,QuotaSnapshot quota){return Render(text,size,dpi,quota,new UsageSnapshot());}
    public static Bitmap Render(string text,Size size,double dpi,QuotaSnapshot quota,UsageSnapshot usage){
        var image=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform((float)dpi,(float)dpi);
            float width=(float)(size.Width/dpi),height=(float)(size.Height/dpi);
            // Soft translucent rim and a dark translucent gradient. No screen capture.
            for(int n=4;n>=1;n--)using(var edge=Round(4-n,4-n,width-8+2*n,height-8+2*n,19+n))
                using(var glow=new Pen(Color.FromArgb(5,220,225,230),1.5f))g.DrawPath(glow,edge);
            using(var path=Round(4,4,width-8,height-8,19)){
                using(var bg=new LinearGradientBrush(new PointF(0,4),new PointF(0,height-4),Color.FromArgb(235,35,36,39),Color.FromArgb(241,23,24,27)))g.FillPath(bg,path);
                using(var stroke=new LinearGradientBrush(new PointF(0,4),new PointF(0,height-4),Color.FromArgb(82,225,228,233),Color.FromArgb(20,170,180,195)))
                using(var edge=new Pen(stroke,1))g.DrawPath(edge,path);
            }
            Number(g,text,width/2+NumberWidth(text,16)/2,10,Color.FromArgb(242,242,245),16);
            using(var line=new Pen(Color.FromArgb(20,255,255,255),1))g.DrawLine(line,14,32,width-14,32);
            WindowRow(g,"5H",quota.fiveHour,quota,39,width);
            WindowRow(g,"WEEK",quota.week,quota,67,width);
            using(var line=new Pen(Color.FromArgb(20,255,255,255),1))g.DrawLine(line,14,98,width-14,98);
            Label(g,"今日 TOKEN",14,109,10,Color.FromArgb(157,161,168),false);
            Token(g,usage.Tokens,width-14,105,Color.FromArgb(194,218,222));
            Money(g,usage.Dollars,width-14,122,Color.FromArgb(194,218,222));
            Label(g,quota.StatusText.Replace("本机时间 · ",""),14,147,9,Color.FromArgb(145,151,160),false);
        }
        return image;
    }
    public void Present(string text,Rectangle bounds,double dpi,QuotaSnapshot quota,UsageSnapshot usage){
        string key=text+"|"+quota.Key+"|"+usage.Key;
        if(key==oldText&&bounds==oldBounds&&dpi==oldDpi)return;
        Bounds=bounds;
        using(var bitmap=DrawContent(text,bounds.Size,dpi,quota,usage)){
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),native=bitmap.GetHbitmap(Color.FromArgb(0)),old=SelectObject(memory,native);
            try{var dst=new P(bounds.X,bounds.Y);var size=new S(bounds.Width,bounds.Height);var src=new P(0,0);var blend=new Blend{Op=0,Flags=0,Alpha=255,Format=1};
                if(!UpdateLayeredWindow(Handle,screen,ref dst,ref size,memory,ref src,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }finally{SelectObject(memory,old);DeleteObject(native);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
        }
        oldText=key;oldBounds=bounds;oldDpi=dpi;
    }
    protected virtual Bitmap DrawContent(string text,Size size,double dpi,QuotaSnapshot quota,UsageSnapshot usage){return Render(text,size,dpi,quota,usage);}
}
public static class BadgeTests {
    static void Check(bool c,string message){if(!c)throw new Exception(message);}
    public static void Run(){
        string persistence=Path.Combine(CompanionFiles.Data,"persistence-test.json");
        var model=new CounterModel(new SavedStats());DateTime day=new DateTime(2026,9,27,23,59,59);
        model.Clock=()=>day;model.Keyboard(1,false);model.Keyboard(1,true);StatsStore.Save(persistence,model.Data);
        day=day.AddSeconds(2);model.Keyboard(2,false);StatsStore.Save(persistence,model.Data);
        var restored=StatsStore.Load(persistence);Check(restored.Total.Keyboard==2&&restored.Days.Count==2,"midnight and restart preserve total");
        File.WriteAllText(persistence,"damaged");Check(StatsStore.Load(persistence).Total.Keyboard>=1,"damaged primary restores backup");
        var gate=new HoverDelay(200);var w=(IntPtr)1;
        Check(!gate.Update(true,0,w),"immediate hover hidden");Check(!gate.Update(true,199,w),"short hover hidden");Check(gate.Update(true,200,w),"200ms hover shown");
        Check(!gate.Update(false,620,w),"leave hides immediately");Check(!gate.Update(true,650,w),"re-entry resets delay");Check(!gate.Update(true,1300,(IntPtr)2),"new window resets delay");
        var jumpHold=new OneShotHover(5000);
        Check(!jumpHold.Update(true,0,w)&&!jumpHold.Update(true,4999,w),"jump hold waits five seconds");
        Check(jumpHold.Update(true,5000,w)&&!jumpHold.Update(true,8000,w),"jump hold fires once per hover");
        Check(!jumpHold.Update(false,8100,w)&&!jumpHold.Update(true,8200,w),"leaving resets jump hold");
        Check(jumpHold.Update(true,13200,w),"new hover can fire once again");
        Check(!jumpHold.Update(true,13250,(IntPtr)2),"changing pet window resets jump hold");
        foreach(double dpi in new[]{1.0,1.5,2.0}){
            var pet=new Rectangle(400,200,(int)(112*dpi),(int)(121*dpi));var toolbar=new Rectangle(360,pet.Bottom+8,(int)(180*dpi),(int)(40*dpi));
            var area=new Rectangle(0,0,2560,1600);var placement=new BadgePlacement();
            var size=CountBadge.Measure("143,336",new UsageSnapshot{ready=true,total_tokens=9000000000,usd_estimate="9999",daily_tokens=136000000,daily_usd_estimate="1077.5956",daily_end=DateTimeOffset.UtcNow.AddDays(1).ToString("o")},dpi);
            var b=placement.Place(pet,toolbar,area,size,dpi);
            Check(!b.IntersectsWith(toolbar)&&!b.IntersectsWith(pet)&&b.Left>pet.Right&&area.Contains(b),"right side and native toolbar spacing");
            b=placement.Place(new Rectangle(1400,200,112,121),Rectangle.Empty,area,size,dpi);
            Check(placement.Side=="left","crossing midpoint switches inward side");
            b=placement.Place(new Rectangle(2400,200,112,121),Rectangle.Empty,area,size,dpi);
            Check(placement.Side=="left"&&area.Contains(b)&&b.Right<2400,"flip only at right edge");
            b=placement.Place(pet,toolbar,area,size,dpi);
            Check(placement.Side=="right","returning to left half switches right");
            b=placement.Place(new Rectangle(10,1500,112,121),Rectangle.Empty,area,size,dpi);
            Check(placement.Side=="right"&&area.Contains(b),"left edge flips; bottom clamps");
        }
        var stateful=new BadgePlacement();var screen=new Rectangle(0,0,1920,1080);var compact=new Size(250,194);
        stateful.Place(new Rectangle(1600,50,112,121),Rectangle.Empty,screen,compact,1);
        Check(stateful.Side=="left","initial side uses available room");
        stateful.Place(new Rectangle(600,50,112,121),Rectangle.Empty,screen,compact,1);
        Check(stateful.Side=="right","left half always prefers right side");
        var negative=new Rectangle(-1920,-200,1920,1080);
        var edge=stateful.Place(new Rectangle(-1900,-190,112,121),Rectangle.Empty,negative,compact,1.5);
        Check(negative.Contains(edge)&&stateful.Side=="right","negative monitor origin and top clamp");
        var narrow=new Rectangle(0,0,400,600);var crowded=new Rectangle(150,0,112,121);
        edge=stateful.Place(crowded,Rectangle.Empty,narrow,compact,1);string stableSide=stateful.Side;
        for(int i=0;i<10;i++)Check(stateful.Place(crowded,Rectangle.Empty,narrow,compact,1)==edge&&stateful.Side==stableSide,"narrow screen fallback must not oscillate");
        Check(narrow.Contains(edge),"narrow screen remains in bounds");
        var fixture=QuotaMonitor.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":31,\"windowDurationMins\":300,\"resetsAt\":1893456000},\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":10080,\"resetsAt\":1893715200}}}");
        using(var font=new Font("Baskerville Old Face",14,FontStyle.Regular,GraphicsUnit.Pixel))Check(font.Name=="Baskerville Old Face","installed lining serif family is used");
        using(var font=new Font("Noto Serif SC",14,FontStyle.Regular,GraphicsUnit.Pixel))Check(font.Name=="Noto Serif SC","installed CJK serif family is used");
        Check(TokenText.Compact(130000000)=="1亿3000万","hundred million with ten-thousand precision");
        Check(TokenText.Compact(80499508)=="8050万","round to nearest ten thousand");
        Check(TokenText.Compact(136000000)=="1亿3600万","exact ten-thousand display");
        Check(TokenText.Compact(123456789)=="1亿2346万","retain million and ten-thousand digits");
        Check(CountBadge.TokenWidth("1亿3600万")>CountBadge.NumberWidth("13600"),"CJK units have their own measured width");
        Check(TokenText.Compact(99995000)=="1亿","ten-thousand rounding carries into hundred million");
        Check(TokenText.Compact(100010000)=="1亿1万","small remainder keeps its ten-thousand unit");
        Check(TokenText.Compact(10000000)=="1000万","exact ten million");
        Check(TokenText.Compact(5300000)=="530万","smaller values retain ten-thousand precision");
        Check(TokenText.Compact(9999)=="9,999","sub-ten-thousand values remain exact");
        Check(TokenText.Compact(0)=="0"&&TokenText.Compact(-1)=="—","zero and unavailable");
        var dailyExample=new UsageSnapshot{ready=true,total_tokens=9876543210,daily_tokens=130000000,
            usd_estimate="9876",daily_usd_estimate="24.5678",daily_end=DateTimeOffset.UtcNow.AddDays(1).ToString("o")};
        Check(dailyExample.Tokens=="1亿3000万"&&dailyExample.Dollars=="24.5678","daily display does not use lifetime figures");
        dailyExample.daily_end=DateTimeOffset.UtcNow.AddSeconds(-1).ToString("o");
        Check(dailyExample.Tokens=="—"&&dailyExample.Dollars=="—","expired day is not shown");
        Check(CountBadge.MoneyWidth("24.5678")>CountBadge.NumberWidth("$24",21)&&CountBadge.MoneyWidth("24.5678")<CountBadge.NumberWidth("$24.5678",21),"joined dollar and enlarged whole use a smaller fraction");
        Check(fixture.fiveHour.Remaining==69&&fixture.week.Remaining==80,"remaining percentage");
        var alertState=new LowQuotaAlertState();
        var alert=new QuotaSnapshot{status="ok",fetchedUtc=DateTime.UtcNow,fiveHour=new QuotaWindow{usedPercent=95,resetsAt=1893456000}};
        Check(!LowQuotaAlert.Due(alert,alertState),"exactly 5 percent does not notify");
        alert.fiveHour.usedPercent=95.1;Check(LowQuotaAlert.Due(alert,alertState),"below 5 percent notifies");
        var restoredAlert=new LowQuotaAlertState{lastReset=alert.fiveHour.resetsAt.Value};
        Check(!LowQuotaAlert.Due(alert,restoredAlert),"persisted window prevents duplicate after restart");
        alert.fiveHour.resetsAt=1893474000;Check(LowQuotaAlert.Due(alert,restoredAlert),"next quota window may notify");
        alert.fetchedUtc=DateTime.UtcNow.AddMinutes(-3);Check(!LowQuotaAlert.Due(alert,alertState),"stale quota never triggers alert");
        alert.fetchedUtc=DateTime.UtcNow;alert.fiveHour.resetsAt=1;Check(!LowQuotaAlert.Due(alert,alertState),"expired window never triggers alert");
        alert.fiveHour.resetsAt=null;Check(!LowQuotaAlert.Due(alert,alertState),"unknown reset never triggers alert");
        alert.fiveHour=null;Check(!LowQuotaAlert.Due(alert,alertState),"missing quota never triggers alert");
        Check(QuotaMonitor.Parse("{\"rateLimits\":null}").fiveHour==null,"missing data never becomes 100%");
        var multi=QuotaMonitor.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99,\"windowDurationMins\":300}},\"rateLimitsByLimitId\":{\"codex\":{\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":300}}}}");
        Check(multi.fiveHour.Remaining==80&&multi.week==null,"prefer named bucket; classify by duration");
        var expired=new QuotaSnapshot{status="ok",fetchedUtc=DateTime.UtcNow.AddMinutes(-3)};
        Check(!expired.Fresh&&expired.Percent(fixture.fiveHour)=="—","stale never appears live");
        Check(fixture.fiveHour.ResetText()=="Reset "+new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero).ToLocalTime().ToString("MM-dd HH:mm"),"local reset conversion");
        var sample=new UsageSnapshot{ready=true,total_tokens=9000000000,usd_estimate="9999",daily_tokens=123456789,daily_usd_estimate="1077.5956",daily_end=DateTimeOffset.UtcNow.AddDays(1).ToString("o"),unpriced_tokens=227464199};
        var previewSize=CountBadge.Measure("143,336",sample,1.5);
        Check(previewSize.Width==315&&previewSize.Height==249,"compact panel dimensions");
        Check(CountBadge.NumberWidth(sample.Tokens)+80<=previewSize.Width/1.5,"full token number fits without clipping");
        using(var img=CountBadge.Render("143,336",previewSize,1.5,fixture,sample)){
            Check(img.GetPixel(0,0).A==0,"transparent corners");img.Save(Path.Combine(CompanionFiles.Data,"badge-preview.png"));
            using(var preview=new Bitmap(img.Width+40,img.Height+40)){
                using(var g=Graphics.FromImage(preview)){g.Clear(Color.FromArgb(22,22,22));g.DrawImageUnscaled(img,20,20);}
                preview.Save(Path.Combine(CompanionFiles.Data,"badge-preview-dark.png"));
            }
        }
        File.WriteAllText(Path.Combine(CompanionFiles.Data,"badge-tests.txt"),"PASS: persistence and backup recovery; 200ms badge hover; one-shot five-second jump hover and reset; lateral placement and toolbar spacing at 100/150/200% DPI; switch inward side across midpoint; edge fallback; negative monitor origin; narrow screen stable fallback; compact dimensions and full token width; alpha corners; quota remaining, bucket selection, missing/stale data, local reset time.\n");
    }
}
