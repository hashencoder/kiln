using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WebHut
{
    /// <summary>
    /// Simple outline icons drawn with GDI+ on a 24x24 grid, so the panel looks the same on every
    /// Windows version without depending on an icon font.
    /// </summary>
    public static class Icons
    {
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name, int size, Color color)
        {
            string key = name + "|" + size + "|" + color.ToArgb();
            Bitmap bmp;
            if (Cache.TryGetValue(key, out bmp)) return bmp;
            bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Draw(g, name, new RectangleF(0, 0, size, size), color);
            }
            Cache[key] = bmp;
            return bmp;
        }

        /// <summary>Draw icon <paramref name="name"/> scaled into <paramref name="r"/>.</summary>
        public static void Draw(Graphics g, string name, RectangleF r, Color color)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (name)
                {
                    case "home":
                        g.DrawLines(pen, P(3, 11, 12, 3.5f, 21, 11));
                        g.DrawLines(pen, P(5.5f, 9.5f, 5.5f, 20.5f, 18.5f, 20.5f, 18.5f, 9.5f));
                        g.DrawLines(pen, P(10, 20.5f, 10, 14.5f, 14, 14.5f, 14, 20.5f));
                        break;
                    case "globe":
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawEllipse(pen, 8, 3, 8, 18);
                        g.DrawLine(pen, 3, 12, 21, 12);
                        break;
                    case "database":
                        g.DrawEllipse(pen, 5, 3, 14, 5);
                        g.DrawLine(pen, 5, 5.5f, 5, 18.5f);
                        g.DrawLine(pen, 19, 5.5f, 19, 18.5f);
                        g.DrawArc(pen, 5, 9.5f, 14, 5, 0, 180);
                        g.DrawArc(pen, 5, 16, 14, 5, 0, 180);
                        break;
                    case "folder":
                        g.DrawPolygon(pen, P(3, 6, 3, 19, 21, 19, 21, 8.5f, 11.5f, 8.5f, 9.5f, 5, 4, 5));
                        break;
                    case "terminal":
                        using (var p = Ui.Rounded(new RectangleF(3, 4, 18, 16), 2.5f)) g.DrawPath(pen, p);
                        g.DrawLines(pen, P(7, 9, 10, 12, 7, 15));
                        g.DrawLine(pen, 12.5f, 15, 16.5f, 15);
                        break;
                    case "refresh":
                        g.DrawArc(pen, 4, 4, 16, 16, 200, 130);
                        g.DrawLines(pen, P(17.5f, 3.5f, 18.6f, 7.2f, 15, 8.3f));
                        g.DrawArc(pen, 4, 4, 16, 16, 20, 130);
                        g.DrawLines(pen, P(6.5f, 20.5f, 5.4f, 16.8f, 9, 15.7f));
                        break;
                    case "cube":
                        g.DrawPolygon(pen, P(12, 2.5f, 20.5f, 7, 20.5f, 17, 12, 21.5f, 3.5f, 17, 3.5f, 7));
                        g.DrawLines(pen, P(3.5f, 7, 12, 11.5f, 20.5f, 7));
                        g.DrawLine(pen, 12, 11.5f, 12, 21.5f);
                        break;
                    case "gear":
                        g.DrawEllipse(pen, 9, 9, 6, 6);
                        g.DrawEllipse(pen, 5.5f, 5.5f, 13, 13);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, 12 + (float)Math.Cos(a) * 6.5f, 12 + (float)Math.Sin(a) * 6.5f,
                                            12 + (float)Math.Cos(a) * 9.5f, 12 + (float)Math.Sin(a) * 9.5f);
                        }
                        break;
                    case "info":
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLine(pen, 12, 11, 12, 16.5f);
                        g.FillEllipse(brush, 10.8f, 6.6f, 2.4f, 2.4f);
                        break;
                    case "play":
                        g.FillPolygon(brush, P(7, 4.5f, 19.5f, 12, 7, 19.5f));
                        break;
                    case "stop":
                        using (var p = Ui.Rounded(new RectangleF(5.5f, 5.5f, 13, 13), 2f)) g.FillPath(brush, p);
                        break;
                    case "external":
                        g.DrawLines(pen, P(11, 5, 5, 5, 5, 19, 19, 19, 19, 13));
                        g.DrawLine(pen, 11.5f, 12.5f, 20, 4);
                        g.DrawLines(pen, P(14, 4, 20, 4, 20, 10));
                        break;
                    case "file":
                        g.DrawPolygon(pen, P(6, 2.5f, 14, 2.5f, 19, 7.5f, 19, 21.5f, 6, 21.5f));
                        g.DrawLines(pen, P(14, 2.5f, 14, 7.5f, 19, 7.5f));
                        g.DrawLine(pen, 9, 12.5f, 16, 12.5f);
                        g.DrawLine(pen, 9, 16.5f, 16, 16.5f);
                        break;
                    case "pulse":
                        g.DrawLines(pen, P(2, 12, 6.5f, 12, 9.5f, 4.5f, 14.5f, 19.5f, 17.5f, 12, 22, 12));
                        break;
                    case "bolt":
                        g.DrawPolygon(pen, P(13.5f, 2, 4, 14, 11.5f, 14, 10.5f, 22, 20, 10, 12.5f, 10));
                        break;
                    case "trash":
                        g.DrawLine(pen, 3.5f, 6, 20.5f, 6);
                        g.DrawLines(pen, P(9, 6, 9, 3, 15, 3, 15, 6));
                        g.DrawLines(pen, P(6, 6, 7, 21, 17, 21, 18, 6));
                        g.DrawLine(pen, 10, 10.5f, 10, 16.5f);
                        g.DrawLine(pen, 14, 10.5f, 14, 16.5f);
                        break;
                    case "power":
                        g.DrawArc(pen, 4, 4.5f, 16, 16, -50, 280);
                        g.DrawLine(pen, 12, 2.5f, 12, 11.5f);
                        break;
                    case "server":
                        using (var p = Ui.Rounded(new RectangleF(3, 3.5f, 18, 7), 2f)) g.DrawPath(pen, p);
                        using (var p = Ui.Rounded(new RectangleF(3, 13.5f, 18, 7), 2f)) g.DrawPath(pen, p);
                        g.FillEllipse(brush, 6, 6, 2.2f, 2.2f);
                        g.FillEllipse(brush, 6, 16, 2.2f, 2.2f);
                        g.DrawLine(pen, 12, 7, 17.5f, 7);
                        g.DrawLine(pen, 12, 17, 17.5f, 17);
                        break;
                    case "moon":
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(3.5f, 3.5f, 17, 17, 300, 300);
                            path.AddArc(8.5f, 1.5f, 13, 13, 168, -228);
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        break;
                    case "sun":
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, 12 + (float)Math.Cos(a) * 7, 12 + (float)Math.Sin(a) * 7,
                                            12 + (float)Math.Cos(a) * 9.5f, 12 + (float)Math.Sin(a) * 9.5f);
                        }
                        break;
                    case "logs":
                        g.DrawLine(pen, 8, 6, 20, 6);
                        g.DrawLine(pen, 8, 12, 20, 12);
                        g.DrawLine(pen, 8, 18, 20, 18);
                        g.FillEllipse(brush, 3, 5, 2.2f, 2.2f);
                        g.FillEllipse(brush, 3, 11, 2.2f, 2.2f);
                        g.FillEllipse(brush, 3, 17, 2.2f, 2.2f);
                        break;
                }
            }
            g.Restore(state);
        }

        static PointF[] P(params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }
    }
}
