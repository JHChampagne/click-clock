using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DualClock
{
    internal static class Branding
    {
        internal const string Name = "Click Clock";
    }

    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr handle, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("shcore.dll")] internal static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        internal static readonly uint ActivateMessage = RegisterWindowMessage("DualClock.Activate.v1");
        internal static int MonitorDpi(Screen screen)
        {
            uint x, y;
            try
            {
                if (GetDpiForMonitor(MonitorFromPoint(new Point(screen.Bounds.Left + 10, screen.Bounds.Top + 10), 2),
                    0, out x, out y) == 0) return (int)x;
            }
            catch (EntryPointNotFoundException) {}
            return 96;
        }
        internal static void DarkTitle(IntPtr handle)
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, 4) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, 4);
        }
    }

    internal static class Palette
    {
        internal static readonly Color Background = Color.FromArgb(10, 11, 13);
        internal static readonly Color Foreground = Color.FromArgb(239, 241, 244);
        internal static readonly Color Muted = Color.FromArgb(115, 120, 128);
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SoftButton : Button
    {
        internal bool Primary;
        internal bool Lit;
        internal string Glyph;
        private bool hover, pressed;
        internal float ScaleFactor = 1;
        public SoftButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Background;
            ForeColor = Palette.Foreground;
            Cursor = Cursors.Hand;
            TabStop = true;
            UseVisualStyleBackColor = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            SharedBackground.Draw(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? (pressed ? Color.FromArgb(190, 193, 198) : hover ? Color.White : Palette.Foreground)
                : Lit ? Color.FromArgb(46, 49, 55) : hover ? Color.FromArgb(32, 35, 40) :
                Glyph == null ? Color.FromArgb(21, 23, 27) : Palette.Background;
            Color ink = !Enabled ? Color.FromArgb(63, 67, 74) : Primary ? Palette.Background :
                hover || Lit ? Palette.Foreground : Color.FromArgb(153, 158, 166);
            using (var path = Palette.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 9 * ScaleFactor))
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (Glyph == null)
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else
            {
                float cx = Width / 2f, cy = Height / 2f, k = ScaleFactor;
                using (var p = new Pen(ink, 1.35f * k))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    if (Glyph == "close")
                    {
                        g.DrawLine(p, cx - 4 * k, cy - 4 * k, cx + 4 * k, cy + 4 * k);
                        g.DrawLine(p, cx + 4 * k, cy - 4 * k, cx - 4 * k, cy + 4 * k);
                    }
                    else if (Glyph == "min") g.DrawLine(p, cx - 5 * k, cy + 2 * k, cx + 5 * k, cy + 2 * k);
                    else if (Glyph == "full")
                    {
                        g.DrawLines(p, new[] { new PointF(cx - 5*k, cy - 1*k), new PointF(cx - 5*k, cy - 5*k), new PointF(cx - 1*k, cy - 5*k) });
                        g.DrawLines(p, new[] { new PointF(cx + 5*k, cy + 1*k), new PointF(cx + 5*k, cy + 5*k), new PointF(cx + 1*k, cy + 5*k) });
                    }
                    else if (Glyph == "restore")
                    {
                        g.DrawLines(p, new[] { new PointF(cx - 6*k, cy - 2*k), new PointF(cx - 2*k, cy - 2*k), new PointF(cx - 2*k, cy - 6*k) });
                        g.DrawLines(p, new[] { new PointF(cx + 6*k, cy + 2*k), new PointF(cx + 2*k, cy + 2*k), new PointF(cx + 2*k, cy + 6*k) });
                    }
                    else if (Glyph == "pin")
                    {
                        g.DrawLine(p, cx - 3*k, cy - 5*k, cx + 3*k, cy - 5*k);
                        g.DrawLine(p, cx - 2*k, cy - 5*k, cx - 2*k, cy);
                        g.DrawLine(p, cx + 2*k, cy - 5*k, cx + 2*k, cy);
                        g.DrawLines(p, new[] { new PointF(cx-2*k,cy),new PointF(cx-4*k,cy+2*k),new PointF(cx+4*k,cy+2*k),new PointF(cx+2*k,cy) });
                        g.DrawLine(p, cx, cy + 2*k, cx, cy + 6*k);
                    }
                }
            }
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Primary ? Color.FromArgb(120, 125, 133) : Color.FromArgb(95, 100, 110), ScaleFactor))
                using (var path = Palette.Round(new RectangleF(3, 3, Width - 6, Height - 6), 7 * ScaleFactor))
                    g.DrawPath(pen, path);
            }
        }
    }

    internal sealed class SegmentDisplay : Control
    {
        private string value = "00:00:00";
        private bool active;
        private static readonly int[] Masks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
        public SegmentDisplay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.StaticText;
        }
        internal void SetValue(string text, bool running, string side)
        {
            if (value == text && active == running && AccessibleName != null) return;
            value = text; active = running;
            AccessibleName = side + " " + text;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            SharedBackground.Draw(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float units = value.Sum(c => c == ':' ? 0.25f : 0.66f) - 0.08f;
            float h = Math.Min(Height - 2, (Width - 4) / units);
            float x = (Width - units * h) / 2f, y = (Height - h) / 2f;
            using (var on = new SolidBrush(active ? Color.FromArgb(246, 247, 249) : Color.FromArgb(119, 125, 136)))
            using (var off = new SolidBrush(Color.FromArgb(22, 25, 30)))
            {
                foreach (char c in value)
                {
                    if (c == ':')
                    {
                        float d = h * 0.058f;
                        g.FillEllipse(on, x + h * .065f, y + h * .31f, d, d);
                        g.FillEllipse(on, x + h * .065f, y + h * .67f, d, d);
                        x += h * .25f;
                    }
                    else
                    {
                        DrawDigit(g, x, y, h, Masks[c - '0'], on, off);
                        x += h * .66f;
                    }
                }
            }
        }
        private static void DrawDigit(Graphics g, float x, float y, float h, int mask, Brush on, Brush off)
        {
            // Seven bars plus the punctuation reproduce the segmented instrument-display aesthetic.
            float w = .55f * h, t = .075f * h, half = h / 2f;
            PointF[][] bars = {
                Horizontal(x+t,y,w-2*t,t), Vertical(x+w-t,y+t,t,half-1.5f*t),
                Vertical(x+w-t,y+half+.5f*t,t,half-1.5f*t), Horizontal(x+t,y+h-t,w-2*t,t),
                Vertical(x,y+half+.5f*t,t,half-1.5f*t), Vertical(x,y+t,t,half-1.5f*t),
                Horizontal(x+t,y+half-t/2,w-2*t,t)
            };
            for (int i = 0; i < 7; i++) g.FillPolygon((mask & (1 << i)) != 0 ? on : off, bars[i]);
        }
        private static PointF[] Horizontal(float x, float y, float w, float t)
        {
            return new[] { new PointF(x,y+t/2),new PointF(x+t/2,y),new PointF(x+w-t/2,y),
                new PointF(x+w,y+t/2),new PointF(x+w-t/2,y+t),new PointF(x+t/2,y+t) };
        }
        private static PointF[] Vertical(float x, float y, float t, float h)
        {
            return new[] { new PointF(x+t/2,y),new PointF(x+t,y+t/2),new PointF(x+t,y+h-t/2),
                new PointF(x+t/2,y+h),new PointF(x,y+h-t/2),new PointF(x,y+t/2) };
        }
    }

    internal sealed class ClockForm : Form
    {
        private readonly StateStore store;
        private readonly ClockEngine engine;
        private readonly AppState state;
        private readonly IClock clock = new SystemClock();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly ToolTip tips = new ToolTip();
        private readonly Label brand = new Label(), studyLabel = new Label(), restLabel = new Label();
        private readonly Label studyStatus = new Label(), restStatus = new Label(), footer = new Label();
        private readonly SegmentDisplay study = new SegmentDisplay(), rest = new SegmentDisplay(), wallClock = new SegmentDisplay();
        private readonly SoftButton main = new SoftButton(), reset = new SoftButton(), goal = new SoftButton(), history = new SoftButton();
        private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer();
        private readonly System.Diagnostics.Stopwatch animationTime = new System.Diagnostics.Stopwatch();
        private Bitmap backdrop;
        private bool systemSuspended;
        private readonly SoftButton pin = new SoftButton(), min = new SoftButton(), full = new SoftButton(), close = new SoftButton();
        private bool ready, fullscreen, closing, saveError;
        private int dpi = 96;
        private Rectangle normalBounds;
        private long lastSaveTick;
        private DateTime messageUntil;
        private string message;
        private HistoryForm historyForm;

        internal ClockForm(StateStore store)
        {
            this.store = store;
            state = store.Load();
            engine = new ClockEngine(state, clock, TimeZoneInfo.Local);
            lastSaveTick = clock.Ticks;
            Text = Branding.Name;
            Name = "DualClock.Main";
            AccessibleName = Branding.Name + "，学习与休息计时器";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Palette.Background;
            ForeColor = Palette.Foreground;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            foreach (Label label in new[] { brand, studyLabel, restLabel, studyStatus, restStatus, footer })
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = Palette.Muted;
                label.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(label);
            }
            brand.Text = Branding.Name;
            brand.ForeColor = Color.FromArgb(202,207,215);
            brand.TextAlign = ContentAlignment.MiddleLeft;
            studyLabel.Text = "学习 / 工作";
            restLabel.Text = "休息 / 娱乐";
            Controls.Add(study); Controls.Add(rest); Controls.Add(wallClock);
            wallClock.Enabled = false; // Keep the top bar draggable through the small clock.
            main.Primary = true;
            main.AccessibleName = "开始学习或切换计时";
            reset.Text = "重置"; goal.Text = "目标"; history.Text = "历史";
            foreach (SoftButton button in new[] { main, reset, goal, history, pin, min, full, close }) Controls.Add(button);
            main.TabIndex = 0; reset.TabIndex = 1; goal.TabIndex = 2; history.TabIndex = 3;
            pin.TabIndex = 4; min.TabIndex = 5; full.TabIndex = 6; close.TabIndex = 7;
            SetupIcon(pin, "pin", "置顶", "保持显示在其他普通窗口上方");
            SetupIcon(min, "min", "最小化", "最小化，继续计时");
            SetupIcon(full, "full", "全屏", "全屏 / 还原 · F11");
            SetupIcon(close, "close", "关闭", "保存并关闭");
            tips.SetToolTip(main, "切换学习与休息 · 空格");
            tips.SetToolTip(reset, "清零本次累计，保留每日历史");
            main.Click += delegate { engine.Switch(); Changed(); };
            reset.Click += delegate { ConfirmReset(); };
            goal.Click += delegate { ShowGoal(); };
            history.Click += delegate { ShowHistory(); };
            pin.Click += delegate
            {
                TopMost = !TopMost; state.Pinned = TopMost; pin.Lit = TopMost;
                pin.AccessibleDescription = TopMost ? "置顶已开启" : "置顶已关闭";
                pin.Invalidate(); Save();
            };
            min.Click += delegate { WindowState = FormWindowState.Minimized; };
            full.Click += delegate { ToggleFullscreen(); };
            close.Click += delegate { Close(); };
            timer.Interval = 100;
            animation.Interval = 33;
            animation.Tick += delegate { RenderBackdrop(); };
            timer.Tick += delegate
            {
                engine.Tick();
                UpdateDisplay();
                if (clock.Ticks - lastSaveTick >= 5 * TimeSpan.TicksPerSecond) Save();
            };
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.SessionEnding += SessionEnding;
            Font = new Font("Microsoft YaHei UI", 9f);
            Size = new Size(480, 250);
            Shown += delegate
            {
                PlaceWindow();
                ready = true;
                TopMost = state.Pinned;
                pin.Lit = TopMost;
                LayoutContents();
                if (store.RecoveryMessage != null) Notify(store.RecoveryMessage, 15);
                UpdateDisplay();
                timer.Start();
                ActiveControl = null;
            };
            LocationChanged += delegate { if (ready && !fullscreen && WindowState == FormWindowState.Normal) normalBounds = Bounds; };
            SizeChanged += delegate
            {
                if (ready && !fullscreen && WindowState == FormWindowState.Normal) normalBounds = Bounds;
                LayoutContents();
                UpdateAnimation();
            };
            VisibleChanged += delegate { if (ready) UpdateAnimation(); };
        }

        private void SetupIcon(SoftButton button, string glyph, string name, string tip)
        {
            button.Glyph = glyph; button.AccessibleName = name; tips.SetToolTip(button, tip);
        }

        private float ScaleFactor { get { return dpi / 96f; } }
        private int Px(float value) { return (int)Math.Round(value * ScaleFactor); }
        private void SetDpi(int value)
        {
            dpi = Math.Max(96, value);
            MinimumSize = new Size(Px(400), Px(220));
        }

        private void PlaceWindow()
        {
            Screen target;
            if (state.HasBounds && state.Width > 0 && state.Height > 0)
                target = Screen.FromRectangle(new Rectangle(state.X, state.Y, state.Width, state.Height));
            else target = Screen.AllScreens.FirstOrDefault(s => !s.Primary && s.Bounds.Height > s.Bounds.Width) ?? Screen.PrimaryScreen;
            SetDpi(Native.MonitorDpi(target));
            Rectangle work = target.WorkingArea;
            Size size = state.HasBounds && state.Width > 0 && state.Height > 0
                ? new Size((int)Math.Round(state.Width * (double)dpi / Math.Max(96, state.WindowDpi)),
                    (int)Math.Round(state.Height * (double)dpi / Math.Max(96, state.WindowDpi)))
                : new Size(Px(480), Px(250));
            size.Width = Math.Min(work.Width, Math.Max(MinimumSize.Width, size.Width));
            size.Height = Math.Min(work.Height, Math.Max(MinimumSize.Height, size.Height));
            int x = state.HasBounds ? state.X : work.Right - Px(16) - size.Width;
            int y = state.HasBounds ? state.Y : work.Top + Px(16);
            Bounds = new Rectangle(Math.Max(work.Left, Math.Min(x, work.Right - size.Width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height)), size.Width, size.Height);
            normalBounds = Bounds;
        }

        private void LayoutContents()
        {
            if (main == null || ClientSize.Width == 0) return;
            float s = ScaleFactor, w = ClientSize.Width / s, h = ClientSize.Height / s;
            float bodyScale = Math.Min(2.4f, Math.Min(w / 480f, h / 250f));
            float digitHeight = Math.Min(112, 48 * bodyScale);
            float offsetY = h > 400 ? (h - 250 * bodyScale) / 2 : 0;
            float labelY = h > 400 ? offsetY + 57 * bodyScale : 57 + Math.Max(0, h - 250) * .20f;
            float digitY = labelY + 31 * Math.Min(bodyScale, 1.5f);
            float statusY = digitY + digitHeight + 10;
            float center = w / 2f;
            Place(brand, 19, 8, 135, 24);
            Place(wallClock, center - 31, 9, 62, 22);
            Place(pin, w - 132, 6, 28, 28);
            Place(min, w - 102, 6, 28, 28);
            Place(full, w - 72, 6, 28, 28);
            Place(close, w - 42, 6, 28, 28);
            Place(studyLabel, 18, labelY, center - 36, 20);
            Place(restLabel, center + 18, labelY, center - 36, 20);
            Place(study, 19, digitY, center - 38, digitHeight + 2);
            Place(rest, center + 19, digitY, center - 38, digitHeight + 2);
            Place(studyStatus, 18, statusY, center - 36, 18);
            Place(restStatus, center + 18, statusY, center - 36, 18);
            float bottom = h > 400 ? offsetY + 184 * bodyScale : h - 66;
            float bw = Math.Min(190, w / 3f), bh = Math.Min(52, Math.Max(38, 44 * bodyScale));
            if (h <= 230)
            {
                labelY = 48;
                digitY = 76;
                statusY = digitY + digitHeight + 6;
                Place(studyLabel, 16, labelY, center-32, 18); Place(restLabel, center+16,labelY,center-32,18);
                Place(study, 15,digitY,center-30,digitHeight); Place(rest,center+15,digitY,center-30,digitHeight);
                Place(studyStatus,16,statusY,center-32,16); Place(restStatus,center+16,statusY,center-32,16);
                bottom = h - 61;
            }
            Place(main, center - bw/2, bottom, bw, bh);
            Place(reset, 20, bottom + (bh-32)/2, 44, 32);
            Place(goal, 70, bottom + (bh-32)/2, 44, 32);
            Place(history, w-76, bottom + (bh-32)/2, 56, 32);
            Place(footer, 12, h - 19, w - 24, 14);
            SetFont(brand, 10, FontStyle.Bold);
            SetFont(studyLabel, Math.Min(13, 10*bodyScale), FontStyle.Regular);
            SetFont(restLabel, Math.Min(13, 10*bodyScale), FontStyle.Regular);
            SetFont(studyStatus, 8, FontStyle.Regular); SetFont(restStatus, 8, FontStyle.Regular);
            SetFont(footer, 7, FontStyle.Regular);
            foreach (SoftButton button in new[] { main, reset, goal, history, pin, min, full, close })
            {
                button.ScaleFactor = s;
                SetFont(button, button == main ? 10 : 8.5f, button == main ? FontStyle.Bold : FontStyle.Regular);
            }
            Invalidate();
        }
        private void Place(Control c, float x, float y, float w, float h)
        {
            c.SetBounds(Px(x), Px(y), Px(w), Px(h));
        }
        private void SetFont(Control c, float points, FontStyle style)
        {
            // Pixel fonts make the owner-drawn surface follow the window's monitor DPI exactly.
            float pixels = points * 96f / 72f * ScaleFactor;
            if (Math.Abs(c.Font.Size - pixels) < .01f && c.Font.Unit == GraphicsUnit.Pixel && c.Font.Style == style) return;
            Font old = c.Font;
            bool owned = c.Tag as string == "font-owned";
            c.Font = new Font("Microsoft YaHei UI", pixels, style, GraphicsUnit.Pixel);
            c.Tag = "font-owned";
            if (owned) old.Dispose();
        }

        internal void DrawBackdrop(Graphics graphics, Point origin)
        {
            graphics.Clear(Palette.Background);
            if (backdrop != null && backdrop.Size == ClientSize)
                graphics.DrawImageUnscaled(backdrop, -origin.X, -origin.Y);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { DrawBackdrop(e.Graphics, Point.Empty); }
        private void RenderBackdrop()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            if (backdrop == null || backdrop.Size != ClientSize)
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = new Bitmap(ClientSize.Width, ClientSize.Height);
            }
            using (var graphics = Graphics.FromImage(backdrop))
                FireworksScene.Draw(graphics, ClientSize, animationTime.Elapsed.TotalSeconds);
            Invalidate(true);
        }
        private void UpdateAnimation()
        {
            bool enabled = ready && engine.GoalReached && Visible && WindowState != FormWindowState.Minimized && !systemSuspended;
            if (enabled == animation.Enabled) return;
            animation.Enabled = enabled;
            if (enabled) { animationTime.Start(); RenderBackdrop(); }
            else
            {
                animationTime.Stop();
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
                if (!engine.GoalReached) animationTime.Reset();
                Invalidate(true);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(36, 39, 45)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width-1, Height-1);
                e.Graphics.DrawLine(pen, Width/2, studyLabel.Top + Px(6), Width/2, studyStatus.Bottom - Px(3));
            }
        }

        private void UpdateDisplay()
        {
            wallClock.SetValue(ClockEngine.FormatTime(DateTime.Now), true, "当前时间");
            study.SetValue(ClockEngine.Format(state.StudyTicks), engine.Running && state.Selected == 0, "学习 / 工作");
            rest.SetValue(ClockEngine.Format(state.RestTicks), engine.Running && state.Selected == 1, "休息 / 娱乐");
            studyStatus.Text = StatusFor(0); restStatus.Text = StatusFor(1);
            if (state.GoalTicks > 0)
                studyStatus.Text = (engine.GoalReached ? "已达成 · " : "目标 ") + ClockEngine.Format(state.GoalTicks);
            string goalTip = state.GoalTicks == 0 ? "设置学习 / 工作目标" :
                "目标 " + ClockEngine.Format(state.GoalTicks) + " · 已学习 " + ClockEngine.Format(state.StudyTicks);
            if (tips.GetToolTip(goal) != goalTip) tips.SetToolTip(goal, goalTip);
            if (goal.Lit != (state.GoalTicks > 0)) { goal.Lit = state.GoalTicks > 0; goal.Invalidate(); }
            studyStatus.ForeColor = engine.Running && state.Selected == 0 ? Color.FromArgb(190,195,204) : Palette.Muted;
            restStatus.ForeColor = engine.Running && state.Selected == 1 ? Color.FromArgb(190,195,204) : Palette.Muted;
            studyLabel.ForeColor = engine.Running && state.Selected == 0 ? Palette.Foreground : Color.FromArgb(145,150,159);
            restLabel.ForeColor = engine.Running && state.Selected == 1 ? Palette.Foreground : Color.FromArgb(145,150,159);
            main.Text = !state.Started ? "开始学习" : state.Selected == 0 ? "切换到休息" : "切换到学习";
            reset.Enabled = state.Started || state.StudyTicks > 0 || state.RestTicks > 0;
            string info = saveError ? "保存失败 · 请检查磁盘空间和数据目录权限" :
                message != null && DateTime.UtcNow < messageUntil ? message : "";
            if (footer.Text != info) footer.Text = info;
            UpdateAnimation();
        }
        private string StatusFor(int side)
        {
            if (!state.Started) return "待开始";
            if (state.Selected != side) return "等待切换";
            return engine.Running ? "●  正在计时" : "等待系统唤醒";
        }
        private void Notify(string text, int seconds)
        {
            message = text; messageUntil = DateTime.UtcNow.AddSeconds(seconds); UpdateDisplay();
        }
        private void Changed() { UpdateDisplay(); Save(); }
        private void CaptureBounds()
        {
            Rectangle r = normalBounds;
            if (r.Width <= 0 || r.Height <= 0) return;
            state.HasBounds = true;
            state.X = r.X; state.Y = r.Y; state.Width = r.Width; state.Height = r.Height;
            state.WindowDpi = fullscreen ? savedNormalDpi : dpi;
            state.Pinned = TopMost;
        }
        private void Save()
        {
            engine.Tick();
            try { PersistState(state); }
            catch (Exception e) { if (!StateStore.Recoverable(e)) throw; }
            lastSaveTick = clock.Ticks;
        }
        private void PersistState(AppState snapshot)
        {
            CaptureBounds();
            try { store.Save(snapshot); saveError = false; }
            catch
            {
                saveError = true;
                throw;
            }
            lastSaveTick = clock.Ticks;
        }
        private int savedNormalDpi = 96;
        private void ToggleFullscreen()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!fullscreen)
            {
                normalBounds = Bounds; savedNormalDpi = dpi; fullscreen = true;
                Bounds = Screen.FromControl(this).Bounds;
                full.Glyph = "restore"; full.AccessibleName = "退出全屏";
            }
            else
            {
                Rectangle restore = normalBounds;
                Screen target = Screen.FromRectangle(restore);
                int restoreDpi = Native.MonitorDpi(target);
                if (restoreDpi != savedNormalDpi)
                {
                    restore.Width = (int)Math.Round(restore.Width * (double)restoreDpi / savedNormalDpi);
                    restore.Height = (int)Math.Round(restore.Height * (double)restoreDpi / savedNormalDpi);
                }
                fullscreen = false;
                SetDpi(restoreDpi);
                Bounds = restore;
                full.Glyph = "full"; full.AccessibleName = "全屏";
                EnsureVisible();
            }
            LayoutContents(); full.Invalidate(); Save();
        }
        private void ConfirmReset()
        {
            if (ConfirmForm.Ask(this, "重置本次计时？", "两侧累计将归零，每日历史会保留。", dpi))
            {
                engine.Reset(); Changed(); Notify("本次计时已重置 · 每日历史已保留", 5);
            }
        }
        private void ShowHistory()
        {
            engine.Tick(); Save();
            if (historyForm != null && !historyForm.IsDisposed) { historyForm.Activate(); return; }
            historyForm = new HistoryForm(engine, dpi, delegate { Save(); }, PersistState);
            historyForm.Icon = Icon;
            historyForm.Show(this);
        }
        private void ShowGoal()
        {
            engine.Tick();
            using (var dialog = new GoalForm(state.GoalTicks, state.StudyTicks, dpi))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    engine.SetGoal(dialog.GoalTicks, PersistState);
                    if (animation.Enabled) animationTime.Restart();
                    else animationTime.Reset();
                    UpdateDisplay();
                }
                catch (Exception e)
                {
                    if (!StateStore.Recoverable(e)) throw;
                    UpdateDisplay();
                    MessageBox.Show(this, "目标保存失败，已恢复原设置。请检查磁盘空间和数据目录权限。", Branding.Name);
                }
            }
        }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Suspend && e.Mode != PowerModes.Resume) return;
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    Action update = delegate
                    {
                        systemSuspended = e.Mode == PowerModes.Suspend;
                        if (systemSuspended) engine.Suspend();
                        else engine.Resume();
                        Changed();
                    };
                    if (InvokeRequired) Invoke(update);
                    else update();
                }
                catch (InvalidOperationException) {}
            }
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed)
                try { BeginInvoke((Action)delegate { EnsureVisible(); }); } catch (InvalidOperationException) {}
        }
        private void SessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (IsHandleCreated && !IsDisposed)
                try { Invoke((Action)delegate { Save(); }); } catch (InvalidOperationException) {}
        }
        private void EnsureVisible()
        {
            if (fullscreen)
            {
                Rectangle restored = normalBounds;
                Screen oldScreen = Screen.FromRectangle(restored);
                Rectangle area = oldScreen.WorkingArea;
                restored.Width = Math.Min(restored.Width, area.Width);
                restored.Height = Math.Min(restored.Height, area.Height);
                restored.X = Math.Max(area.Left, Math.Min(restored.X, area.Right-restored.Width));
                restored.Y = Math.Max(area.Top, Math.Min(restored.Y, area.Bottom-restored.Height));
                normalBounds = restored;
                Bounds = Screen.FromControl(this).Bounds;
                return;
            }
            if (WindowState != FormWindowState.Normal) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int w = Math.Min(Width, work.Width), h = Math.Min(Height, work.Height);
            Bounds = new Rectangle(Math.Max(work.Left, Math.Min(Left, work.Right-w)),
                Math.Max(work.Top, Math.Min(Top, work.Bottom-h)), w, h);
            normalBounds = Bounds;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
            if (keyData == Keys.Escape && fullscreen) { ToggleFullscreen(); return true; }
            if (keyData == Keys.Space) { engine.Switch(); Changed(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00A3 && m.WParam.ToInt32() == 2)
            {
                ToggleFullscreen(); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == Native.ActivateMessage)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show(); Activate(); Native.SetForegroundWindow(Handle); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x02E0)
            {
                int value = (int)(m.WParam.ToInt64() & 0xffff);
                RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                SetDpi(value);
                Bounds = new Rectangle(r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top);
                LayoutContents(); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x0084 && !fullscreen)
            {
                Point p = PointToClient(new Point(unchecked((short)(m.LParam.ToInt64() & 0xffff)),
                    unchecked((short)((m.LParam.ToInt64() >> 16) & 0xffff))));
                int edge = Px(6), hit = 1;
                bool l = p.X < edge, r = p.X >= Width-edge, t = p.Y < edge, b = p.Y >= Height-edge;
                if (l && t) hit = 13; else if (r && t) hit = 14; else if (l && b) hit = 16; else if (r && b) hit = 17;
                else if (l) hit = 10; else if (r) hit = 11; else if (t) hit = 12; else if (b) hit = 15;
                else if (p.Y < Px(40)) hit = 2;
                m.Result = (IntPtr)hit; return;
            }
            base.WndProc(ref m);
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closing)
            {
                Save();
                if (saveError && e.CloseReason == CloseReason.UserClosing)
                {
                    MessageBox.Show(this, "记录尚未保存成功。请检查磁盘空间及数据目录权限后再关闭。\n\n数据位置：" +
                        Path.GetDirectoryName(store.FilePath), "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                    return;
                }
                closing = true; timer.Stop(); animation.Stop();
                SystemEvents.PowerModeChanged -= PowerChanged;
                SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                SystemEvents.SessionEnding -= SessionEnding;
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= PowerChanged;
                SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                SystemEvents.SessionEnding -= SessionEnding;
                timer.Dispose(); tips.Dispose(); animation.Dispose();
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
                foreach (Control control in Controls)
                    if (control.Tag as string == "font-owned") control.Font.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ConfirmForm : Form
    {
        internal static bool Ask(Form owner, string title, string description, int dpi, string confirmText = "重置")
        {
            using (var dialog = new ConfirmForm(title, description, dpi, confirmText))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }
        private ConfirmForm(string title, string description, int dpi, string confirmText)
        {
            float s = dpi / 96f;
            Text = title; BackColor = Palette.Background; ForeColor = Palette.Foreground;
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 12*s, FontStyle.Regular, GraphicsUnit.Pixel);
            int detailHeight = Math.Max((int)(30*s), TextRenderer.MeasureText(description, Font,
                new Size((int)(306*s),0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + (int)(8*s));
            int buttonY = (int)(66*s) + detailHeight;
            ClientSize = new Size((int)(350*s), buttonY + (int)(54*s));
            var heading = new Label { Text = title, ForeColor = Palette.Foreground,
                Bounds = new Rectangle((int)(22*s),(int)(20*s),(int)(310*s),(int)(25*s)), Font = new Font(Font, FontStyle.Bold) };
            var detail = new Label { Text = description, ForeColor = Palette.Muted,
                Bounds = new Rectangle((int)(22*s),(int)(54*s),(int)(306*s),detailHeight) };
            var cancel = new SoftButton { Text = "取消", ScaleFactor = s,
                Bounds = new Rectangle((int)(150*s),buttonY,(int)(80*s),(int)(34*s)), DialogResult = DialogResult.Cancel };
            var ok = new SoftButton { Text = confirmText, Primary = true, ScaleFactor = s,
                Bounds = new Rectangle((int)(240*s),buttonY,(int)(88*s),(int)(34*s)), DialogResult = DialogResult.OK };
            Controls.AddRange(new Control[] { heading, detail, cancel, ok });
            AcceptButton = cancel; CancelButton = cancel;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(Handle); }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly ClockEngine engine;
        private readonly Action save;
        private readonly Action<AppState> persist;
        private readonly int initialDpi;
        private readonly DataGridView grid = new DataGridView();
        private readonly Label summary = new Label();
        private readonly Label caption = new Label();
        private readonly SoftButton delete = new SoftButton();
        private DateTime captionUntil;
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        internal HistoryForm(ClockEngine engine, int dpi, Action save, Action<AppState> persist)
        {
            this.engine = engine; this.save = save; this.persist = persist; initialDpi = dpi;
            float s = dpi / 96f;
            Text = Branding.Name + " · 每日历史"; BackColor = Palette.Background; ForeColor = Palette.Foreground;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(dpi, dpi);
            Size = new Size((int)(620*s),(int)(440*s)); MinimumSize = new Size((int)(440*s),(int)(300*s));
            Font = new Font("Microsoft YaHei UI", 12*s, FontStyle.Regular, GraphicsUnit.Pixel);
            summary.Dock = DockStyle.Top; summary.Height = (int)(62*s);
            summary.Padding = new Padding((int)(18*s),(int)(15*s),0,0);
            summary.ForeColor = Palette.Foreground;
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = (int)(60*s), Padding = new Padding((int)(16*s)) };
            var export = new SoftButton { Text = "导出 CSV", Primary = true, Dock = DockStyle.Right,
                Width = (int)(110*s), ScaleFactor = s };
            export.Click += delegate { Export(); };
            delete.Text = "删除"; delete.AccessibleName = "删除选中的历史记录";
            delete.Dock = DockStyle.Left; delete.Width = (int)(72*s); delete.ScaleFactor = s; delete.Enabled = false;
            delete.Click += delegate { DeleteSelected(); };
            caption.Text = "重置保留每日历史"; caption.Dock = DockStyle.Fill;
            caption.TextAlign = ContentAlignment.MiddleLeft; caption.ForeColor = Palette.Muted;
            caption.Padding = new Padding((int)(12*s),0,0,0);
            caption.AutoEllipsis = true;
            bottom.Controls.Add(caption);
            bottom.Controls.Add(delete);
            bottom.Controls.Add(export);
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true; grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false; grid.RowHeadersVisible = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Palette.Background; grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Color.FromArgb(33,36,43); grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(23,25,30);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(160,166,176);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding((int)(12*s),0,0,0);
            grid.ColumnHeadersHeight = (int)(38*s); grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle.BackColor = Palette.Background; grid.DefaultCellStyle.ForeColor = Palette.Foreground;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(42,46,54);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Padding = new Padding((int)(12*s),0,0,0);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowTemplate.Height = (int)(38*s);
            foreach (string name in new[] { "日期", "学习 / 工作", "休息 / 娱乐", "合计", "切换次数" })
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, Name = name,
                    MinimumWidth = (int)((name == "切换次数" ? 104 : 100)*s),
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.SelectionChanged += delegate { delete.Enabled = SelectedDate != null; };
            Controls.Add(grid); Controls.Add(bottom); Controls.Add(summary);
            LoadRows();
            refresh.Interval = 1000; refresh.Tick += delegate
            {
                engine.Tick(); UpdateRows();
                if (DateTime.UtcNow >= captionUntil) caption.Text = "重置保留每日历史";
            }; refresh.Start();
        }
        private string SelectedDate
        {
            get { return grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as string : null; }
        }
        private void LoadRows()
        {
            string selected = SelectedDate;
            int top = grid.FirstDisplayedScrollingRowIndex;
            string topDate = top >= 0 && top < grid.Rows.Count ? grid.Rows[top].Tag as string : null;
            grid.Rows.Clear();
            foreach (DayRecord day in engine.State.Days.OrderByDescending(d => d.Date))
            {
                int index = grid.Rows.Add(day.Date, ClockEngine.Format(day.StudyTicks), ClockEngine.Format(day.RestTicks),
                    ClockEngine.Format(day.StudyTicks + day.RestTicks), ClockEngine.FormatSwitchCount(day));
                grid.Rows[index].Tag = day.Date;
            }
            UpdateSummary();
            grid.ClearSelection();
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
                if ((string)row.Tag == selected)
                {
                    grid.CurrentCell = row.Cells[0]; row.Selected = true; break;
                }
            foreach (DataGridViewRow row in grid.Rows)
                if ((string)row.Tag == topDate) { grid.FirstDisplayedScrollingRowIndex = row.Index; break; }
            delete.Enabled = SelectedDate != null;
        }
        private void UpdateRows()
        {
            var days = engine.State.Days.OrderByDescending(d => d.Date).ToList();
            if (days.Count != grid.Rows.Count || !days.Select(d => d.Date).SequenceEqual(grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as string)))
            { LoadRows(); return; }
            for (int i = 0; i < days.Count; i++)
            {
                grid.Rows[i].Cells[1].Value = ClockEngine.Format(days[i].StudyTicks);
                grid.Rows[i].Cells[2].Value = ClockEngine.Format(days[i].RestTicks);
                grid.Rows[i].Cells[3].Value = ClockEngine.Format(days[i].StudyTicks + days[i].RestTicks);
                grid.Rows[i].Cells[4].Value = ClockEngine.FormatSwitchCount(days[i]);
            }
            UpdateSummary();
        }
        private void DeleteSelected()
        {
            string date = SelectedDate;
            if (date == null) return;
            string description = "将删除该日的时长和切换次数。\n主界面累计和计时状态保持不变。";
            if (date == engine.Today) description += "\n后续计时或切换会重新生成今天的记录。";
            int currentDpi;
            try { currentDpi = (int)Native.GetDpiForWindow(Handle); }
            catch (EntryPointNotFoundException) { currentDpi = initialDpi; }
            if (!ConfirmForm.Ask(this, "删除 " + date + " 的历史？", description, Math.Max(96,currentDpi), "删除")) return;
            try
            {
                if (engine.DeleteDay(date, persist))
                {
                    caption.Text = "已删除 " + date;
                    captionUntil = DateTime.UtcNow.AddSeconds(5);
                }
                LoadRows();
            }
            catch (Exception e)
            {
                if (!StateStore.Recoverable(e)) throw;
                LoadRows();
                MessageBox.Show(this, "保存失败，该条历史已恢复。\n请检查磁盘空间及数据目录权限后重试。", "删除未保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void UpdateSummary()
        {
            long study = engine.State.Days.Sum(d => d.StudyTicks), rest = engine.State.Days.Sum(d => d.RestTicks);
            summary.Text = engine.State.Days.Count == 0 ? "还没有记录，开始一段学习吧。" :
                "已记录 " + engine.State.Days.Count + " 天     学习 " + ClockEngine.Format(study) + "     休息 " + ClockEngine.Format(rest);
        }
        private void Export()
        {
            engine.Tick(); save();
            using (var dialog = new SaveFileDialog { Title = "导出每日历史", Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = Branding.Name + "-每日历史-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", AddExtension = true, DefaultExt = "csv" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { StateStore.ExportCsv(dialog.FileName, engine.State.Days); summary.Text = "已导出到：" + dialog.FileName; }
                catch (Exception e)
                {
                    if (!StateStore.Recoverable(e)) throw;
                    MessageBox.Show(this, "无法写入文件，请选择可写的位置，或关闭正在使用该文件的软件。", "导出失败");
                }
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(Handle); }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try { Native.SetProcessDpiAwareness(2); } catch (EntryPointNotFoundException) {}
            string dataDirectory = Environment.GetEnvironmentVariable("DUALCLOCK_DATA_DIR");
            if (string.IsNullOrWhiteSpace(dataDirectory))
                dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualClock");
            string instanceKey = "Local\\DualClock.v1." + StableHash(Path.GetFullPath(dataDirectory).ToUpperInvariant());
            bool created;
            using (var mutex = new Mutex(true, instanceKey, out created))
            {
                if (!created)
                {
                    Native.PostMessage((IntPtr)0xffff, Native.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("程序遇到问题，请重新打开。最近保存的记录会保留。\n" + e.Exception.Message, Branding.Name);
                };
                Application.Run(new ClockForm(new StateStore(dataDirectory)));
                mutex.ReleaseMutex();
            }
        }
        private static string StableHash(string value)
        {
            uint hash = 2166136261;
            foreach (char c in value) { hash ^= c; hash *= 16777619; }
            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
