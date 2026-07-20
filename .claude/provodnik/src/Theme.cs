// Палитра и шрифты — те же, что на страницах прототипа (жёлтый акцент #f5b301).
using System.Drawing;

namespace Provodnik
{
    static class Theme
    {
        public static readonly Color Enclosure = Color.FromArgb(0xd9, 0xdc, 0xd6);
        public static readonly Color Panel = Color.FromArgb(0xf5, 0xf6, 0xf3);
        public static readonly Color Panel2 = Color.FromArgb(0xec, 0xee, 0xe9);
        public static readonly Color Ink = Color.FromArgb(0x23, 0x28, 0x2c);
        public static readonly Color InkSoft = Color.FromArgb(0x5a, 0x61, 0x67);
        public static readonly Color Line = Color.FromArgb(0xb6, 0xbc, 0xb4);
        public static readonly Color Tag = Color.FromArgb(0xf5, 0xb3, 0x01);
        public static readonly Color TagInk = Color.FromArgb(0x1d, 0x1a, 0x10);
        public static readonly Color TagEdge = Color.FromArgb(0x8a, 0x6a, 0x00);
        public static readonly Color Ok = Color.FromArgb(0x2f, 0x7d, 0x46);
        public static readonly Color Err = Color.FromArgb(0xb2, 0x3a, 0x2e);

        public static readonly Font Sans = new Font("Segoe UI", 9f);
        public static readonly Font SansBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Small = new Font("Segoe UI", 8f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);
        public static readonly Font MonoSmall = new Font("Consolas", 8.5f);
        public static readonly Font Head = new Font("Consolas", 8.5f, FontStyle.Bold);
        public static readonly Font TagFont = new Font("Consolas", 8.5f, FontStyle.Bold);
    }
}
