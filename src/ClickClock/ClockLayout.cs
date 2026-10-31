using System;
using System.Drawing;

namespace DualClock
{
    // Pure logical-pixel layout, shared by desktop rendering and headless CI checks.
    internal sealed class ClockLayout
    {
        internal bool Portrait;
        internal float ControlScale;
        internal RectangleF Clock, StudyLabel, Study, StudyStatus, RestLabel, Rest, RestStatus;
        internal RectangleF Main, Reset, Goal, History, Footer;
        internal PointF DividerStart, DividerEnd;

        internal static ClockLayout Calculate(float width, float height)
        {
            var l = new ClockLayout { Portrait = width < height };
            float scale = l.Portrait ? Math.Min(width / 400, (height - 54) / 666)
                : Math.Min(width / 480, (height - 34) / 216);
            scale = Math.Max(0.1f, scale);
            float center = width / 2;
            l.ControlScale = Math.Max(0.8f, (float)Math.Sqrt(scale));
            float c = l.ControlScale;
            l.Footer = new RectangleF(12, height - 19, width - 24, 14);
            if (l.Portrait)
            {
                float top = 34 + Math.Max(0, height - 54 - 666 * scale) / 2;
                float left = center - 176 * scale, bodyWidth = 352 * scale;
                l.Main = new RectangleF(center - 90*c, top + 526*scale, 180*c, 48*c);
                l.Clock = new RectangleF(center - l.Main.Width*.425f, top + 16*scale,
                    l.Main.Width*.85f, l.Main.Height*.85f);
                l.StudyLabel = new RectangleF(left, top+114*scale, bodyWidth, Math.Max(16,22*scale));
                l.Study = new RectangleF(left, top+146*scale, bodyWidth, 64*scale);
                l.StudyStatus = new RectangleF(left, top+218*scale, bodyWidth, Math.Max(16,20*scale));
                l.RestLabel = new RectangleF(left, top+334*scale, bodyWidth, Math.Max(16,22*scale));
                l.Rest = new RectangleF(left, top+366*scale, bodyWidth, 64*scale);
                l.RestStatus = new RectangleF(left, top+438*scale, bodyWidth, Math.Max(16,20*scale));
                l.DividerStart = new PointF(center-105*scale, top+286*scale);
                l.DividerEnd = new PointF(center+105*scale, top+286*scale);
                float row = l.Main.Bottom + 12*c;
                l.Reset = new RectangleF(center-86*c,row,48*c,32*c);
                l.Goal = new RectangleF(center-24*c,row,48*c,32*c);
                l.History = new RectangleF(center+38*c,row,48*c,32*c);
            }
            else
            {
                float top = 34 + Math.Max(0, height - 34 - 216*scale)/2;
                float left = center - 240*scale;
                l.Main = new RectangleF(center-80*c,Math.Min(top+150*scale,height-24-44*c),160*c,44*c);
                l.Clock = new RectangleF(center-l.Main.Width*.425f,top+4*scale,
                    l.Main.Width*.85f,l.Main.Height*.85f);
                l.StudyLabel = new RectangleF(left+18*scale,top+52*scale,204*scale,Math.Max(18,20*scale));
                l.RestLabel = new RectangleF(center+18*scale,top+52*scale,204*scale,Math.Max(18,20*scale));
                l.Study = new RectangleF(left+19*scale,top+76*scale,202*scale,42*scale);
                l.Rest = new RectangleF(center+19*scale,top+76*scale,202*scale,42*scale);
                l.StudyStatus = new RectangleF(left+18*scale,top+121*scale,204*scale,Math.Max(16,16*scale));
                l.RestStatus = new RectangleF(center+18*scale,top+121*scale,204*scale,Math.Max(16,16*scale));
                l.DividerStart = new PointF(center,top+56*scale);
                l.DividerEnd = new PointF(center,top+137*scale);
                float row=l.Main.Top+(l.Main.Height-32*c)/2;
                if (width<=640 && height<=360)
                {
                    l.Reset=new RectangleF(20,row,44,32*c);
                    l.Goal=new RectangleF(70,row,44,32*c);
                    l.History=new RectangleF(width-76,row,56,32*c);
                }
                else
                {
                    l.Goal=new RectangleF(l.Main.Left-56*c,row,44*c,32*c);
                    l.Reset=new RectangleF(l.Main.Left-106*c,row,44*c,32*c);
                    l.History=new RectangleF(l.Main.Right+12*c,row,56*c,32*c);
                }
            }
            return l;
        }
    }
}
