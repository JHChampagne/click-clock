using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DualClock
{
    internal static class SharedBackground
    {
        internal static void Draw(Control control, Graphics graphics)
        {
            var clock = control.Parent as ClockForm;
            if (clock != null) clock.DrawBackdrop(graphics, control.Location);
            else graphics.Clear(Palette.Background);
        }
    }
    // A bounded, analytic particle scene: no growing particle list or per-frame state to save.
    internal static class FireworksScene
    {
        private static readonly Color[] Colors = {
            Color.FromArgb(194, 170, 120), Color.FromArgb(127, 172, 190), Color.FromArgb(177, 141, 172)
        };

        internal static void Draw(Graphics g, Size size, double seconds)
        {
            g.Clear(Palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(size.Width / 480f, size.Height / 250f);
            for (int burst = 0; burst < 3; burst++)
            {
                double time = (seconds + burst * 1.67) % 5.6;
                int cycle = (int)((seconds + burst * 1.67) / 5.6);
                float x = size.Width * (0.18f + ((burst * 37 + cycle * 23) % 65) / 100f);
                float y = size.Height * (0.23f + ((burst * 17 + cycle * 13) % 26) / 100f);
                Color color = Colors[burst];
                if (time < 0.7)
                {
                    float rise = (float)(time / 0.7);
                    float rocketY = size.Height - (size.Height - y) * rise;
                    using (var pen = new Pen(Color.FromArgb(105, color), Math.Max(1, scale)))
                        g.DrawLine(pen, x, rocketY + 10 * scale, x, rocketY);
                    continue;
                }
                double age = time - 0.7;
                if (age > 3.7) continue;
                int alpha = (int)(225 * Math.Pow(1 - age / 3.7, 1.1));
                for (int spark = 0; spark < 40; spark++)
                {
                    double angle = spark * Math.PI * 2 / 40 + burst * 0.23;
                    double speed = (42 + (spark * 19 % 40)) * scale;
                    double distance = speed * (1 - Math.Exp(-age * 0.9));
                    float px = x + (float)(Math.Cos(angle) * distance);
                    float py = y + (float)(Math.Sin(angle) * distance + age * age * 8 * scale);
                    double past = Math.Max(0, age - 0.24);
                    double tail = speed * (1 - Math.Exp(-past * 0.9));
                    float tx = x + (float)(Math.Cos(angle) * tail);
                    float ty = y + (float)(Math.Sin(angle) * tail + past * past * 8 * scale);
                    using (var pen = new Pen(Color.FromArgb(alpha * 3 / 4, color), Math.Max(1, scale)))
                        g.DrawLine(pen, tx, ty, px, py);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                        g.FillEllipse(brush, px, py, Math.Max(1.4f, 1.8f * scale), Math.Max(1.4f, 1.8f * scale));
                }
            }
        }
    }

    internal sealed class GoalForm : Form
    {
        private readonly NumericUpDown hours = new NumericUpDown(), minutes = new NumericUpDown();
        private readonly Label validation = new Label();
        internal long GoalTicks { get; private set; }

        internal GoalForm(long currentGoal, long studyTicks, int dpi)
        {
            float s = Math.Max(96, dpi) / 96f;
            Text = Branding.Name + " · 学习目标";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size((int)(360*s), (int)(250*s));
            BackColor = Palette.Background; ForeColor = Palette.Foreground;
            Font = new Font("Microsoft YaHei UI", 12*s, FontStyle.Regular, GraphicsUnit.Pixel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AddLabel("学习 / 工作目标", 22, 18, 316, 25, s);
            AddLabel("当前累计  " + ClockEngine.Format(studyTicks), 22, 51, 316, 24, s);
            long totalMinutes = currentGoal == 0 ? 60 : currentGoal / TimeSpan.TicksPerMinute;
            hours.Minimum = minutes.Minimum = 0; hours.Maximum = 999; minutes.Maximum = 59;
            hours.Value = totalMinutes / 60; minutes.Value = totalMinutes % 60;
            SetBounds(hours, 22, 91, 95, 30, s); SetBounds(minutes, 184, 91, 70, 30, s);
            foreach (var input in new[] {hours,minutes})
            {
                input.BackColor = Color.FromArgb(25,27,32); input.ForeColor = Palette.Foreground;
                input.Font = Font; Controls.Add(input);
            }
            hours.AccessibleName = "目标小时"; minutes.AccessibleName = "目标分钟";
            AddLabel("小时", 124, 93, 50, 25, s); AddLabel("分钟", 263, 93, 55, 25, s);
            validation.ForeColor = Palette.Muted; SetBounds(validation,22,139,316,35,s);
            validation.Text = "按主界面学习累计判断；重置保留目标。";
            Controls.Add(validation);
            var clear = MakeButton("清除目标",22,195,90,s);
            var cancel = MakeButton("取消",158,195,78,s);
            var save = MakeButton("保存",246,195,92,s); save.Primary = true;
            clear.Click += delegate { GoalTicks=0; DialogResult=DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel;
            save.Click += delegate
            {
                long count = (long)hours.Value * 60 + (long)minutes.Value;
                if (count == 0) { validation.Text = "请输入至少 1 分钟，或点击“清除目标”。"; return; }
                GoalTicks=count*TimeSpan.TicksPerMinute; DialogResult=DialogResult.OK;
            };
            AcceptButton = save; CancelButton = cancel;
        }
        private static void SetBounds(Control c, int x, int y, int w, int h, float s)
        { c.SetBounds((int)(x*s),(int)(y*s),(int)(w*s),(int)(h*s)); }
        private void AddLabel(string text,int x,int y,int w,int h,float s)
        {
            var label = new Label {Text=text,ForeColor=Palette.Muted};
            SetBounds(label,x,y,w,h,s); Controls.Add(label);
        }
        private SoftButton MakeButton(string text,int x,int y,int w,float s)
        {
            var button = new SoftButton {Text=text,ScaleFactor=s};
            SetBounds(button,x,y,w,34,s); Controls.Add(button); return button;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitle(Handle); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
