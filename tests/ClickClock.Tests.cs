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
            clock.Advance(12); engine.Tick(); Equal(state.StudyTicks, 0, "Startup paused");
            engine.Switch(); clock.Advance(10); engine.Switch();
            Equal(state.StudyTicks, 10, "Switch settles study");
            clock.Advance(4); engine.TogglePause(); Equal(state.RestTicks, 4, "Pause settles rest");
            clock.Advance(7); engine.Tick(); Equal(state.RestTicks, 4, "Pause excludes elapsed time");
            engine.Switch(); Check(!engine.Running && state.Selected == 0, "Switch while paused stays paused");
            engine.TogglePause(); clock.Advance(3); engine.Tick(); Equal(state.StudyTicks, 13, "Resume selected side");
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
            clock.Advance(4); engine.Tick(); Check(state.StudyTicks==before && !engine.Running,"Sleep and wake stay paused");
            engine.TogglePause(); clock.Advance(2); engine.Tick(); Check(state.StudyTicks==before+2*TimeSpan.TicksPerSecond,"Wake manual resume");
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
            var resumed = new ClockEngine(loaded,clock,china); Check(!resumed.Running,"Restart restores paused");
            store.Save(state); File.WriteAllText(store.FilePath,"broken");
            loaded = store.Load(); Check(store.RecoveryMessage!=null && loaded.StudyTicks==state.StudyTicks,"Corrupt primary recovers backup");
            store.Save(loaded); File.WriteAllText(store.FilePath,"broken again");
            loaded = store.Load(); Check(loaded.StudyTicks==state.StudyTicks,"Recovery preserves valid backup");
            string csv = Path.Combine(root,"history.csv"); StateStore.ExportCsv(csv,loaded.Days);
            byte[] bytes = File.ReadAllBytes(csv); Check(bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf,"CSV UTF-8 BOM");
            string text = File.ReadAllText(csv,Encoding.UTF8); Check(text.Contains("日期,学习/工作时长") && text.Contains("2026-10-30,00:00:02"),"CSV content");
            string[] lines = File.ReadAllLines(csv); Check(lines[1].StartsWith("2026-10-30,"),"CSV reverse chronological");

            HistoryFeatureTests(root);
            if (args.Contains("--layout")) LayoutTests(root);
            Console.WriteLine("ALL PASSED: " + passed);
        }

        private static void HistoryFeatureTests(string root)
        {
            var clock = new FakeClock { UtcNow = new DateTime(2026,10,30,10,0,0,DateTimeKind.Utc) };
            var state = new AppState();
            var engine = new ClockEngine(state,clock,TimeZoneInfo.Utc);
            engine.Switch(); Check(state.Days.Sum(d=>d.SwitchCount)==0,"First start does not count a switch");
            engine.Switch(); Check(state.Days.Single().SwitchCount==1,"Instant switch creates zero-duration history with count");
            engine.TogglePause(); engine.Switch();
            Check(!engine.Running && state.Days.Single().SwitchCount==2,"Paused side change counts exactly once");
            engine.TogglePause(); engine.Suspend(); engine.Reset();
            Check(state.Days.Single().SwitchCount==2,"Pause resume suspend reset do not increment count");
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
            engine.TogglePause(); Check(engine.DeleteDay(engine.Today,store.Save),"Delete paused today succeeds");
            clock.Advance(4); engine.Tick(); Check(state.Days.Count==0,"Paused timer does not immediately recreate deleted today");
            engine.Switch(); Check(state.Days.Single().SwitchCount==1 && !engine.Running,"Paused switch recreates deleted today with count one");

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
            Check(!new ClockEngine(reopened,clock,TimeZoneInfo.Utc).Running,"Upgrade restart remains paused");
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
            foreach (Size size in new[] {new Size(480,250),new Size(400,220)})
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"layout"))))
            {
                Console.WriteLine("Showing layout test window...");
                form.Show();
                Application.DoEvents();
                Console.WriteLine("Layout test window shown.");
                typeof(ClockForm).GetMethod("SetDpi",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{dpi});
                form.Size = new Size(size.Width*dpi/96,size.Height*dpi/96);
                typeof(ClockForm).GetMethod("LayoutContents",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                var buttons = form.Controls.OfType<SoftButton>().ToList();
                Check(form.Controls.Cast<Control>().All(c=>form.ClientRectangle.Contains(c.Bounds)),"Bounds " + size + " DPI " + dpi);
                Check(buttons.All(a=>buttons.All(b=>a==b || !a.Bounds.IntersectsWith(b.Bounds))),"Buttons do not overlap " + size + " DPI " + dpi);
                var brand=form.Controls.OfType<Label>().Single(l=>l.Text==Branding.Name);
                Check(form.Text==Branding.Name && TextRenderer.MeasureText(brand.Text,brand.Font).Width<=brand.ClientSize.Width,
                    "Click Clock branding fits " + size + " DPI " + dpi);
                using (var bitmap = new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    bitmap.Save(Path.Combine(root,"layout-"+size.Width+"-"+dpi+".png"));
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
                form.Close();
            }
            HistoryLayoutTests(root);
            using (var form = new ClockForm(new StateStore(Path.Combine(root,"counter-ui"))))
            {
                form.Show(); Application.DoEvents();
                var primary = form.Controls.OfType<SoftButton>().Single(b=>b.Primary);
                primary.PerformClick(); primary.PerformClick();
                var keyMethod=typeof(ClockForm).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);
                keyMethod.Invoke(form,new object[]{Message.Create(form.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Space});
                form.Controls.OfType<SoftButton>().Single(b=>b.Text=="暂停").PerformClick();
                keyMethod.Invoke(form,new object[]{Message.Create(form.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Space});
                var saved=new StateStore(Path.Combine(root,"counter-ui")).Load();
                Check(saved.Days.Sum(d=>d.SwitchCount)==3,"Button and Space paths immediately persist switches including paused changes");
                var liveEngine=(ClockEngine)typeof(ClockForm).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(!liveEngine.Running,"Space switch preserves paused UI engine");
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
