// Колонка одной подпапки заявки: жёлтая бирка-кембрик, список файлов, приём перетаскивания.
using System;
using System.Collections.Generic;
using System.Drawing;
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

        readonly Label tag;
        readonly Label hint;
        readonly ListBox list;
        readonly Button addButton;
        readonly List<Entry> entries = new List<Entry>();

        public ColumnPanel(string category)
        {
            Category = category;
            BackColor = Theme.Panel;
            BorderStyle = BorderStyle.FixedSingle;
            AllowDrop = true;

            tag = new Label();
            tag.Text = category.ToUpperInvariant();
            tag.Font = Theme.TagFont;
            tag.BackColor = Theme.Tag;
            tag.ForeColor = Theme.TagInk;
            tag.TextAlign = ContentAlignment.MiddleCenter;
            tag.AutoEllipsis = true;
            Controls.Add(tag);

            hint = new Label();
            hint.Text = "перетащите файлы сюда";
            hint.Font = Theme.Small;
            hint.ForeColor = Theme.InkSoft;
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(hint);

            list = new ListBox();
            list.Font = Theme.MonoSmall;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Panel2;
            list.IntegralHeight = false;
            list.HorizontalScrollbar = true;
            list.KeyDown += OnListKeyDown;
            Controls.Add(list);

            addButton = new Button();
            addButton.Text = "Добавить файлы";
            addButton.Click += delegate { PickFiles(); };
            Controls.Add(addButton);

            // перетаскивание ловим и на дочерних элементах: иначе список «съедает» drop
            foreach (Control c in new Control[] { this, list, hint, tag })
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
            tag.SetBounds(8, 8, Math.Max(20, w - 16), 22);
            hint.SetBounds(8, 34, Math.Max(20, w - 16), 22);
            int listTop = 60;
            int footH = 32;
            list.SetBounds(6, listTop, Math.Max(20, w - 12), Math.Max(20, h - listTop - footH - 6));
            addButton.SetBounds(6, h - footH - 2, Math.Max(20, w - 12), 28);
        }

        public void SetEntries(List<Entry> items)
        {
            entries.Clear();
            entries.AddRange(items);
            list.BeginUpdate();
            list.Items.Clear();
            if (items.Count == 0) list.Items.Add("пусто");
            else foreach (Entry e in items) list.Items.Add(e.IsDir ? e.Name + "/" : e.Name);
            list.EndUpdate();
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            int i = list.SelectedIndex;
            if (i < 0 || i >= entries.Count) return;
            Action<string, Entry> h = DeleteRequested;
            if (h != null) h(Category, entries[i]);
        }

        /// <summary>Удалить выбранное — из меню окна или клавишей Delete.</summary>
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
            hint.ForeColor = on ? Theme.TagEdge : Theme.InkSoft;
            hint.Text = on ? "отпустите — файлы лягут сюда" : "перетащите файлы сюда";
            BackColor = on ? Color.FromArgb(0xff, 0xf8, 0xe0) : Theme.Panel;
        }
    }
}
