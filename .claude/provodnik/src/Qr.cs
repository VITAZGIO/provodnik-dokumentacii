// Генератор QR-кода. Написан с нуля, чтобы не тянуть внешние библиотеки:
// байтовый режим, коррекция ошибок уровня M, версии 1–20 (хватает с большим запасом:
// адрес вида https://xxx-yyy-zzz.trycloudflare.com/m?k=TOKEN — это версия 4–5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Provodnik
{
    static class Qr
    {
        public static int ForceMask = -1;          // -1 — выбирать маску по штрафным очкам


        // ---- таблицы стандарта ISO/IEC 18004 для уровня коррекции M ----

        // на версию: {всего кодовых слов, ECC на блок, блоков в группе 1, данных в блоке гр.1,
        //             блоков в группе 2, данных в блоке гр.2}
        static readonly int[][] EccM = new int[][]
        {
            null,                                     // версии нумеруются с 1
            new int[] {  26, 10,  1, 16,  0,  0 },
            new int[] {  44, 16,  1, 28,  0,  0 },
            new int[] {  70, 26,  1, 44,  0,  0 },
            new int[] { 100, 18,  2, 32,  0,  0 },
            new int[] { 134, 24,  2, 43,  0,  0 },
            new int[] { 172, 16,  4, 27,  0,  0 },
            new int[] { 196, 18,  4, 31,  0,  0 },
            new int[] { 242, 22,  2, 38,  2, 39 },
            new int[] { 292, 22,  3, 36,  2, 37 },
            new int[] { 346, 26,  4, 43,  1, 44 },
            new int[] { 404, 30,  1, 50,  4, 51 },
            new int[] { 466, 22,  6, 36,  2, 37 },
            new int[] { 532, 22,  8, 37,  1, 38 },
            new int[] { 581, 24,  4, 40,  5, 41 },
            new int[] { 655, 24,  5, 41,  5, 42 },
            new int[] { 733, 28,  7, 45,  3, 46 },
            new int[] { 815, 28, 10, 46,  1, 47 },
            new int[] { 901, 26,  9, 43,  4, 44 },
            new int[] { 991, 26,  3, 44, 11, 45 },
            new int[] {1085, 26,  3, 41, 13, 42 },
        };

        // позиции центров выравнивающих квадратов
        static readonly int[][] AlignPos = new int[][]
        {
            null, new int[] {},
            new int[] {6,18}, new int[] {6,22}, new int[] {6,26}, new int[] {6,30}, new int[] {6,34},
            new int[] {6,22,38}, new int[] {6,24,42}, new int[] {6,26,46}, new int[] {6,28,50},
            new int[] {6,30,54}, new int[] {6,32,58}, new int[] {6,34,62},
            new int[] {6,26,46,66}, new int[] {6,26,48,70}, new int[] {6,26,50,74},
            new int[] {6,30,54,78}, new int[] {6,30,56,82}, new int[] {6,30,58,86}, new int[] {6,34,62,90},
        };

        // «лишние» биты после сборки потока
        static readonly int[] Remainder =
            { 0, 0, 7, 7, 7, 7, 7, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 3, 3, 3 };

        // ---- арифметика в поле Галуа GF(256), примитивный многочлен 0x11D ----
        static readonly int[] Exp = new int[512];
        static readonly int[] Log = new int[256];

        static Qr()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x;
                Log[x] = i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        static int GfMul(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        /// <summary>Многочлен-генератор Рида—Соломона для нужного числа проверочных байт.</summary>
        static int[] RsGenerator(int degree)
        {
            int[] poly = new int[] { 1 };
            for (int i = 0; i < degree; i++)
            {
                int[] next = new int[poly.Length + 1];
                for (int j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= GfMul(poly[j], Exp[i]);
                }
                poly = next;
            }
            return poly;
        }

        /// <summary>Проверочные байты для одного блока данных.</summary>
        static byte[] RsEncode(byte[] data, int eccCount)
        {
            int[] gen = RsGenerator(eccCount);
            int[] rem = new int[eccCount];
            foreach (byte b in data)
            {
                int factor = b ^ rem[0];
                Array.Copy(rem, 1, rem, 0, eccCount - 1);
                rem[eccCount - 1] = 0;
                for (int i = 0; i < eccCount; i++) rem[i] ^= GfMul(gen[i + 1], factor);
            }
            byte[] result = new byte[eccCount];
            for (int i = 0; i < eccCount; i++) result[i] = (byte)rem[i];
            return result;
        }

        // ---- накопитель битов ----
        class Bits
        {
            public readonly List<bool> Data = new List<bool>();
            public void Add(int value, int length)
            {
                for (int i = length - 1; i >= 0; i--) Data.Add(((value >> i) & 1) != 0);
            }
        }

        /// <summary>Кодирует текст в матрицу модулей: true — чёрный.</summary>
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);

            int version = -1;
            for (int v = 1; v <= 20; v++)
            {
                int countBits = v < 10 ? 8 : 16;
                int capacity = DataCodewords(v) * 8;
                if (4 + countBits + data.Length * 8 <= capacity) { version = v; break; }
            }
            if (version < 0) throw new ArgumentException("Слишком длинный текст для QR-кода");

            int[] spec = EccM[version];
            int eccPerBlock = spec[1], g1Blocks = spec[2], g1Data = spec[3], g2Blocks = spec[4], g2Data = spec[5];
            int totalData = DataCodewords(version);

            // --- поток данных ---
            Bits bits = new Bits();
            bits.Add(0x4, 4);                                     // режим «байты»
            bits.Add(data.Length, version < 10 ? 8 : 16);
            foreach (byte b in data) bits.Add(b, 8);

            int capacityBits = totalData * 8;
            int terminator = Math.Min(4, capacityBits - bits.Data.Count);
            bits.Add(0, terminator);
            while (bits.Data.Count % 8 != 0) bits.Data.Add(false);
            byte[] pad = new byte[] { 0xEC, 0x11 };
            for (int i = 0; bits.Data.Count < capacityBits; i++) bits.Add(pad[i % 2], 8);

            byte[] stream = new byte[totalData];
            for (int i = 0; i < totalData; i++)
            {
                int b = 0;
                for (int j = 0; j < 8; j++) b = (b << 1) | (bits.Data[i * 8 + j] ? 1 : 0);
                stream[i] = (byte)b;
            }

            // --- разбивка на блоки и коррекция ошибок ---
            List<byte[]> dataBlocks = new List<byte[]>();
            List<byte[]> eccBlocks = new List<byte[]>();
            int offset = 0;
            for (int i = 0; i < g1Blocks + g2Blocks; i++)
            {
                int size = i < g1Blocks ? g1Data : g2Data;
                byte[] block = new byte[size];
                Array.Copy(stream, offset, block, 0, size);
                offset += size;
                dataBlocks.Add(block);
                eccBlocks.Add(RsEncode(block, eccPerBlock));
            }

            // --- чередование блоков ---
            List<byte> final = new List<byte>();
            int maxData = Math.Max(g1Data, g2Data);
            for (int i = 0; i < maxData; i++)
                foreach (byte[] block in dataBlocks)
                    if (i < block.Length) final.Add(block[i]);
            for (int i = 0; i < eccPerBlock; i++)
                foreach (byte[] block in eccBlocks)
                    final.Add(block[i]);

            Bits payload = new Bits();
            foreach (byte b in final) payload.Add(b, 8);
            payload.Add(0, Remainder[version]);

            // --- сборка матрицы ---
            int n = version * 4 + 17;
            bool[,] modules = new bool[n, n];
            bool[,] reserved = new bool[n, n];
            DrawFunctionPatterns(modules, reserved, version, n);
            PlaceData(modules, reserved, payload.Data, n);

            // --- выбор маски по штрафным очкам ---
            bool[,] best = null;
            int bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                if (ForceMask >= 0 && mask != ForceMask) continue;   // только для отладки
                bool[,] candidate = (bool[,])modules.Clone();
                ApplyMask(candidate, reserved, mask, n);
                DrawFormatInfo(candidate, mask, n);
                int penalty = Penalty(candidate, n);
                if (penalty < bestPenalty) { bestPenalty = penalty; best = candidate; }
            }
            return best;
        }

        static int DataCodewords(int version)
        {
            int[] s = EccM[version];
            return s[2] * s[3] + s[4] * s[5];
        }

        static void DrawFunctionPatterns(bool[,] m, bool[,] res, int version, int n)
        {
            // три больших квадрата по углам + отступ вокруг них
            int[][] finders = new int[][] { new int[] { 0, 0 }, new int[] { n - 7, 0 }, new int[] { 0, n - 7 } };
            foreach (int[] f in finders)
            {
                for (int dy = -1; dy <= 7; dy++)
                    for (int dx = -1; dx <= 7; dx++)
                    {
                        int x = f[0] + dx, y = f[1] + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n) continue;
                        bool on = (dx >= 0 && dx <= 6 && (dy == 0 || dy == 6)) ||
                                  (dy >= 0 && dy <= 6 && (dx == 0 || dx == 6)) ||
                                  (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
                        m[x, y] = on;
                        res[x, y] = true;
                    }
            }

            // выравнивающие квадратики
            int[] pos = AlignPos[version];
            foreach (int cy in pos)
                foreach (int cx in pos)
                {
                    if (res[cx, cy]) continue;         // не поверх больших квадратов
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            m[cx + dx, cy + dy] = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                            res[cx + dx, cy + dy] = true;
                        }
                }

            // пунктирные дорожки
            for (int i = 8; i < n - 8; i++)
            {
                if (!res[i, 6]) { m[i, 6] = i % 2 == 0; res[i, 6] = true; }
                if (!res[6, i]) { m[6, i] = i % 2 == 0; res[6, i] = true; }
            }

            // места под служебную информацию о формате
            for (int i = 0; i < 9; i++)
            {
                if (!res[i, 8]) res[i, 8] = true;
                if (!res[8, i]) res[8, i] = true;
            }
            for (int i = 0; i < 8; i++)
            {
                res[n - 1 - i, 8] = true;
                res[8, n - 1 - i] = true;
            }
            m[8, n - 8] = true;                        // всегда чёрный модуль
            res[8, n - 8] = true;

            // номер версии (только для крупных кодов)
            if (version >= 7)
            {
                int value = version;
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                value = (version << 12) | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((value >> i) & 1) != 0;
                    int a = i / 3, b = n - 11 + i % 3;
                    m[a, b] = bit; res[a, b] = true;
                    m[b, a] = bit; res[b, a] = true;
                }
            }
        }

        static void PlaceData(bool[,] m, bool[,] res, List<bool> bits, int n)
        {
            int index = 0;
            bool upward = true;
            for (int right = n - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;             // шестую колонку пропускаем — там дорожка
                for (int i = 0; i < n; i++)
                {
                    int y = upward ? n - 1 - i : i;
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        if (res[x, y]) continue;
                        m[x, y] = index < bits.Count && bits[index];
                        index++;
                    }
                }
                upward = !upward;
            }
        }

        static bool MaskBit(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (y / 2 + x / 3) % 2 == 0;
                case 5: return (x * y) % 2 + (x * y) % 3 == 0;
                case 6: return ((x * y) % 2 + (x * y) % 3) % 2 == 0;
                default: return ((x + y) % 2 + (x * y) % 3) % 2 == 0;
            }
        }

        static void ApplyMask(bool[,] m, bool[,] res, int mask, int n)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (!res[x, y] && MaskBit(mask, x, y)) m[x, y] = !m[x, y];
        }

        static void DrawFormatInfo(bool[,] m, int mask, int n)
        {
            int data = (0x0 << 3) | mask;              // 0b00 — уровень коррекции M
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int value = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) m[8, i] = ((value >> i) & 1) != 0;
            m[8, 7] = ((value >> 6) & 1) != 0;
            m[8, 8] = ((value >> 7) & 1) != 0;
            m[7, 8] = ((value >> 8) & 1) != 0;
            for (int i = 9; i < 15; i++) m[14 - i, 8] = ((value >> i) & 1) != 0;

            for (int i = 0; i < 8; i++) m[n - 1 - i, 8] = ((value >> i) & 1) != 0;
            for (int i = 8; i < 15; i++) m[8, n - 15 + i] = ((value >> i) & 1) != 0;
            m[8, n - 8] = true;
        }

        /// <summary>
        /// Штрафные очки по четырём правилам стандарта — по ним выбирается наименее «шумная»
        /// маска. Правила: 1) длинные одноцветные полосы, 2) квадраты 2×2, 3) узоры, похожие на
        /// угловой квадрат (сбивают камеру с толку), 4) перекос доли чёрного от половины.
        /// Тонкость, из-за которой камеры хуже читают код, если её пропустить: в правиле 3
        /// белое поле за краем кода тоже считается белыми модулями.
        /// </summary>
        static int Penalty(bool[,] m, int n)
        {
            int score = 0;

            // 1 и 3 — по строкам, затем по столбцам
            for (int dir = 0; dir < 2; dir++)
            {
                for (int a = 0; a < n; a++)
                {
                    bool runColor = false;
                    int runLength = 0;
                    int[] history = new int[7];
                    for (int b = 0; b < n; b++)
                    {
                        bool cur = dir == 0 ? m[b, a] : m[a, b];
                        if (cur == runColor)
                        {
                            runLength++;
                            if (runLength == 5) score += 3;
                            else if (runLength > 5) score += 1;
                        }
                        else
                        {
                            AddHistory(runLength, history, n);
                            if (!runColor) score += CountFinderPatterns(history) * 40;
                            runColor = cur;
                            runLength = 1;
                        }
                    }
                    score += TerminateAndCount(runColor, runLength, history, n) * 40;
                }
            }

            // 2. квадраты 2×2 одного цвета
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                    if (m[x, y] == m[x + 1, y] && m[x, y] == m[x, y + 1] && m[x, y] == m[x + 1, y + 1])
                        score += 3;

            // 4. перекос доли чёрного от половины
            int dark = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[x, y]) dark++;
            int total = n * n;
            int k = (int)((Math.Abs((long)dark * 20 - (long)total * 10) + total - 1) / total) - 1;
            score += k * 10;

            return score;
        }

        /// <summary>Ищет в истории полос соотношение 1:1:3:1:1 с широким белым полем сбоку.</summary>
        static int CountFinderPatterns(int[] history)
        {
            int len = history[1];
            bool core = len > 0 && history[2] == len && history[3] == len * 3
                        && history[4] == len && history[5] == len;
            return (core && history[0] >= len * 4 && history[6] >= len ? 1 : 0)
                 + (core && history[6] >= len * 4 && history[0] >= len ? 1 : 0);
        }

        static void AddHistory(int runLength, int[] history, int n)
        {
            if (history[0] == 0) runLength += n;        // белое поле перед началом строки
            Array.Copy(history, 0, history, 1, history.Length - 1);
            history[0] = runLength;
        }

        static int TerminateAndCount(bool runColor, int runLength, int[] history, int n)
        {
            if (runColor)
            {
                AddHistory(runLength, history, n);
                runLength = 0;
            }
            runLength += n;                            // белое поле после конца строки
            AddHistory(runLength, history, n);
            return CountFinderPatterns(history);
        }

        /// <summary>Рисует QR-код картинкой нужного размера в пикселях.</summary>
        public static Bitmap ToBitmap(string text, int sizePx)
        {
            bool[,] m = Encode(text);
            int n = m.GetLength(0);
            int quiet = 4;              // белое поле вокруг кода; по стандарту — 4 модуля.
                                        // С меньшим полем часть масок камерой не читается.
            int total = n + quiet * 2;
            int scale = Math.Max(1, sizePx / total);
            int side = total * scale;

            Bitmap bmp = new Bitmap(side, side);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0x1d, 0x1a, 0x10)))
                {
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                            if (m[x, y])
                                g.FillRectangle(brush, (x + quiet) * scale, (y + quiet) * scale, scale, scale);
                }
            }
            return bmp;
        }
    }
}
