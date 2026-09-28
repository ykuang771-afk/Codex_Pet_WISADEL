using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class CompanionOptions {
    public bool paused=false, overlay_enabled=true, autostart=true;
    public string display_period="total";
}
public static class CompanionFiles {
    public static string Data=Environment.GetEnvironmentVariable("WISADEL_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WisadelCompanion");
    public static string Stats=Environment.GetEnvironmentVariable("WISADEL_STATS_PATH") ?? Path.Combine(Data,"stats.json");
    public static T Read<T>(string name,T fallback) where T:class {
        try{return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(Path.Combine(Data,name))) ?? fallback;}catch{return fallback;}
    }
    public static void Write(string name,object value) {
        string path=Path.Combine(Data,name),temp=path+"."+Process.GetCurrentProcess().Id+".tmp";
        Directory.CreateDirectory(Data);
        File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
}
public class PetPosition {
    public Rectangle Bounds;
    public Rectangle Toolbar;
    public IntPtr Window;
    public DateTime Time;
    public bool Found, AppRunning;
}
public class PetTracker {
    public volatile bool Stop;
    public volatile PetPosition Latest=new PetPosition();
    delegate bool WindowCallback(IntPtr h,IntPtr p);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback c,IntPtr p);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
    [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int i);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr h,uint flags);
    public void Run() {
        while(!Stop) {
            var next=new PetPosition{Time=DateTime.UtcNow};
            try {
                EnumWindows((h,p)=>{
                    uint id;GetWindowThreadProcessId(h,out id);
                    try {
                        var proc=Process.GetProcessById((int)id);
                        if(proc.ProcessName!="ChatGPT" && proc.ProcessName!="Codex")return true;
                        next.AppRunning=true;
                        // Only inspect the app's visible floating, layered tool window.
                        // Never traverse the main chat window or other applications.
                        int style=GetWindowLong(h,-20);
                        if(!IsWindowVisible(h)||IsIconic(h)||(style&0x80088)!=0x80088)return true;
                        var root=AutomationElement.FromHandle(h);
                        var candidates=root.FindAll(TreeScope.Descendants,new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Image),
                            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Group),
                            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Custom)));
                        foreach(AutomationElement element in candidates) {
                            var c=element.Current;
                            if(c.IsOffscreen || c.ClassName==null || !c.ClassName.Contains("codex-avatar-button"))continue;
                            if(!(c.Name.Contains("维什戴尔")||c.Name.ToLowerInvariant().Contains("wisadel")))continue;
                            var r=c.BoundingRectangle;
                            if(r.IsEmpty||r.Width<16||r.Height<16||r.Width>600||r.Height>650)continue;
                            next.Bounds=Rectangle.FromLTRB((int)Math.Round(r.Left),(int)Math.Round(r.Top),(int)Math.Round(r.Right),(int)Math.Round(r.Bottom));
                            next.Window=h;next.Found=true;
                            // Read only native control geometry, not notification/chat text.
                            var buttons=root.FindAll(TreeScope.Descendants,new OrCondition(
                                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)));
                            foreach(AutomationElement button in buttons){
                                var bc=button.Current;if(bc.IsOffscreen)continue;
                                string name=bc.Name;
                                if(name!="开始新聊天"&&name!="开始语音聊天"&&name!="隐藏活动"&&name!="显示活动"&&name!="Start new chat"&&name!="Start voice chat"&&name!="Hide activity"&&name!="Show activity")continue;
                                var br=bc.BoundingRectangle;
                                if(br.IsEmpty||br.Height<5||br.Height>180||br.Width>600)continue;
                                var rect=Rectangle.FromLTRB((int)Math.Floor(br.Left),(int)Math.Floor(br.Top),(int)Math.Ceiling(br.Right),(int)Math.Ceiling(br.Bottom));
                                next.Toolbar=next.Toolbar.IsEmpty?rect:Rectangle.Union(next.Toolbar,rect);
                            }
                            return false;
                        }
                    }catch{}
                    return true;
                },IntPtr.Zero);
            }catch{}
            Latest=next;
            Thread.Sleep(120);
        }
    }
}
public class CompanionContext:ApplicationContext {
    [StructLayout(LayoutKind.Sequential)]struct RawDevice{public ushort Page,Usage;public uint Flags;public IntPtr Target;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
    class InputWindow:NativeWindow {
        readonly CompanionContext parent;
        public InputWindow(CompanionContext p){parent=p;CreateHandle(new CreateParams{Caption="WisadelInputReceiver",Parent=(IntPtr)(-3)});}
        protected override void WndProc(ref Message m){if(m.Msg==0xFF)parent.Input(m.LParam);if(m.Msg==0xFE)parent.model.Held.Clear();base.WndProc(ref m);}
    }
    readonly CounterModel model;
    readonly InputWindow receiver;
    readonly CountBadge badge=new CountBadge();
    readonly SpeechBubble speechBubble=new SpeechBubble();
    readonly SpeechEngine speech=new SpeechEngine(DateTime.UtcNow,Environment.TickCount);
    readonly BadgePlacement placement=new BadgePlacement();
    readonly PetTracker tracker=new PetTracker();
    readonly QuotaMonitor quota=new QuotaMonitor();
    readonly UsageMonitor usage=new UsageMonitor();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly NotifyIcon tray=new NotifyIcon();
    LowQuotaAlertState lowQuotaAlert=CompanionFiles.Read("quota-alert.json",new LowQuotaAlertState());
    readonly EventWaitHandle stopSignal;
    CompanionOptions options=new CompanionOptions();
    DateTime lastSave=DateTime.MinValue,lastSettings=DateTime.MinValue,lastApp=DateTime.UtcNow;
    bool dirty,hovered,saveError;
    string speechState="idle";
    long showTransitions,hideTransitions;
    readonly Stopwatch hoverClock=Stopwatch.StartNew();
    readonly HoverDelay hoverDelay=new HoverDelay(200);
    readonly OneShotHover jumpHold=new OneShotHover(5000);
    public CompanionContext(EventWaitHandle signal) {
        stopSignal=signal;model=new CounterModel(StatsStore.Load(CompanionFiles.Stats));dirty=true;
        quota.Start();
        receiver=new InputWindow(this);
        var devices=new[]{new RawDevice{Page=1,Usage=6,Flags=0x2100,Target=receiver.Handle},new RawDevice{Page=1,Usage=2,Flags=0x2100,Target=receiver.Handle}};
        if(!RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(RawDevice))))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var worker=new Thread(tracker.Run){IsBackground=true,Name="WisadelPetPosition"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();
        tray.Icon=File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wisadel.ico"))?new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wisadel.ico")):SystemIcons.Application;
        tray.Text="维什戴尔助手 · 悬停宠物显示计数";
        var menu=new ContextMenuStrip();
        menu.Items.Add("暂停 / 继续统计",null,(s,e)=>{options.paused=!options.paused;CompanionFiles.Write("settings.json",options);});
        menu.Items.Add("显示 / 隐藏悬停计数",null,(s,e)=>{options.overlay_enabled=!options.overlay_enabled;CompanionFiles.Write("settings.json",options);});
        menu.Items.Add("退出助手",null,(s,e)=>{options.autostart=false;CompanionFiles.Write("settings.json",options);ExitThread();});
        tray.ContextMenuStrip=menu;tray.Visible=true;
        timer.Interval=50;timer.Tick+=(s,e)=>Tick();timer.Start();
    }
    void Input(IntPtr handle) {
        uint size=0,header=(uint)(8+IntPtr.Size*2);
        if(GetRawInputData(handle,0x10000003,IntPtr.Zero,ref size,header)==uint.MaxValue||size<header||size>65536)return;
        var data=Marshal.AllocHGlobal((int)size);
        try {
            if(GetRawInputData(handle,0x10000003,data,ref size,header)!=size)return;
            int type=Marshal.ReadInt32(data),o=(int)header;bool changed=false;
            if(type==1&&size>=header+16){int make=(ushort)Marshal.ReadInt16(data,o),flags=(ushort)Marshal.ReadInt16(data,o+2),vk=(ushort)Marshal.ReadInt16(data,o+6);if(vk!=255)changed=model.Keyboard(make|(vk<<16)|((flags&6)<<8),(flags&1)!=0);}
            else if(type==0&&size>=header+24)changed=model.Mouse((ushort)Marshal.ReadInt16(data,o+4));
            dirty|=changed;
        }finally{Marshal.FreeHGlobal(data);}
    }
    void Tick(){
        if(stopSignal.WaitOne(0)){ExitThread();return;}
        DateTime now=DateTime.UtcNow;
        if((now-lastSettings).TotalMilliseconds>=500){options=CompanionFiles.Read("settings.json",options);options.display_period="total";model.Paused=options.paused;usage.Tick();speech.Poll(now);CheckQuotaAlert();lastSettings=now;}
        PetPosition pet=tracker.Latest;if(pet.AppRunning)lastApp=now;
        if((now-lastApp).TotalSeconds>20){ExitThread();return;}
        Point cursor;GetCursorPos(out cursor);
        hovered=pet.Found && (now-pet.Time).TotalSeconds<2 && PetTracker.IsWindowVisible(pet.Window) && pet.Bounds.Contains(cursor)
            && PetTracker.GetAncestor(PetTracker.WindowFromPoint(cursor),2)==pet.Window;
        bool notDragging=(GetAsyncKeyState(1)&0x8000)==0;
        bool show=hoverDelay.Update(hovered&&options.overlay_enabled&&notDragging,hoverClock.ElapsedMilliseconds,pet.Window);
        if(jumpHold.Update(hovered&&notDragging,hoverClock.ElapsedMilliseconds,pet.Window))speech.TriggerJump(now);
        if(show){
            Counts counts=model.Data.Total;
            double dpi=1;try{dpi=GetDpiForWindow(pet.Window)/96.0;}catch{}if(dpi<1)dpi=1;
            string text=(counts.Keyboard+counts.Mouse).ToString("N0");
            var area=Screen.FromRectangle(pet.Bounds).WorkingArea;
            Rectangle target=placement.Place(pet.Bounds,pet.Toolbar,area,CountBadge.Measure(text,usage.Latest,dpi),dpi);
            badge.Present(text,target,dpi,quota.Latest,usage.Latest);
            if(!badge.Visible){showTransitions++;badge.Show();}
        }else if(badge.Visible){hideTransitions++;badge.Hide();}
        bool speechAllowed=pet.Found&&(now-pet.Time).TotalSeconds<2&&PetTracker.IsWindowVisible(pet.Window)&&(GetAsyncKeyState(1)&0x8000)==0;
        speech.Tick(now,speechAllowed);
        speechState=!pet.Found?"pet_missing":(now-pet.Time).TotalSeconds>=2?"pet_stale":!PetTracker.IsWindowVisible(pet.Window)?"pet_hidden":(GetAsyncKeyState(1)&0x8000)!=0?"dragging":speech.Text==null?"idle":"no_room";
        if(speechAllowed&&speech.Text!=null){
            double dpi=1;try{dpi=Math.Max(1,GetDpiForWindow(pet.Window)/96.0);}catch{}
            var target=SpeechBubble.Place(pet.Bounds,pet.Toolbar,badge.Visible?badge.Bounds:Rectangle.Empty,Screen.FromRectangle(pet.Bounds).WorkingArea,SpeechBubble.MeasureText(speech.Text,dpi),dpi);
            if(!target.IsEmpty){speechState="showing";speechBubble.PresentText(speech.Text,target,dpi);if(!speechBubble.Visible){speechBubble.Show();try{CompanionFiles.Write("speech-last-display.json",new{text=speech.Text,visible=speechBubble.Visible,time=DateTime.UtcNow.ToString("o"),bounds=new[]{target.X,target.Y,target.Width,target.Height}});}catch{}}}
            else speechBubble.Hide();
        }else if(speechBubble.Visible)speechBubble.Hide();
        if((now-lastSave).TotalSeconds>=1){Persist();WriteStatus(pet);lastSave=now;}
    }
    void CheckQuotaAlert(){
        var snapshot=quota.Latest;
        if(!LowQuotaAlert.Due(snapshot,lowQuotaAlert))return;
        var next=new LowQuotaAlertState{lastReset=snapshot.fiveHour.resetsAt.Value};
        try{
            CompanionFiles.Write("quota-alert.json",next);
            lowQuotaAlert=next;
            tray.ShowBalloonTip(10000,"维什戴尔 · 额度低于 5%",LowQuotaAlert.Message(snapshot),ToolTipIcon.Warning);
        }catch{}
    }
    void Persist(){if(!dirty)return;try{StatsStore.Save(CompanionFiles.Stats,model.Data);dirty=false;saveError=false;}catch{saveError=true;}}
    void WriteStatus(PetPosition p){try{CompanionFiles.Write("runtime.json",new{pid=Process.GetCurrentProcess().Id,updated=DateTime.UtcNow.ToString("o"),raw_input_registered=true,pet_found=p.Found,hovered=hovered,badge_visible=badge.Visible,show_transitions=showTransitions,hide_transitions=hideTransitions,pet_rect=new[]{p.Bounds.X,p.Bounds.Y,p.Bounds.Width,p.Bounds.Height},toolbar_rect=new[]{p.Toolbar.X,p.Toolbar.Y,p.Toolbar.Width,p.Toolbar.Height},badge_rect=new[]{badge.Left,badge.Top,badge.Width,badge.Height},badge_total=model.Data.Total.Keyboard+model.Data.Total.Mouse,hover_delay_ms=200,badge_style="glass-text-compact",badge_side=placement.Side,speech_state=speechState,speech_visible=speechBubble.Visible,speech_text=speech.Text,speech_rect=new[]{speechBubble.Left,speechBubble.Top,speechBubble.Width,speechBubble.Height},display_period="total",usage_ready=usage.Latest.ready,usage_fresh=usage.Latest.Fresh,daily_tokens=usage.Latest.daily_tokens,daily_usd_estimate=usage.Latest.daily_usd_estimate,usage_day_start=usage.Latest.daily_start,usage_day_end=usage.Latest.daily_end,total_tokens=usage.Latest.total_tokens,usd_estimate=usage.Latest.usd_estimate,stats_path=CompanionFiles.Stats,quota_status=quota.Latest.status,quota_fresh=quota.Latest.Fresh,paused=model.Paused,save_error=saveError,session=model.Session});}catch{}}
    protected override void ExitThreadCore(){timer.Stop();tracker.Stop=true;Persist();quota.Dispose();usage.Dispose();try{CompanionFiles.Write("runtime.json",new{pid=0,updated=DateTime.UtcNow.ToString("o"),stopped=true,save_error=saveError});}catch{}speechBubble.Close();badge.Close();receiver.DestroyHandle();tray.Visible=false;tray.Dispose();base.ExitThreadCore();}
}
public static class CompanionProgram {
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr mode);
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,System.Text.StringBuilder text,int size,out int required);
    [STAThread]public static void Main(string[] args){
        try {
            try{SetProcessDpiAwarenessContext((IntPtr)(-4));}catch{}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(CompanionFiles.Data);
            if(Array.IndexOf(args,"--self-test")>=0){BadgeTests.Run();SpeechTests.Run();return;}
            if(Array.IndexOf(args,"--quota-check")>=0){
                using(var monitor=new QuotaMonitor()){
                    monitor.Start();var deadline=DateTime.UtcNow.AddSeconds(25);
                    while(!monitor.Latest.Fresh&&DateTime.UtcNow<deadline)Thread.Sleep(200);
                    if(!monitor.Latest.Fresh)throw new InvalidOperationException("Live quota read failed");
                    CompanionFiles.Write("quota-check.json",new{ok=true,remaining5h=monitor.Latest.fiveHour==null?null:monitor.Latest.fiveHour.Remaining,remainingWeek=monitor.Latest.week==null?null:monitor.Latest.week.Remaining,reset5h=monitor.Latest.fiveHour==null?null:monitor.Latest.fiveHour.ResetText(),resetWeek=monitor.Latest.week==null?null:monitor.Latest.week.ResetText()});
                }return;
            }
            if(Array.IndexOf(args,"--preview")>=0){using(var image=CountBadge.Render("143,336",CountBadge.Measure("143,336",new UsageSnapshot(),1.5),1.5))image.Save(Path.Combine(CompanionFiles.Data,"badge-preview.png"));return;}
            var desktop=new System.Text.StringBuilder(256);int required;GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out required);
            if(!String.Equals(desktop.ToString(),"Default",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Companion must run on the interactive Default desktop; current desktop: "+desktop);
            bool created;
            // Share the old counter's singleton to prevent duplicate writers/counts.
            using(var mutex=new Mutex(true,"Local\\WisadelInputCounter-v1",out created))
            using(var stop=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\WisadelCompanion-stop-v1")){
                if(!created)return;
                using(var app=new CompanionContext(stop))Application.Run(app);
                mutex.ReleaseMutex();
            }
        }catch(Exception ex){try{File.WriteAllText(Path.Combine(CompanionFiles.Data,"last-error.log"),ex.ToString());}catch{}}
    }
}
