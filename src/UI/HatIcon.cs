using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Coatrack
{
    /// <summary>Draws Coatrack's top-hat icons (no image files needed).</summary>
    public static class HatIcon
    {
        public static readonly Color Orange = Color.FromArgb(0xD9, 0x77, 0x57);
        public static readonly Color Cream = Color.FromArgb(0xFA, 0xF9, 0xF5);
        public static readonly Color Ink = Color.FromArgb(0x13, 0x13, 0x13);

        /// <summary>A top hat as one solid shape, in a box of the given size at (x, y).</summary>
        static GraphicsPath HatPath(float x, float y, float s)
        {
            var p = new GraphicsPath();
            // Crown: slightly wider at the top, like a stovepipe.
            p.AddPolygon(new[]
            {
                new PointF(x + 0.24f * s, y + 0.10f * s),
                new PointF(x + 0.76f * s, y + 0.10f * s),
                new PointF(x + 0.71f * s, y + 0.72f * s),
                new PointF(x + 0.29f * s, y + 0.72f * s)
            });
            // Brim.
            float bx = x + 0.04f * s, by = y + 0.70f * s, bw = 0.92f * s, bh = 0.16f * s;
            p.AddPath(RoundedRect(bx, by, bw, bh, bh / 2f), false);
            p.FillMode = FillMode.Winding;
            return p;
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>The full-colour app icon: orange hat with a cream band on a dark rounded square.</summary>
        public static Bitmap DrawAppIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = size * 0.04f;
                using (GraphicsPath bg = RoundedRect(m, m, size - 2 * m, size - 2 * m, size * 0.22f))
                using (var b = new SolidBrush(Ink))
                    g.FillPath(b, bg);
                float hs = size * 0.70f, hx = (size - hs) / 2f, hy = size * 0.13f;
                using (GraphicsPath hat = HatPath(hx, hy, hs))
                using (var b = new SolidBrush(Orange))
                    g.FillPath(b, hat);
                // Band across the bottom of the crown.
                using (var band = new GraphicsPath())
                using (var b = new SolidBrush(Cream))
                {
                    band.AddPolygon(new[]
                    {
                        new PointF(hx + 0.277f * hs, hy + 0.55f * hs),
                        new PointF(hx + 0.723f * hs, hy + 0.55f * hs),
                        new PointF(hx + 0.712f * hs, hy + 0.68f * hs),
                        new PointF(hx + 0.288f * hs, hy + 0.68f * hs)
                    });
                    g.FillPath(b, band);
                }
            }
            return bmp;
        }

        /// <summary>
        /// The notification-area icon: a plain hat in the taskbar's text colour, with a badge showing the
        /// running instance's initial (badge = null for none).
        /// </summary>
        public static Icon DrawTrayIcon(int size, bool lightTaskbar, string badge)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    Color fg = lightTaskbar ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;
                    bool hasBadge = !string.IsNullOrEmpty(badge);
                    float hs = hasBadge ? size * 0.86f : size;
                    float hx = hasBadge ? 0 : 0, hy = hasBadge ? 0 : size * 0.02f;
                    using (GraphicsPath hat = HatPath(hx, hy, hs))
                    using (var b = new SolidBrush(fg))
                        g.FillPath(b, hat);

                    if (hasBadge)
                    {
                        float d = size * 0.60f;
                        float bx = size - d, by = size - d;
                        // A ring in the background colour separates the badge from the hat.
                        using (var cut = new SolidBrush(Color.Transparent))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            float r = size * 0.07f;
                            g.FillEllipse(cut, bx - r, by - r, d + 2 * r, d + 2 * r);
                            g.CompositingMode = CompositingMode.SourceOver;
                        }
                        using (var b = new SolidBrush(Orange)) g.FillEllipse(b, bx, by, d, d);
                        float fontPx = d * (badge.Length > 1 ? 0.62f : 0.78f);
                        using (var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(Color.White))
                        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString(badge, font, b, new RectangleF(bx, by + d * 0.03f, d, d), fmt);
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        /// <summary>Writes a multi-size .ico (PNG-compressed entries) of the app icon.</summary>
        public static void WriteAppIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap b = DrawAppIcon(sizes[i]))
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    images[i] = ms.ToArray();
                }
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
        }
    }
}
