using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DualClock
{
    public interface IClock
    {
        long Ticks { get; }
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        [DllImport("kernel32.dll")]
        private static extern bool QueryUnbiasedInterruptTime(out ulong time);
        public long Ticks
        {
            get
            {
                ulong ticks;
                if (QueryUnbiasedInterruptTime(out ticks)) return (long)ticks;
                return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency));
            }
        }
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    public sealed class DayRecord
    {
        public string Date { get; set; }
        public long StudyTicks { get; set; }
        public long RestTicks { get; set; }
        public long SwitchCount { get; set; }
        public bool HasUnknownSwitchCount { get; set; }
    }

    public sealed class AppState
    {
        public int Version = 2;
        public long StudyTicks;
        public long RestTicks;
        public int Selected;
        public bool Started;
        public bool Pinned;
        public bool HasBounds;
        public int X, Y, Width, Height;
        public int WindowDpi = 96;
        public List<DayRecord> Days = new List<DayRecord>();
    }

    public sealed class ClockEngine
    {
        private readonly IClock clock;
        private readonly TimeZoneInfo zone;
        private long anchor;
        public readonly AppState State;
        public bool Running { get; private set; }
        public string Today { get { return TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

        public ClockEngine(AppState state, IClock clock, TimeZoneInfo zone)
        {
            State = state;
            this.clock = clock;
            this.zone = zone;
            anchor = clock.Ticks;
            Running = state.Started;
        }

        public void Tick()
        {
            long now = clock.Ticks;
            long delta = Math.Max(0, now - anchor);
            anchor = now;
            if (!Running || delta == 0) return;
            if (State.Selected == 0) State.StudyTicks += delta;
            else State.RestTicks += delta;
            DateTime end = clock.UtcNow;
            DateTime cursor = end.AddTicks(-delta);
            while (cursor < end)
            {
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(cursor, zone);
                DateTime midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
                // Some time zones jump over midnight at the start of daylight saving time.
                while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
                DateTime boundary = TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
                DateTime stop = boundary < end ? boundary : end;
                if (stop <= cursor) stop = end;
                string day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DayRecord record = GetDay(day);
                long part = (stop - cursor).Ticks;
                if (State.Selected == 0) record.StudyTicks += part;
                else record.RestTicks += part;
                cursor = stop;
            }
        }

        public void Switch()
        {
            Tick();
            if (!State.Started)
            {
                State.Started = true;
                State.Selected = 0;
                Running = true;
            }
            else
            {
                State.Selected = 1 - State.Selected;
                GetDay(Today).SwitchCount++;
            }
            anchor = clock.Ticks;
        }

        private DayRecord GetDay(string date)
        {
            DayRecord day = State.Days.FirstOrDefault(d => d.Date == date);
            if (day == null)
            {
                day = new DayRecord { Date = date };
                State.Days.Add(day);
            }
            return day;
        }

        public bool DeleteDay(string date, Action<AppState> persist)
        {
            if (persist == null) throw new ArgumentNullException("persist");
            // Settle time at confirmation. Persist must not tick: that would recreate today's row.
            Tick();
            int index = State.Days.FindIndex(d => d.Date == date);
            if (index < 0) return false;
            DayRecord removed = State.Days[index];
            State.Days.RemoveAt(index);
            try { persist(State); }
            catch
            {
                State.Days.Insert(index, removed);
                throw;
            }
            return true;
        }

        public static string FormatSwitchCount(DayRecord day)
        {
            return day.SwitchCount.ToString(CultureInfo.InvariantCulture) + "次";
        }

        public void Resume()
        {
            // Ignore repeated resume notifications without discarding unsaved elapsed time.
            if (Running || !State.Started) return;
            anchor = clock.Ticks;
            Running = true;
        }

        public void Suspend()
        {
            Tick();
            Running = false;
        }

        public void Reset()
        {
            Tick();
            Running = false;
            State.Started = false;
            State.Selected = 0;
            State.StudyTicks = State.RestTicks = 0;
        }

        public static string Format(long ticks)
        {
            long seconds = Math.Max(0, ticks / TimeSpan.TicksPerSecond);
            return (seconds / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" +
                ((seconds / 60) % 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }

    public sealed class StateStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AppState));
        private readonly string path;
        private byte[] upgradeBackup;
        public string RecoveryMessage { get; private set; }
        public StateStore(string directory)
        {
            path = Path.Combine(directory, "state.xml");
        }
        public string FilePath { get { return path; } }

        private AppState Read(string file)
        {
            bool migrated;
            return Read(file, out migrated);
        }

        private AppState Read(string file, out bool migrated)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(file, settings))
            {
                AppState state = (AppState)Serializer.Deserialize(reader);
                if ((state.Version != 1 && state.Version != 2) || state.StudyTicks < 0 || state.RestTicks < 0 ||
                    state.Selected < 0 || state.Selected > 1 || state.Days == null)
                    throw new InvalidDataException("Invalid state");
                var dates = new HashSet<string>();
                foreach (DayRecord day in state.Days)
                {
                    DateTime parsed;
                    if (day == null || !DateTime.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out parsed) || day.StudyTicks < 0 || day.RestTicks < 0 || day.SwitchCount < 0 || !dates.Add(day.Date))
                        throw new InvalidDataException("Invalid history");
                }
                migrated = state.Version == 1;
                if (migrated)
                {
                    foreach (DayRecord day in state.Days)
                    {
                        day.SwitchCount = 0;
                        day.HasUnknownSwitchCount = true;
                    }
                    state.Version = 2;
                }
                return state;
            }
        }

        public AppState Load()
        {
            if (File.Exists(path))
            {
                try
                {
                    bool migrated;
                    AppState state = Read(path, out migrated);
                    if (migrated) upgradeBackup = File.ReadAllBytes(path);
                    return state;
                }
                catch (Exception e) { if (!Recoverable(e)) throw; }
            }
            if (File.Exists(path + ".bak"))
            {
                try
                {
                    bool migrated;
                    AppState recovered = Read(path + ".bak", out migrated);
                    if (migrated) upgradeBackup = File.ReadAllBytes(path + ".bak");
                    RecoveryMessage = "已从备份恢复记录";
                    return recovered;
                }
                catch (Exception e) { if (!Recoverable(e)) throw; }
            }
            if (File.Exists(path)) RecoveryMessage = "记录无法读取，已新建计时；原文件已保留";
            return new AppState();
        }

        public void Save(AppState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (upgradeBackup != null)
            {
                string backupPath = Path.Combine(Path.GetDirectoryName(path), "state.pre-v2.xml");
                if (File.Exists(backupPath) || File.Exists(backupPath + ".tmp"))
                    backupPath = Path.Combine(Path.GetDirectoryName(path), "state.pre-v2-" +
                        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".xml");
                using (var stream = new FileStream(backupPath + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(upgradeBackup, 0, upgradeBackup.Length);
                    stream.Flush(true);
                }
                File.Move(backupPath + ".tmp", backupPath);
                upgradeBackup = null;
            }
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Serializer.Serialize(stream, state);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                // Preserve a valid backup when replacing a corrupt primary after recovery.
                bool valid = true;
                try { Read(path); } catch (Exception e) { if (!Recoverable(e)) throw; valid = false; }
                if (!valid)
                {
                    string damaged = path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                    File.Copy(path, damaged, true);
                }
                File.Replace(temp, path, valid ? path + ".bak" : null, true);
            }
            else File.Move(temp, path);
        }

        public static bool Recoverable(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException ||
                e is InvalidDataException || e is XmlException || e is ArgumentException;
        }

        public static void ExportCsv(string path, IEnumerable<DayRecord> days)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("日期,学习/工作时长,休息/娱乐时长,合计时长,学习秒数,休息秒数,切换次数");
                foreach (DayRecord d in days.OrderByDescending(d => d.Date))
                    writer.WriteLine(string.Join(",", new[] { d.Date, ClockEngine.Format(d.StudyTicks),
                        ClockEngine.Format(d.RestTicks), ClockEngine.Format(d.StudyTicks + d.RestTicks),
                        (d.StudyTicks / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture),
                        (d.RestTicks / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture),
                        ClockEngine.FormatSwitchCount(d) }));
            }
        }
    }
}
