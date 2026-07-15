// Рисует иконки для обеих версий программы: provodnik.ico и provodnik-pro.ico.
// Запускается один раз через make-icons.bat; готовые .ico лежат рядом и коммитятся,
// так что при обычной сборке этот файл не нужен.
//
// Рисунок: тёмный квадрат и жёлтый знак QR-кода (три угловых квадрата — то, по чему
// QR узнаётся даже размером 16 пикселей). У версии «pro» в свободном углу глобус —
// намёк на доступ через интернет; у обычной — точки, как в настоящем QR.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class MakeIcons
{
    static readonly Color Back = Color.FromArgb(0x23, 0x28, 0x2c);   // --ink
    static readonly Color Tag = Color.FromArgb(0xf5, 0xb3, 0x01);    // --tag, жёлтый акцент
    static readonly Color Globe = Color.FromArgb(0xf5, 0xf6, 0xf3);  // --panel

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Save(Path.Combine(dir, "provodnik.ico"), false);
        Save(Path.Combine(dir, "provodnik-pro.ico"), true);
        Console.WriteLine("gotovo: provodnik.ico, provodnik-pro.ico");
    }

    static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    static void Save(string path, bool pro)
    {
        byte[][] images = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
        {
            using (Bitmap bmp = Draw(Sizes[i], pro))
            {
                // Размеры до 64 — старым форматом (DIB): его понимают все, включая
                // System.Drawing.Icon, через который иконку берёт значок в трее.
                // 128 и 256 — PNG, иначе файл раздувается; их читает только Проводник,
                // и он PNG понимает с Windows Vista.
                if (Sizes[i] <= 64)
                {
                    images[i] = Dib(bmp);
                }
                else
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        images[i] = ms.ToArray();
                    }
                }
            }
        }

        using (FileStream f = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(f))
        {
            w.Write((short)0);                     // зарезервировано
            w.Write((short)1);                     // тип: иконка
            w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)0);                  // цветов в палитре — нет
                w.Write((byte)0);                  // зарезервировано
                w.Write((short)1);                 // плоскостей
                w.Write((short)32);                // бит на пиксель
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] data in images) w.Write(data);
        }
    }

    /// <summary>Картинка в старом формате значков: заголовок, пиксели снизу вверх и маска.</summary>
    static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w2 = new BinaryWriter(ms))
        {
            w2.Write(40);                          // размер заголовка
            w2.Write(w);
            w2.Write(h * 2);                       // высота удвоена: картинка + маска
            w2.Write((short)1);                    // плоскостей
            w2.Write((short)32);                   // бит на пиксель
            w2.Write(0);                           // без сжатия
            w2.Write(w * h * 4);
            w2.Write(0); w2.Write(0); w2.Write(0); w2.Write(0);

            for (int y = h - 1; y >= 0; y--)       // пиксели снизу вверх, порядок BGRA
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    w2.Write(c.B); w2.Write(c.G); w2.Write(c.R); w2.Write(c.A);
                }

            int maskRow = ((w + 31) / 32) * 4;     // маска прозрачности: строки кратны 4 байтам
            for (int y = 0; y < h; y++) w2.Write(new byte[maskRow]);
            return ms.ToArray();
        }
    }

    static Bitmap Draw(int size, bool pro)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size;

            // тёмная подложка со скруглением
            using (GraphicsPath path = Rounded(0.02f * s, 0.02f * s, 0.96f * s, 0.96f * s, 0.16f * s))
            using (SolidBrush b = new SolidBrush(Back))
                g.FillPath(b, path);

            // три угловых квадрата QR-кода
            Finder(g, s, 0.10f, 0.10f);
            Finder(g, s, 0.58f, 0.10f);
            Finder(g, s, 0.10f, 0.58f);

            if (pro) DrawGlobe(g, s);
            else DrawDots(g, s);
        }
        return bmp;
    }

    /// <summary>Угловой квадрат QR-кода: рамка и точка внутри.</summary>
    static void Finder(Graphics g, float s, float x, float y)
    {
        float side = 0.32f * s;
        float stroke = Math.Max(1f, 0.055f * s);
        using (Pen p = new Pen(Tag, stroke))
            g.DrawRectangle(p, x * s + stroke / 2, y * s + stroke / 2, side - stroke, side - stroke);
        using (SolidBrush b = new SolidBrush(Tag))
            g.FillRectangle(b, x * s + side * 0.34f, y * s + side * 0.34f, side * 0.32f, side * 0.32f);
    }

    /// <summary>Обычная версия: точки, как в теле QR-кода.</summary>
    static void DrawDots(Graphics g, float s)
    {
        float d = 0.10f * s;
        float x0 = 0.58f * s, y0 = 0.58f * s;
        using (SolidBrush b = new SolidBrush(Tag))
        {
            g.FillRectangle(b, x0, y0, d, d);
            g.FillRectangle(b, x0 + d * 1.6f, y0, d, d);
            g.FillRectangle(b, x0, y0 + d * 1.6f, d, d);
            g.FillRectangle(b, x0 + d * 1.6f, y0 + d * 1.6f, d, d);
        }
    }

    /// <summary>Версия «pro»: глобус — доступ через интернет.</summary>
    static void DrawGlobe(Graphics g, float s)
    {
        float d = 0.30f * s;
        float x = 0.60f * s, y = 0.60f * s;
        float stroke = Math.Max(1f, 0.045f * s);
        using (Pen p = new Pen(Globe, stroke))
        {
            g.DrawEllipse(p, x, y, d, d);
            g.DrawLine(p, x, y + d / 2, x + d, y + d / 2);          // экватор
            if (s >= 32) g.DrawEllipse(p, x + d * 0.30f, y, d * 0.40f, d);  // меридиан
        }
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }
}
