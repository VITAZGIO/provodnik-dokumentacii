// Колонка одной подпапки заявки: жёлтая бирка-кембрик со счётчиком файлов,
// список содержимого и приём перетаскивания.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Provodnik
{
    class ColumnPanel : Panel
    {
        public string Category { get; private set; }

        /// <summary>Сюда перетащили файлы или папки.</summary>
        public event Action<string, string[]> FilesDropped;

        /// <summary>Просьба удалить запись из папки.</summary>
        public event Action<string, Entry> DeleteRequested;

        readonly Label hint;
        readonly ListBox list;
        readonly Button addButton;
        readonly List<Entry> entries = new List<Entry>();

        int count;                              // сколько файлов лежит в подпапке
        double glow;                            // 0 — обычная, 1 — под перетаскиванием
        bool hovered;

        public ColumnPanel(string category)
        {
            Category = category;
            BackColor = Theme.Enclosure;         // фон рисуем сами, этот не виден
            AllowDrop = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            hint = new Label();
            hint.Text = "перетащите файлы сюда";
            hint.Font = Theme.Small;
            hint.ForeColor = Theme.InkSoft;
            hint.BackColor = Color.Transparent;
            hint.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(hint);

            list = new ListBox();
            list.Font = Theme.MonoSmall;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Panel;
            list.IntegralHeight = false;
            list.HorizontalScrollbar = true;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 20;
            list.DrawItem += OnDrawItem;
            list.KeyDown += OnListKeyDown;
            Controls.Add(list);

            addButton = new Button();
            addButton.Text = "Добавить файлы";
            addButton.FlatStyle = FlatStyle.Flat;
            addButton.BackColor = Theme.Panel2;
            addButton.FlatAppearance.BorderColor = Theme.Line;
            addButton.Click += delegate { PickFiles(); };
            addButton.MouseEnter += delegate { Anim.ColorTo(addButton, Color.White, 120); };
            addButton.MouseLeave += delegate { Anim.ColorTo(addButton, Theme.Panel2, 160); };
            Controls.Add(addButton);

            // перетаскивание ловим и на дочерних элементах: иначе список «съедает» drop
            foreach (Control c in new Control[] { this, list, hint, addButton })
            {
                c.AllowDrop = true;
                c.DragEnter += OnDragEnter;
                c.DragLeave += OnDragLeave;
                c.DragDrop += OnDragDrop;
            }

            Resize += delegate { Layout_(); };
            Layout_();
        }

        void Layout_()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            hint.SetBounds(10, 44, Math.Max(20, w - 20), 20);
            int top = 70;
            int footH = 36;
            list.SetBounds(8, top, Math.Max(20, w - 16), Math.Max(20, h - top - footH - 8));
            addButton.SetBounds(8, h - footH - 4, Math.Max(20, w - 16), 30);
        }

        // ------------------------------------------------------------ рисование ----
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Enclosure);

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Round(box, 6))
            {
                using (SolidBrush b = new SolidBrush(Anim.Mix(Theme.Panel, Color.FromArgb(0xff, 0xfb, 0xed), glow)))
                    g.FillPath(b, path);
                using (Pen p = new Pen(Anim.Mix(Theme.Line, Theme.TagEdge, glow), 1 + (float)glow))
                    g.DrawPath(p, path);
            }

            DrawTag(g);

            // пунктирная рамка зоны приёма — подсвечивается при перетаскивании
            Rectangle drop = new Rectangle(9, 40, Width - 19, 28);
            using (Pen p = new Pen(Anim.Mix(Theme.Line, Theme.TagEdge, glow)))
            {
                p.DashStyle = DashStyle.Dash;
                using (GraphicsPath path = Round(drop, 4)) g.DrawPath(p, path);
            }
        }

        /// <summary>Бирка-кембрик с названием подпапки и числом файлов.</summary>
        void DrawTag(Graphics g)
        {
            string text = Category.ToUpperInvariant();
            SizeF size = g.MeasureString(text, Theme.TagFont);
            int badge = count > 0 ? 22 : 0;
            int tagW = Math.Min(Width - 20, (int)size.Width + 24 + badge);
            Rectangle tag = new Rectangle((Width - tagW) / 2, 10, tagW, 24);

            using (GraphicsPath path = Round(tag, 11))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(tag,
                           Color.FromArgb(0xff, 0xc9, 0x2e), Theme.Tag, LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (Pen p = new Pen(Theme.TagEdge)) g.DrawPath(p, path);
            }

            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                Rectangle textRect = new Rectangle(tag.X + 8, tag.Y, tag.Width - 16 - badge, tag.Height);
                using (SolidBrush b = new SolidBrush(Theme.TagInk))
                    g.DrawString(text, Theme.TagFont, b, textRect, sf);
            }

            if (count > 0)
            {
                Rectangle dot = new Rectangle(tag.Right - 24, tag.Y + 3, 18, 18);
                using (SolidBrush b = new SolidBrush(Theme.Ink)) g.FillEllipse(b, dot);
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.DrawString(count > 99 ? "99+" : count.ToString(), Theme.Tiny, b, dot, sf);
                }
            }
        }

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? Color.FromArgb(0xff, 0xf0, 0xc4)
                                  : (e.Index % 2 == 0 ? Theme.Panel : Theme.Panel2);
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);

            string text = list.Items[e.Index].ToString();
            bool isDir = e.Index < entries.Count && entries[e.Index].IsDir;
            bool empty = entries.Count == 0;

            // слева — цветная метка: папка, файл или «пусто»
            if (!empty)
            {
                Rectangle mark = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 6, 8, 8);
                using (SolidBrush b = new SolidBrush(isDir ? Theme.TagEdge : Theme.InkSoft))
                {
                    if (isDir) e.Graphics.FillRectangle(b, mark);
                    else e.Graphics.FillEllipse(b, mark);
                }
            }

            using (SolidBrush b = new SolidBrush(empty ? Theme.InkSoft : Theme.Ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                Rectangle r = new Rectangle(e.Bounds.X + (empty ? 6 : 18), e.Bounds.Y,
                                            e.Bounds.Width - (empty ? 10 : 22), e.Bounds.Height);
                e.Graphics.DrawString(text, empty ? Theme.Small : Theme.MonoSmall, b, r, sf);
            }
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------ данные ----
        public void SetEntries(List<Entry> items, int fileCount)
        {
            entries.Clear();
            entries.AddRange(items);
            count = fileCount;
            list.BeginUpdate();
            list.Items.Clear();
            if (items.Count == 0) list.Items.Add("пусто");
            else foreach (Entry e in items) list.Items.Add(e.IsDir ? e.Name + "  ▸" : e.Name);
            list.EndUpdate();
            Invalidate();
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) RequestDeleteSelected();
        }

        /// <summary>Удалить выбранное — клавишей Delete.</summary>
        public void RequestDeleteSelected()
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= entries.Count) return;
            Action<string, Entry> h = DeleteRequested;
            if (h != null) h(Category, entries[i]);
        }

        void PickFiles()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Какие файлы добавить в «" + Category + "»?";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Fire(dlg.FileNames);
            }
        }

        void Fire(string[] paths)
        {
            Action<string, string[]> h = FilesDropped;
            if (h != null) h(Category, paths);
        }

        // ------------------------------------------------------- перетаскивание ----
        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            Highlight(true);
        }

        void OnDragLeave(object sender, EventArgs e)
        {
            Highlight(false);
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            Highlight(false);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            Fire((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        void Highlight(bool on)
        {
            if (hovered == on) return;
            hovered = on;
            hint.Text = on ? "отпустите — файлы лягут сюда" : "перетащите файлы сюда";
            hint.ForeColor = on ? Theme.TagEdge : Theme.InkSoft;
            double from = glow, to = on ? 1 : 0;
            Anim.Run(140, delegate (double t)
            {
                glow = from + (to - from) * t;
                Invalidate();
            });
        }

        /// <summary>Короткая жёлтая вспышка — когда в колонку что-то прилетело.</summary>
        public void Flash()
        {
            Anim.Run(500, delegate (double t)
            {
                glow = Math.Sin(t * Math.PI);
                Invalidate();
            });
        }
    }
}
