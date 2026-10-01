// Генератор иконки MonitorTray.ico (тёмный монитор с бирюзовым значком питания)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class IconGen
{
    static void Main()
    {
        int[] sizes = new int[] { 256, 48, 32, 16 };
        byte[][] data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap b = Draw(sizes[i]))
            {
                if (sizes[i] == 256)
                {
                    using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); data[i] = ms.ToArray(); }
                }
                else data[i] = ToIcoBmp(b);
            }
        }
        using (BinaryWriter w = new BinaryWriter(File.Create("MonitorTray.ico")))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            uint offset = 6 + (uint)(16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s == 256 ? 0 : s));
                w.Write((byte)(s == 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)data[i].Length);
                w.Write(offset);
                offset += (uint)data[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) w.Write(data[i]);
        }
        Console.WriteLine("MonitorTray.ico OK");
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size / 256f;
            float penW = 13 * s; if (penW < 2.2f) penW = 2.2f;
            float glowW = penW * 1.9f;
            float borderW = 3.2f * s; if (borderW < 1f) borderW = 1f;

            // корпус монитора
            using (GraphicsPath bez = RoundRect(22 * s, 26 * s, 212 * s, 172 * s, 18 * s))
            {
                RectangleF br = new RectangleF(22 * s, 26 * s, 212 * s, 172 * s);
                using (LinearGradientBrush lg = new LinearGradientBrush(br, Color.FromArgb(88, 99, 120), Color.FromArgb(52, 59, 74), 90f))
                    g.FillPath(lg, bez);
                using (Pen p = new Pen(Color.FromArgb(36, 41, 54), borderW)) g.DrawPath(p, bez);
            }
            // экран
            using (GraphicsPath scr = RoundRect(36 * s, 40 * s, 184 * s, 144 * s, 8 * s))
            {
                RectangleF sr = new RectangleF(36 * s, 40 * s, 184 * s, 144 * s);
                using (LinearGradientBrush sg = new LinearGradientBrush(sr, Color.FromArgb(24, 34, 50), Color.FromArgb(8, 12, 19), 90f))
                    g.FillPath(sg, scr);
            }
            // значок питания (дуга с разрывом сверху + вертикальная линия)
            float cx = 128 * s, cy = 112 * s, r = 46 * s;
            using (Pen glow = new Pen(Color.FromArgb(70, 0, 210, 255), glowW))
            {
                glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                g.DrawArc(glow, cx - r, cy - r, 2 * r, 2 * r, 318f, 284f);
                g.DrawLine(glow, cx, cy - r - 10 * s, cx, cy - 8 * s);
            }
            using (Pen pen = new Pen(Color.FromArgb(0, 216, 255), penW))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, 318f, 284f);
                g.DrawLine(pen, cx, cy - r - 10 * s, cx, cy - 8 * s);
            }
            // подставка
            using (GraphicsPath neck = RoundRect(116 * s, 196 * s, 24 * s, 24 * s, 5 * s))
            using (SolidBrush nb = new SolidBrush(Color.FromArgb(62, 70, 86)))
                g.FillPath(nb, neck);
            using (GraphicsPath bs = RoundRect(84 * s, 218 * s, 88 * s, 14 * s, 7 * s))
            {
                RectangleF brr = new RectangleF(84 * s, 218 * s, 88 * s, 14 * s);
                using (LinearGradientBrush bb = new LinearGradientBrush(brr, Color.FromArgb(88, 99, 120), Color.FromArgb(48, 54, 68), 90f))
                    g.FillPath(bb, bs);
            }
        }
        return bmp;
    }

    static byte[] ToIcoBmp(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[bd.Stride * h];
        Marshal.Copy(bd.Scan0, px, 0, px.Length);
        b.UnlockBits(bd);
        int maskStride = ((w + 31) / 32) * 4;
        int imgSize = w * h * 4 + maskStride * h;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter wr = new BinaryWriter(ms))
        {
            // BITMAPINFOHEADER
            wr.Write(40u); wr.Write(w); wr.Write(h * 2); wr.Write((ushort)1); wr.Write((ushort)32);
            wr.Write(0u); wr.Write((uint)imgSize); wr.Write(0); wr.Write(0);
            wr.Write(0); wr.Write(0);
            // пиксели снизу вверх (формат памяти GDI+ 32bppArgb = BGRA)
            for (int y = h - 1; y >= 0; y--)
                wr.Write(px, y * bd.Stride, w * 4);
            // AND-маска (все нули — прозрачность берётся из альфы)
            wr.Write(new byte[maskStride * h]);
            wr.Flush();
            return ms.ToArray();
        }
    }
}
