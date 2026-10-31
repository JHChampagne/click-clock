using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DualClock
{
    internal sealed class FakeClock : IClock
    {
        public long Ticks { get; set; }
        public DateTime UtcNow { get; set; }
        public void Advance(double seconds) { Ticks += (long)(seconds*TimeSpan.TicksPerSecond); UtcNow = UtcNow.AddSeconds(seconds); }
    }
    internal static class Tests
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        private static int passed;
        private static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static void Equal(long actual, long seconds, string name) { Check(actual == seconds*TimeSpan.TicksPerSecond, name); }
        [STAThread]
        private static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); Environment.ExitCode=1; }
        }
        private static void Run(string[] args)
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "DualClockTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var clock = new FakeClock { UtcNow = new DateTime(2026,10,29,12,0,0,DateTimeKind.Utc) };
            var state = new AppState();
            var engine = new ClockEngine(state, clock, TimeZoneInfo.Utc);
            clock.Advance(12); engine.Tick(); Equal(state.StudyTicks, 0, "First launch waits for start");
            engine.Switch(); clock.Advance(10); engine.Switch();
            Equal(state.StudyTicks, 10, "Switch settles study");
            clock.Advance(4); engine.Suspend(); Equal(state.RestTicks, 4, "Suspend settles rest");
            clock.Advance(7); engine.Tick(); Equal(state.RestTicks, 4, "Suspend excludes elapsed time");
            engine.Resume(); engine.Switch(); Check(engine.Running && state.Selected == 0, "Switch after wake keeps timer running");
            clock.Advance(3); engine.Tick(); Equal(state.StudyTicks, 13, "Continue selected side");
            for (int i=0;i<1000;i++) engine.Switch();
            Equal(state.StudyTicks+state.RestTicks, 17, "Rapid switches preserve total");
            engine.Reset(); Check(!state.Started && !engine.Running && state.StudyTicks==0 && state.RestTicks==0, "Reset clears session");
            Equal(state.Days.Sum(d=>d.StudyTicks+d.RestTicks), 17, "Reset preserves history");

            clock = new FakeClock { UtcNow = new DateTime(2026,10,29,23,59,58,DateTimeKind.Utc) };
            state = new AppState(); engine = new ClockEngine(state,clock,TimeZoneInfo.Utc); engine.Switch(); clock.Advance(5); engine.Tick();
            Equal(state.StudyTicks,5,"Midnight keeps session total");
            Equal(state.Days.Single(d=>d.Date=="2026-10-29").StudyTicks,2,"Midnight previous day");
            Equal(state.Days.Single(d=>d.Date=="2026-10-30").StudyTicks,3,"Midnight next day");
            clock.Advance(2*86400); engine.Tick(); Check(state.Days.Count==4,"Multi-day splitting");
            Equal(state.Days.Sum(d=>d.StudyTicks),172805,"Multi-day sum exact");
            engine.Suspend(); long before = state.StudyTicks;
            clock.UtcNow = clock.UtcNow.AddHours(8); // Real unbiased time excludes sleep.
            clock.Advance(4); engine.Tick(); Check(state.StudyTicks==before && !engine.Running,"Sleep excludes elapsed before resume event");
            engine.Resume(); clock.Advance(2); engine.Tick(); Check(state.StudyTicks==before+2*TimeSpan.TicksPerSecond,"Wake automatically resumes original side");
            clock.UtcNow = clock.UtcNow.AddHours(-3); clock.Advance(2); engine.Tick();
            Check(state.StudyTicks==before+4*TimeSpan.TicksPerSecond,"Wall-clock rollback never decreases total");
            clock.UtcNow = clock.UtcNow.AddDays(3); clock.Advance(3); engine.Tick();
            Check(state.StudyTicks==before+7*TimeSpan.TicksPerSecond,"Wall-clock forward never invents elapsed time");
            Check(ClockEngine.Format(TimeSpan.FromHours(125).Ticks+TimeSpan.FromSeconds(3).Ticks)=="125:00:03","Hours beyond 99");
            Check(ClockEngine.Format(TimeSpan.FromMilliseconds(1999).Ticks)=="00:00:01","Display floors fractional seconds");

            TimeZoneInfo china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            clock = new FakeClock { UtcNow = new DateTime(2026,10,29,15,59,59,DateTimeKind.Utc) };
            state = new AppState(); engine = new ClockEngine(state,clock,china); engine.Switch(); clock.Advance(3); engine.Tick();
            Equal(state.Days.Single(d=>d.Date=="2026-10-29").StudyTicks,1,"Local midnight UTC+8 previous day");
            Equal(state.Days.Single(d=>d.Date=="2026-10-30").StudyTicks,2,"Local midnight UTC+8 next day");
            // DST transition includes the repeated hour without inventing or dropping time.
            TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            clock = new FakeClock { UtcNow = new DateTime(2026,11,1,3,59,59,DateTimeKind.Utc) };
            var dst = new AppState(); var dstEngine = new ClockEngine(dst,clock,eastern); dstEngine.Switch(); clock.Advance(26*3600); dstEngine.Tick();
            Equal(dst.Days.Single(d=>d.Date=="2026-11-01").StudyTicks,25*3600,"DST 25-hour day splitting");
            Equal(dst.Days.Sum(d=>d.StudyTicks),26*3600,"DST elapsed conservation");

            var store = new StateStore(Path.Combine(root,"persistence")); store.Save(state);
            AppState loaded = store.Load(); Check(loaded.Started && loaded.StudyTicks==state.StudyTicks && loaded.Days.Count==2,"Save/load round trip");
            var resumed = new ClockEngine(loaded,clock,china); Check(resumed.Running,"Restart automatically restores running state");
            store.Save(state); File.WriteAllText(store.FilePath,"broken");
            loaded = store.Load(); Check(store.RecoveryMessage!=null && loaded.StudyTicks==state.StudyTicks,"Corrupt primary recovers backup");
            store.Save(loaded); File.WriteAllText(store.FilePath,"broken again");
            loaded = store.Load(); Check(loaded.StudyTicks==state.StudyTicks,"Recovery preserves valid backup");
            string csv = Path.Combine(root,"history.csv"); StateStore.ExportCsv(csv,loaded.Days);
            byte[] bytes = File.ReadAllBytes(csv); Check(bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf,"CSV UTF-8 BOM");
            string text = File.ReadAllText(csv,Encoding.UTF8); Check(text.Contains("日期,学习/工作时长") && text.Contains("2026-10-30,00:00:02"),"CSV content");
            string[] lines = File.ReadAllLines(csv); Check(lines[1].StartsWith("2026-10-30,"),"CSV reverse chronological");

            LifecycleTests(root);
            HistoryFeatureTests(root);
            GoalTests(root);
            ResponsiveLayoutTests();
            if (args.Contains("--layout")) LayoutTests(root);
            Console.WriteLine("ALL PASSED: " + passed);
        }

        private static void ResponsiveLayoutTests()
        {
            bool contained=true, separate=true, orientation=true;
            for(int width=400;width<=2000;width+=37)
            for(int height=220;height<=2000;height+=43)
            {
                var l=ClockLayout.Calculate(width,height);
                var bounds=new RectangleF(0,34,width,height-34);
                var controls=new[] {l.Clock,l.StudyLabel,l.Study,l.StudyStatus,l.RestLabel,l.Rest,l.RestStatus,l.Main,l.Reset,l.Goal,l.History,l.Footer};
                contained &= controls.All(r=>r.Width>0 && r.Height>0 && bounds.Contains(r));
                if(!controls.All(a=>controls.All(b=>a==b || !a.IntersectsWith(b))))
                {
                    if(separate) Console.WriteLine("Overlap at " + width + "x" + height + ": " + string.Join(";",controls.Select((a,i)=>i+":"+a)));
                    separate=false;
                }
                orientation &= l.Portrait==(width<height);
            }
            Check(contained,"Responsive layout sweep keeps content in window");
            Check(separate,"Responsive layout sweep has no content overlaps");
            Check(orientation,"Responsive layout switches at width-height boundary");
            var small=ClockLayout.Calculate(480,250); var wide=ClockLayout.Calculate(1920,1080);
            Check(wide.Study.Height>small.Study.Height*3 && wide.Main.Width>small.Main.Width,
                "Wide fullscreen scales timers and actions beyond compact window");
            Check(wide.Reset.Left>1920*.25f && wide.History.Right<1920*.75f,
                "Wide fullscreen groups actions near the center");
            Check(Math.Abs(small.Clock.Width/small.Main.Width-.85f)<.001f && small.Clock.Y>=34,
                "Larger wall clock sits below toolbar in proportion to switch button");
            var portrait=ClockLayout.Calculate(400,720);
            Check(portrait.Study.Bottom<portrait.Rest.Top && portrait.Main.Bottom<portrait.Reset.Top &&
                portrait.DividerStart.Y==portrait.DividerEnd.Y,"Portrait layout stacks timers and secondary actions");
        }

        private static void GoalTests(string root)
        {
            var clock = new FakeClock {UtcNow=new DateTime(2026,10,30,23,59,30,DateTimeKind.Utc)};
            var state = new AppState(); var engine = new ClockEngine(state,clock,TimeZoneInfo.Utc);
            var store = new StateStore(Path.Combine(root,"goals"));
            Check(!engine.GoalReached && state.GoalTicks==0,"Old and new states default to no goal");
            engine.SetGoal(TimeSpan.TicksPerMinute,store.Save);
            Check(store.Load().GoalTicks==TimeSpan.TicksPerMinute && !state.Started,"Goal saves immediately without starting timer");
            engine.Switch(); clock.Advance(59); engine.Tick(); Check(!engine.GoalReached,"Goal not reached before exact boundary");
            clock.Advance(1); engine.Tick(); Check(engine.GoalReached && state.Days.Count==2,"Goal reached at boundary across midnight");
            engine.Switch(); clock.Advance(120); engine.Tick(); Check(engine.GoalReached && state.StudyTicks==TimeSpan.TicksPerMinute,"Rest preserves achieved goal without learning progress");
            engine.DeleteDay(engine.Today,store.Save); Check(engine.GoalReached,"Deleting daily history leaves goal intact");
            store.Save(state); var loaded=store.Load();
            Check(new ClockEngine(loaded,clock,TimeZoneInfo.Utc).GoalReached,"Restart restores goal celebration");
            engine.SetGoal(TimeSpan.TicksPerMinute*2,store.Save); Check(!engine.GoalReached,"Raising goal stops celebration");
            engine.SetGoal(TimeSpan.TicksPerMinute,store.Save); Check(engine.GoalReached,"Setting already reached goal celebrates immediately");
            long count=state.Days.Sum(d=>d.SwitchCount);
            engine.Suspend(); clock.Advance(3600); engine.Resume();
            Check(engine.GoalReached && count==state.Days.Sum(d=>d.SwitchCount),"Sleep recovery preserves goal without extra switches");
            engine.Reset(); Check(!engine.GoalReached && state.GoalTicks==TimeSpan.TicksPerMinute,"Reset clears progress but retains goal");
            engine.Switch(); clock.Advance(60); engine.Tick(); Check(engine.GoalReached,"Retained goal can be reached after reset");
            engine.SetGoal(0,store.Save); Check(!engine.GoalReached && store.Load().GoalTicks==0,"Clear goal disables and saves celebration");
            engine.SetGoal(ClockEngine.MaximumGoalTicks,store.Save); Check(store.Load().GoalTicks==TimeSpan.FromMinutes(59999).Ticks,"Maximum goal supported");
            foreach (long invalid in new[] {-1L,1L,ClockEngine.MaximumGoalTicks+TimeSpan.TicksPerMinute})
            {
                bool rejected=false; try {engine.SetGoal(invalid,store.Save);} catch(ArgumentOutOfRangeException) {rejected=true;}
                Check(rejected && state.GoalTicks==ClockEngine.MaximumGoalTicks,"Invalid goal rejected without modification " + invalid);
            }
            using (var locked=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                bool failed=false; try {engine.SetGoal(TimeSpan.TicksPerMinute,store.Save);} catch(IOException) {failed=true;}
                Check(failed && state.GoalTicks==ClockEngine.MaximumGoalTicks,"Failed goal save restores previous target");
            }
            Check(store.Load().GoalTicks==ClockEngine.MaximumGoalTicks,"Failed goal save preserves disk state");
            Check(ClockEngine.FormatTime(new DateTime(2026,10,30,0,0,0))=="00:00" &&
                ClockEngine.FormatTime(new DateTime(2026,10,30,23,59,59))=="23:59","Clock uses fixed 24-hour format at midnight");
            Check(ClockEngine.FormatTime(new DateTime(2026,10,30,13,5,0))=="13:05","Clock preserves minute zero padding");
            string xml=File.ReadAllText(store.FilePath);
            File.WriteAllText(store.FilePath,xml.Replace("<GoalTicks>"+ClockEngine.MaximumGoalTicks+"</GoalTicks>","<GoalTicks>-1</GoalTicks>"));
            var recovery=new StateStore(Path.Combine(root,"goals"));
            Check(recovery.Load().GoalTicks>=0 && recovery.RecoveryMessage!=null,"Invalid saved goal triggers backup recovery");
        }

        private static void LifecycleTests(string root)
        {
            foreach (int side in new[] {0,1})
            {
                var clock = new FakeClock {UtcNow=new DateTime(2026,10,29,23,59,50,DateTimeKind.Utc)};
                var state = new AppState();
                var engine = new ClockEngine(state,clock,TimeZoneInfo.Utc);
                engine.Resume(); clock.Advance(1); engine.Tick();
                Check(!engine.Running && !state.Started && state.Days.Count==0,"Wake never starts unused timer " + side);
                engine.Switch(); if (side==1) engine.Switch();
                clock.Advance(2); engine.Tick();
                var store = new StateStore(Path.Combine(root,"restart-side-"+side)); store.Save(state);
                clock.Advance(86400); // Closed time spans midnight and must not enter any daily record.
                var loaded=store.Load(); engine=new ClockEngine(loaded,clock,TimeZoneInfo.Utc);
                Check(engine.Running && loaded.Selected==side && loaded.StudyTicks+loaded.RestTicks==2*TimeSpan.TicksPerSecond,
                    "Restart retains side and excludes closed time " + side);
                clock.Advance(3); engine.Tick();
                Equal(loaded.Days.Single(d=>d.Date=="2026-10-30").StudyTicks+loaded.Days.Single(d=>d.Date=="2026-10-30").RestTicks,
                    3,"Cross-day restart records only new elapsed time " + side);
                clock.Advance(1); engine.Resume(); clock.Advance(2); engine.Resume(); engine.Tick();
                Equal(loaded.StudyTicks+loaded.RestTicks,8,"Duplicate resume preserves running elapsed time " + side);
                clock.Advance(2); engine.Suspend(); engine.Suspend();
                clock.UtcNow=clock.UtcNow.AddHours(8); // Unbiased clock does not advance while asleep.
                clock.Advance(4); engine.Resume(); clock.Advance(2); engine.Tick();
                Equal(loaded.StudyTicks+loaded.RestTicks,12,"Wake excludes sleep and resumes same side " + side);
                Check(engine.Running && loaded.Selected==side && loaded.Days.Sum(d=>d.SwitchCount)==side,
                    "Automatic restart and wake never count as switches " + side);
                engine.Switch();
                Check(engine.Running && loaded.Selected==1-side && loaded.Days.Sum(d=>d.SwitchCount)==side+1,
                    "First switch after wake counts exactly once " + side);
                engine.Reset(); engine.Suspend(); clock.Advance(2); engine.Resume(); engine.Tick();
                Check(!engine.Running && !loaded.Started && loaded.StudyTicks+loaded.RestTicks==0,
                    "Wake after reset stays ready to start " + side);
                store.Save(loaded); var reset=store.Load();
                Check(!new ClockEngine(reset,clock,TimeZoneInfo.Utc).Running,"Restart after reset stays ready to start " + side);
            }
        }

        private static void HistoryFeatureTests(string root)
        {
            var clock = new FakeClock { UtcNow = new DateTime(2026,10,30,10,0,0,DateTimeKind.Utc) };
            var state = new AppState();
            var engine = new ClockEngine(state,clock,TimeZoneInfo.Utc);
            engine.Switch(); Check(state.Days.Sum(d=>d.SwitchCount)==0,"First start does not count a switch");
            engine.Switch(); Check(state.Days.Single().SwitchCount==1,"Instant switch creates zero-duration history with count");
            engine.Switch();
            Check(engine.Running && state.Days.Single().SwitchCount==2,"Running side change counts exactly once");
            engine.Suspend(); engine.Resume(); engine.Reset();
            Check(state.Days.Single().SwitchCount==2,"Automatic resume suspend reset do not increment count");
            for (int i=0;i<3;i++)
            {
                engine.Switch(); clock.Advance(2); engine.Switch(); clock.Advance(1); engine.Reset();
            }
            Check(state.Days.Count==1 && state.Days.Single().SwitchCount==5,"Repeated same-day resets preserve one row and total count");
            Equal(state.Days.Single().StudyTicks+state.Days.Single().RestTicks,9,"Repeated same-day resets preserve history duration");

            var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            clock = new FakeClock { UtcNow = new DateTime(2026,10,29,15,59,59,DateTimeKind.Utc) };
            state = new AppState(); engine = new ClockEngine(state,clock,china); engine.Switch();
            clock.Advance(.5); engine.Switch(); clock.Advance(1); engine.Switch();
            Check(state.Days.Single(d=>d.Date=="2026-10-29").SwitchCount==1 &&
                state.Days.Single(d=>d.Date=="2026-10-30").SwitchCount==1,"Switch count belongs to actual local date across midnight");

            var store = new StateStore(Path.Combine(root,"delete")); store.Save(state);
            long studyBefore = state.StudyTicks, restBefore = state.RestTicks; int selected = state.Selected;
            bool committed = false;
            Check(engine.DeleteDay("2026-10-29",s=>{ store.Save(s); committed=true; }) && committed,"Delete past record commits immediately");
            Check(store.Load().Days.All(d=>d.Date!="2026-10-29") && state.Days.Count==1,"Deleted past record absent in memory and on disk");
            Check(engine.Running && state.Selected==selected && state.StudyTicks==studyBefore && state.RestTicks==restBefore,
                "History deletion leaves main totals and running side intact");
            Check(!engine.DeleteDay("2000-01-01",s=>{throw new Exception("must not commit");}),"Deleting missing date is a no-op");
            clock.Advance(2);
            Check(engine.DeleteDay(engine.Today,store.Save),"Delete active today succeeds after settling time");
            Check(state.Days.Count==0 && store.Load().Days.Count==0 && engine.Running,"Active-day removal persists without recreating during commit");
            Check(state.StudyTicks==studyBefore+2*TimeSpan.TicksPerSecond,"Delete settles main total at confirmation");
            clock.Advance(3); engine.Tick();
            Check(state.Days.Count==1 && state.Days.Single().SwitchCount==0 && !state.Days.Single().HasUnknownSwitchCount,
                "Subsequent timing recreates today as a new complete record");
            Equal(state.Days.Single().StudyTicks,3,"Recreated today excludes deleted duration");
            engine.Suspend(); Check(engine.DeleteDay(engine.Today,store.Save),"Delete suspended today succeeds");
            clock.Advance(4); engine.Tick(); Check(state.Days.Count==0,"Suspended timer does not immediately recreate deleted today");
            engine.Resume(); engine.Switch(); Check(state.Days.Single().SwitchCount==1 && engine.Running,"Switch after wake recreates deleted today with count one");

            state.Days.Insert(0,new DayRecord {Date="2026-10-28",StudyTicks=17,SwitchCount=7,HasUnknownSwitchCount=true});
            store.Save(state);
            DayRecord original = state.Days[0]; bool threw = false;
            using (var locked = new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                try { engine.DeleteDay("2026-10-28",store.Save); }
                catch (IOException) { threw=true; }
            }
            Check(threw && object.ReferenceEquals(state.Days[0],original) && original.SwitchCount==7 && original.HasUnknownSwitchCount,
                "Real file-save failure restores exact record at original position");
            Check(store.Load().Days.Any(d=>d.Date=="2026-10-28"),"Failed deletion leaves persisted history intact");
            store.Save(state); Check(store.Load().Days.Count==2,"Rollback remains saveable after file lock released");

            string legacyDirectory = Path.Combine(root,"legacy"); Directory.CreateDirectory(legacyDirectory);
            string legacyXml = "<?xml version=\"1.0\"?><AppState><Version>1</Version><StudyTicks>90000000</StudyTicks>" +
                "<RestTicks>30000000</RestTicks><Selected>1</Selected><Started>true</Started><Pinned>true</Pinned>" +
                "<HasBounds>true</HasBounds><X>1920</X><Y>-1089</Y><Width>480</Width><Height>250</Height><WindowDpi>96</WindowDpi>" +
                "<Days><DayRecord><Date>2026-10-30</Date><StudyTicks>90000000</StudyTicks><RestTicks>30000000</RestTicks></DayRecord></Days></AppState>";
            File.WriteAllText(Path.Combine(legacyDirectory,"state.xml"),legacyXml,new UTF8Encoding(false));
            byte[] legacyBytes=File.ReadAllBytes(Path.Combine(legacyDirectory,"state.xml"));
            var legacyStore=new StateStore(legacyDirectory); var migrated=legacyStore.Load();
            Check(migrated.Version==2 && migrated.Days.Single().HasUnknownSwitchCount && migrated.Days.Single().SwitchCount==0,
                "V1 migration marks historical switch count unknown");
            Check(migrated.StudyTicks==90000000 && migrated.RestTicks==30000000 && migrated.Pinned && migrated.X==1920 && migrated.Y==-1089,
                "V1 migration preserves durations and window settings");
            Check(ClockEngine.FormatSwitchCount(migrated.Days.Single())=="0次","Legacy missing switches displays zero with unit");
            Check(File.ReadAllBytes(legacyStore.FilePath).SequenceEqual(legacyBytes),"Loading migration does not rewrite original before save");
            var migrationClock=new FakeClock {UtcNow=new DateTime(2026,10,30,12,0,0,DateTimeKind.Utc)};
            var migratedEngine=new ClockEngine(migrated,migrationClock,TimeZoneInfo.Utc); migratedEngine.Switch();
            Check(ClockEngine.FormatSwitchCount(migrated.Days.Single())=="1次","New switches on legacy history display direct count with unit");
            legacyStore.Save(migrated);
            string preserved=Path.Combine(legacyDirectory,"state.pre-v2.xml");
            Check(File.ReadAllBytes(preserved).SequenceEqual(legacyBytes),"Upgrade creates byte-identical independent V1 backup");
            legacyStore.Save(migrated); legacyStore.Save(migrated);
            Check(File.ReadAllBytes(preserved).SequenceEqual(legacyBytes),"Routine saves never overwrite upgrade backup");
            var reopened=new StateStore(legacyDirectory).Load();
            Check(reopened.Version==2 && reopened.Days.Single().SwitchCount==1 && reopened.Days.Single().HasUnknownSwitchCount,
                "V2 round-trip preserves count and unknown marker");
            var upgradedEngine=new ClockEngine(reopened,clock,TimeZoneInfo.Utc);
            Check(upgradedEngine.Running && reopened.Selected==0,"Upgrade restart automatically resumes preserved side");
            string csv=Path.Combine(root,"feature-history.csv");
            StateStore.ExportCsv(csv,new[] {migrated.Days.Single(),new DayRecord {Date="2026-10-29",HasUnknownSwitchCount=true},
                new DayRecord {Date="2026-10-28",SwitchCount=4}});
            string[] exported=File.ReadAllLines(csv,Encoding.UTF8);
            Check(exported[0].EndsWith(",切换次数") && exported[1].EndsWith(",1次") && exported[2].EndsWith(",0次") &&
                exported[3].EndsWith(",4次"),"CSV exports all switch counts directly with unit including legacy zero");
            File.WriteAllText(legacyStore.FilePath,"corrupt");
            var recovered=new StateStore(legacyDirectory).Load();
            Check(recovered.Days.Single().SwitchCount==1 && recovered.Days.Single().HasUnknownSwitchCount,"V2 backup recovery preserves counter semantics");
            var fallbackDirectory=Path.Combine(root,"legacy-backup"); Directory.CreateDirectory(fallbackDirectory);
            File.WriteAllText(Path.Combine(fallbackDirectory,"state.xml"),"corrupt");
            File.WriteAllText(Path.Combine(fallbackDirectory,"state.xml.bak"),legacyXml);
            var fallbackStore=new StateStore(fallbackDirectory); var fallback=fallbackStore.Load(); fallbackStore.Save(fallback);
            Check(fallback.Version==2 && File.ReadAllText(Path.Combine(fallbackDirectory,"state.pre-v2.xml"))==legacyXml,
                "V1 recovery from backup also preserves pre-upgrade original");
        }

        private static void LayoutTests(string root)
        {
            try { Native.SetProcessDpiAwareness(2); } catch (EntryPointNotFoundException) {}
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            foreach (int dpi in new[] {96,120,144})
            foreach (Size size in new[] {new Size(480,250),new Size(400,220),new Size(400,720),
                new Size(400,401),new Size(400,400),new Size(401,400),new Size(1920,1080),new Size(1080,1920)})
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"layout-"+size.Width+"x"+size.Height+"-"+dpi))))
            {
                Console.WriteLine("Showing layout test window...");
                form.Show();
                Application.DoEvents();
                Console.WriteLine("Layout test window shown.");
                typeof(ClockForm).GetMethod("SetDpi",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{dpi});
                // Allow offscreen render fixtures taller than the attached monitor's tracking limit.
                form.MaximumSize=new Size(10000,10000);
                form.Size = new Size(size.Width*dpi/96,size.Height*dpi/96);
                if(form.ClientSize!=new Size(size.Width*dpi/96,size.Height*dpi/96))
                    SetWindowPos(form.Handle,IntPtr.Zero,form.Left,form.Top,size.Width*dpi/96,size.Height*dpi/96,0x14);
                Check(form.ClientSize==new Size(size.Width*dpi/96,size.Height*dpi/96),"Requested render size reached " + size + " DPI " + dpi);
                typeof(ClockForm).GetMethod("LayoutContents",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                var buttons = form.Controls.OfType<SoftButton>().ToList();
                Check(buttons.Count==8 && !buttons.Any(b=>b.Text=="暂停" || b.Text=="继续"),
                    "No manual pause control " + size + " DPI " + dpi);
                Check(form.Controls.Cast<Control>().All(c=>form.ClientRectangle.Contains(c.Bounds)),"Bounds " + size + " DPI " + dpi);
                var all=form.Controls.Cast<Control>().ToList();
                Check(all.All(a=>all.All(b=>a==b || !a.Bounds.IntersectsWith(b.Bounds))),"All controls remain separate after pixel rounding " + size + " DPI " + dpi);
                Check(buttons.All(a=>buttons.All(b=>a==b || !a.Bounds.IntersectsWith(b.Bounds))),"Buttons do not overlap " + size + " DPI " + dpi);
                var clockDisplay=form.Controls.OfType<SegmentDisplay>().Single(c=>c.AccessibleName.StartsWith("当前时间"));
                Check(!buttons.Any(b=>b.Bounds.IntersectsWith(clockDisplay.Bounds)) &&
                    !form.Controls.OfType<Label>().Any(l=>l.Text.Contains("空格切换")),"Clock fits and permanent footer removed " + size + " DPI " + dpi);
                var brand=form.Controls.OfType<Label>().Single(l=>l.Text==Branding.Name);
                Check(form.Text==Branding.Name && TextRenderer.MeasureText(brand.Text,brand.Font).Width<=brand.ClientSize.Width,
                    "Click Clock branding fits " + size + " DPI " + dpi);
                Check(form.Controls.OfType<Label>().All(label=>string.IsNullOrEmpty(label.Text) ||
                    TextRenderer.MeasureText(label.Text,label.Font,Size.Empty,TextFormatFlags.NoPadding).Height<=label.Height+1),
                    "Labels remain readable " + size + " DPI " + dpi);
                using (var preview=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));
                    preview.Save(Path.Combine(root,"layout-"+size.Width+"x"+size.Height+"-"+dpi+".png"));
                }
                SetLongSessionPreview(form);
                var status=form.Controls.OfType<Label>().Single(l=>l.Text.StartsWith("目标 "));
                Check(TextRenderer.MeasureText(status.Text,status.Font,Size.Empty,TextFormatFlags.NoPadding).Width<=status.Width,
                    "Maximum target fits status row " + size + " DPI " + dpi);
                using (var bitmap = new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(root,"layout-long-"+size.Width+"x"+size.Height+"-"+dpi+".png"));
                }
            }
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"native-integration"))))
            {
                form.Show(); Application.DoEvents();
                Rectangle normal = form.Bounds;
                Screen portrait = Screen.AllScreens.FirstOrDefault(s=>!s.Primary && s.Bounds.Height>s.Bounds.Width);
                if (portrait!=null) Check(portrait.WorkingArea.Contains(normal),"First placement on portrait secondary screen");
                var pin = form.Controls.OfType<SoftButton>().Single(b=>b.Glyph=="pin");
                pin.PerformClick(); Check(form.TopMost,"Native topmost enabled");
                pin.PerformClick(); Check(!form.TopMost,"Native topmost disabled");
                var full = typeof(ClockForm).GetMethod("ToggleFullscreen",BindingFlags.Instance|BindingFlags.NonPublic);
                full.Invoke(form,null); Check(form.Bounds==Screen.FromRectangle(normal).Bounds,"Native fullscreen bounds");
                full.Invoke(form,null); Check(form.Bounds==normal,"Native fullscreen restores original bounds");
                form.Location = new Point(-30000,-30000);
                typeof(ClockForm).GetMethod("EnsureVisible",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                Check(Screen.FromRectangle(form.Bounds).WorkingArea.Contains(form.Bounds),"Offscreen window recovers to visible work area");
                var liveEngine = (ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                liveEngine.Switch();
                typeof(ClockForm).GetMethod("PowerChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,
                    new object[]{null,new PowerModeChangedEventArgs(PowerModes.Suspend)});
                Check(!liveEngine.Running,"Power suspend event pauses running UI engine");
                var power=typeof(ClockForm).GetMethod("PowerChanged",BindingFlags.Instance|BindingFlags.NonPublic);
                power.Invoke(form,new object[]{null,new PowerModeChangedEventArgs(PowerModes.Resume)});
                power.Invoke(form,new object[]{null,new PowerModeChangedEventArgs(PowerModes.Resume)});
                Check(liveEngine.Running && liveEngine.State.Selected==0 && liveEngine.State.Days.Sum(d=>d.SwitchCount)==0,
                    "Power resume and duplicate notification restore original side without switches");
                form.Close();
            }
            HistoryLayoutTests(root);
            GoalLayoutTests(root);
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"counter-ui"))))
            {
                form.Show(); Application.DoEvents();
                var primary = form.Controls.OfType<SoftButton>().Single(b=>b.Primary);
                primary.PerformClick(); primary.PerformClick();
                var keyMethod=typeof(ClockForm).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);
                keyMethod.Invoke(form,new object[]{Message.Create(form.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Space});
                keyMethod.Invoke(form,new object[]{Message.Create(form.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Space});
                var saved=new StateStore(Path.Combine(root,"counter-ui")).Load();
                Check(saved.Days.Sum(d=>d.SwitchCount)==3,"Button and Space paths immediately persist each switch");
                var liveEngine=(ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(liveEngine.Running && saved.Selected==1,"Space switch keeps selected side running");
                form.Close();
            }
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"counter-ui"))))
            {
                form.Show(); Application.DoEvents();
                var liveEngine=(ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(liveEngine.Running && liveEngine.State.Selected==1 && liveEngine.State.Days.Sum(d=>d.SwitchCount)==3,
                    "Reopened UI automatically resumes saved rest side without extra switch");
                form.Close();
            }
        }

        private static void SetLongSessionPreview(ClockForm form)
        {
            var state=((ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form)).State;
            state.StudyTicks=TimeSpan.FromHours(125).Ticks+TimeSpan.FromSeconds(3).Ticks;
            state.GoalTicks=ClockEngine.MaximumGoalTicks;
            typeof(ClockForm).GetMethod("UpdateDisplay",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
        }

        private static void GoalLayoutTests(string root)
        {
            foreach (int dpi in new[] {96,120,144})
            using (var dialog=new GoalForm(0,TimeSpan.FromMinutes(12).Ticks,dpi))
            {
                dialog.Show(); Application.DoEvents();
                Check(dialog.Controls.Cast<Control>().All(c=>dialog.ClientRectangle.Contains(c.Bounds)),"Goal dialog bounds DPI " + dpi);
                var inputs=dialog.Controls.OfType<NumericUpDown>().ToList();
                var hours=inputs.Single(c=>c.AccessibleName=="目标小时");
                var minutes=inputs.Single(c=>c.AccessibleName=="目标分钟");
                Check(hours.Value==1 && minutes.Value==0,"Goal dialog defaults to one hour DPI " + dpi);
                hours.Value=0;
                dialog.Controls.OfType<SoftButton>().Single(b=>b.Text=="保存").PerformClick();
                Check(dialog.DialogResult!=DialogResult.OK,"Zero target stays open for correction DPI " + dpi);
                minutes.Value=1;
                using (var bitmap=new Bitmap(dialog.Width,dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(root,"goal-dialog-"+dpi+".png"));
                }
                dialog.Controls.OfType<SoftButton>().Single(b=>b.Text=="保存").PerformClick();
                Check(dialog.GoalTicks==TimeSpan.TicksPerMinute && dialog.DialogResult==DialogResult.OK,"Goal dialog accepts minimum DPI " + dpi);
                dialog.Close();
            }
            using (var dialog=new GoalForm(TimeSpan.TicksPerHour,0,96))
            {
                dialog.Show(); dialog.Controls.OfType<SoftButton>().Single(b=>b.Text=="取消").PerformClick();
                Check(dialog.DialogResult==DialogResult.Cancel,"Goal cancel does not request save"); dialog.Close();
            }
            using (var dialog=new GoalForm(TimeSpan.TicksPerHour,0,96))
            {
                dialog.Show(); dialog.Controls.OfType<SoftButton>().Single(b=>b.Text=="清除目标").PerformClick();
                Check(dialog.DialogResult==DialogResult.OK && dialog.GoalTicks==0,"Goal clear explicitly disables target"); dialog.Close();
            }
            var store=new StateStore(Path.Combine(root,"fireworks-ui"));
            store.Save(new AppState {Started=true,Selected=1,StudyTicks=TimeSpan.TicksPerMinute,GoalTicks=TimeSpan.TicksPerMinute});
            using (var form=new ClockForm(store))
            {
                form.Show(); Application.DoEvents();
                var animation=(System.Windows.Forms.Timer)typeof(ClockForm).GetField("animation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                var elapsed=(System.Diagnostics.Stopwatch)typeof(ClockForm).GetField("animationTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(animation.Enabled && elapsed.IsRunning,"Achieved goal loops when reopened on rest side");
                using(var background=new Bitmap(form.Width,form.Height))
                using(var graphics=Graphics.FromImage(background))
                {
                    FireworksScene.Draw(graphics,background.Size,2.1);
                    background.Save(Path.Combine(root,"fireworks-scene.png"));
                    var backdrop=typeof(ClockForm).GetField("backdrop",BindingFlags.Instance|BindingFlags.NonPublic);
                    var previous=(Bitmap)backdrop.GetValue(form); if(previous!=null) previous.Dispose();
                    backdrop.SetValue(form,background.Clone());
                    var display=form.Controls.OfType<SegmentDisplay>().First();
                    using(var tile=new Bitmap(display.Width,display.Height))
                    using(var tileGraphics=Graphics.FromImage(tile))
                    {
                        SharedBackground.Draw(display,tileGraphics);
                        Check(tile.GetPixel(0,0)==background.GetPixel(display.Left,display.Top),"Child controls use exact shared background coordinates");
                    }
                    using(var composite=new Bitmap(form.Width,form.Height))
                    {
                        form.DrawToBitmap(composite,new Rectangle(Point.Empty,composite.Size));
                        composite.Save(Path.Combine(root,"goal-achieved.png"));
                    }
                    FireworksScene.Draw(graphics,background.Size,8.2);
                    background.Save(Path.Combine(root,"fireworks-next-cycle.png"));
                }
                var power=typeof(ClockForm).GetMethod("PowerChanged",BindingFlags.Instance|BindingFlags.NonPublic);
                power.Invoke(form,new object[]{null,new PowerModeChangedEventArgs(PowerModes.Suspend)});
                Check(!animation.Enabled && !elapsed.IsRunning,"Suspend stops animation rendering");
                power.Invoke(form,new object[]{null,new PowerModeChangedEventArgs(PowerModes.Resume)});
                Check(animation.Enabled,"Resume restores goal celebration");
                form.WindowState=FormWindowState.Minimized; Application.DoEvents();
                Check(!animation.Enabled,"Minimizing stops animation rendering");
                form.WindowState=FormWindowState.Normal; Application.DoEvents();
                Check(animation.Enabled,"Restoring window resumes animation rendering");
                var engine=(ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                engine.Reset(); typeof(ClockForm).GetMethod("UpdateDisplay",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                Check(!animation.Enabled && !elapsed.IsRunning && engine.State.GoalTicks>0,"Reset stops animation while retaining goal");
                form.Close();
            }
        }

        private static void HistoryLayoutTests(string root)
        {
            foreach (int dpi in new[] {96,120,144})
            foreach (Size size in new[] {new Size(620,440),new Size(440,300)})
            {
                var state=new AppState();
                state.Days.Add(new DayRecord {Date="2026-10-29",StudyTicks=TimeSpan.FromHours(125).Ticks,SwitchCount=5});
                state.Days.Add(new DayRecord {Date="2026-10-28",RestTicks=TimeSpan.FromMinutes(3).Ticks,HasUnknownSwitchCount=true});
                state.Days.Add(new DayRecord {Date="2026-10-27",HasUnknownSwitchCount=true,SwitchCount=2});
                var engine=new ClockEngine(state,new FakeClock {UtcNow=new DateTime(2026,10,30,10,0,0,DateTimeKind.Utc)},TimeZoneInfo.Utc);
                int commits=0;
                using (var form=new HistoryForm(engine,dpi,delegate{},s=>{commits++;}))
                {
                    form.AutoScaleMode=AutoScaleMode.None;
                    form.Size=new Size(size.Width*dpi/96,size.Height*dpi/96);
                    form.Show(); Application.DoEvents();
                    var grid=(DataGridView)typeof(HistoryForm).GetField("grid",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                    var delete=(SoftButton)typeof(HistoryForm).GetField("delete",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                    Check(form.Text==Branding.Name + " · 每日历史" && grid.Columns.Count==5 && grid.Rows[0].Cells[4].Value.ToString()=="5次" &&
                        grid.Rows[1].Cells[4].Value.ToString()=="0次" && grid.Rows[2].Cells[4].Value.ToString()=="2次",
                        "History displays all counter states " + size + " DPI " + dpi);
                    var buttons=form.Controls.OfType<Panel>().SelectMany(p=>p.Controls.OfType<SoftButton>()).ToList();
                    Check(buttons.All(a=>a.Parent.ClientRectangle.Contains(a.Bounds)) &&
                        buttons.All(a=>buttons.All(b=>a==b || !a.Bounds.IntersectsWith(b.Bounds))),"History toolbar fits " + size + " DPI " + dpi);
                    grid.CurrentCell=grid.Rows[1].Cells[0]; grid.Rows[1].Selected=true;
                    var update=typeof(HistoryForm).GetMethod("UpdateRows",BindingFlags.Instance|BindingFlags.NonPublic);
                    update.Invoke(form,null);
                    Check(delete.Enabled && (string)grid.SelectedRows[0].Tag=="2026-10-28","History refresh preserves selection " + size + " DPI " + dpi);
                    state.Days.Add(new DayRecord {Date="2026-10-30",SwitchCount=1}); update.Invoke(form,null);
                    Check((string)grid.SelectedRows[0].Tag=="2026-10-28" && grid.SelectedRows[0].Index==2,
                        "New day row preserves selection by date " + size + " DPI " + dpi);
                    using (var bitmap=new Bitmap(form.Width,form.Height))
                    {
                        form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.Combine(root,"history-"+size.Width+"-"+dpi+".png"));
                    }
                    if (dpi==96 && size.Width==620)
                    {
                        InvokeDeleteWithChoice(form,DialogResult.Cancel);
                        Check(state.Days.Count==4 && commits==0 && (string)grid.SelectedRows[0].Tag=="2026-10-28",
                            "Cancel deletion leaves history selection and persistence untouched");
                        InvokeDeleteWithChoice(form,DialogResult.OK);
                        Check(state.Days.Count==3 && commits==1 && state.Days.All(d=>d.Date!="2026-10-28"),
                            "Confirm deletion commits selected date only");
                        Check(grid.Rows.Count==3 && grid.SelectedRows.Count==0 && !delete.Enabled,
                            "Successful deletion refreshes grid without selecting a different record");
                    }
                    form.Close();
                }
            }
            foreach (int dpi in new[] {96,120,144})
            {
                string description="将删除该日的时长和切换次数。\n主界面累计和计时状态保持不变。\n后续计时或切换会重新生成今天的记录。";
                using (var dialog=(ConfirmForm)Activator.CreateInstance(typeof(ConfirmForm),BindingFlags.Instance|BindingFlags.NonPublic,
                    null,new object[]{"删除 2026-10-30 的历史？",description,dpi,"删除"},null))
                {
                    dialog.Show(); Application.DoEvents();
                    Check(dialog.Controls.Cast<Control>().All(c=>dialog.ClientRectangle.Contains(c.Bounds)),"Delete confirmation fits DPI " + dpi);
                    Check(dialog.AcceptButton==dialog.CancelButton,"Delete confirmation defaults to Cancel DPI " + dpi);
                    using (var bitmap=new Bitmap(dialog.Width,dialog.Height))
                    {
                        dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"confirm-"+dpi+".png"));
                    }
                    dialog.Close();
                }
            }
        }

        private static void InvokeDeleteWithChoice(HistoryForm form,DialogResult choice)
        {
            bool answered=false;
            using (var response=new System.Windows.Forms.Timer {Interval=50})
            {
                response.Tick+=delegate
                {
                    var confirmation=Application.OpenForms.OfType<ConfirmForm>().SingleOrDefault();
                    if (confirmation==null) return;
                    response.Stop(); answered=true; confirmation.DialogResult=choice;
                };
                response.Start();
                typeof(HistoryForm).GetMethod("DeleteSelected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                response.Stop();
            }
            Check(answered,"Deletion confirmation shown before " + choice);
        }
    }
}
