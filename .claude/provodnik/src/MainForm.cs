// Главное окно программы. Разделы сверху вниз:
//   шапка         — рабочая папка, кнопки «Выбрать…», «Подпапки…», справка;
//   новая заявка  — номер и наименование;
//   существующие  — список заявок;
//   приём с телефона — QR-код, адрес, выбор сетевого адреса, журнал принятого;
//   колонки       — по одной на подпапку открытой заявки, с перетаскиванием файлов;
//   низ           — строка состояния и «Выключить сервер».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Provodnik
{
    class MainForm : Form
    {
        readonly Server server;

        Label folderName, ipMsg, currentName, statusLabel;
        Panel folderChip, folderDot;
        TextBox orderNum, orderName, ipInput;
        ComboBox ordersBox;
        PictureBox qrImage;
        TextBox phoneUrl;
        ListBox recentList;
        Panel currentBar, colsPanel;
        Button openButton;

        string currentOrder;
        readonly List<ColumnPanel> columns = new List<ColumnPanel>();
        bool busy;

        // Раскладка блоков внутри карточек считается вручную, поэтому её нужно применить
        // и при первом показе окна, а не только при изменении размера.
        readonly List<Action> layouts = new List<Action>();

        public MainForm(Server server)
        {
            this.server = server;

            Text = "Проводник документации";
            ClientSize = new Size(1180, 830);
            MinimumSize = new Size(940, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Enclosure;
            Font = Theme.Sans;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            BuildUi();
            ApplyLayout();

            State.FileReceived += OnFileReceived;

            // повторный запуск exe поднимает флажок из чужого потока — ловим его здесь
            Timer watch = new Timer();
            watch.Interval = 300;
            watch.Tick += delegate
            {
                if (!State.ShowWindowRequested) return;
                State.ShowWindowRequested = false;
                ShowFromTray();
            };
            watch.Start();

            RefreshFolder();
            RefreshOrders();
            RefreshQr();
            RefreshRecent();
            SetStatus(Files.HasBase
                ? "Рабочая папка: " + Config.BaseDir
                : "Выберите рабочую папку, чтобы начать.", Files.HasBase ? Theme.Ok : Theme.InkSoft);
        }

        // ------------------------------------------------------------ вёрстка ----
        void BuildUi()
        {
            // Порядок важен: в WinForms при стыковке первым «занимает место» тот, кто добавлен
            // позже. Поэтому середина создаётся первой, а шапка и низ — после неё.
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(12, 10, 12, 6);
            Controls.Add(body);

            // --- шапка ---
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.BackColor = Theme.Panel;
            Controls.Add(header);

            Label brand = new Label();
            brand.Text = "ПРОВОДНИК ДОКУМЕНТАЦИИ";
            brand.Font = Theme.Head;
            brand.SetBounds(14, 10, 300, 16);
            header.Controls.Add(brand);

            Label brandSub = new Label();
            brandSub.Text = "папки заявок и раскладка файлов";
            brandSub.Font = Theme.Small;
            brandSub.ForeColor = Theme.InkSoft;
            brandSub.SetBounds(14, 28, 300, 16);
            header.Controls.Add(brandSub);

            folderChip = new Panel();
            folderChip.BackColor = Theme.Panel2;
            folderChip.BorderStyle = BorderStyle.FixedSingle;
            folderChip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(folderChip);

            folderDot = new Panel();
            folderDot.SetBounds(8, 10, 9, 9);
            folderDot.BackColor = Theme.Err;
            folderChip.Controls.Add(folderDot);

            folderName = new Label();
            folderName.Font = Theme.MonoSmall;
            folderName.AutoEllipsis = true;
            folderName.SetBounds(23, 7, 330, 16);
            folderChip.Controls.Add(folderName);

            Button pickButton = new Button();
            pickButton.Text = "Выбрать…";
            pickButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pickButton.Click += delegate { PickFolder(); };
            header.Controls.Add(pickButton);

            Button subfoldersButton = new Button();
            subfoldersButton.Text = "Подпапки…";
            subfoldersButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            subfoldersButton.Click += delegate { EditSubfolders(); };
            header.Controls.Add(subfoldersButton);

            Button helpButton = new Button();
            helpButton.Text = "?";
            helpButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            helpButton.Click += delegate { ShowHelp(); };
            header.Controls.Add(helpButton);

            Action layoutHeader = delegate
            {
                int right = header.ClientSize.Width - 12;
                helpButton.SetBounds(right - 34, 14, 34, 28); right -= 34 + 8;
                subfoldersButton.SetBounds(right - 96, 14, 96, 28); right -= 96 + 8;
                pickButton.SetBounds(right - 92, 14, 92, 28); right -= 92 + 8;
                int chipW = Math.Max(200, Math.Min(420, right - 330));
                folderChip.SetBounds(right - chipW, 15, chipW, 26);
                folderName.Width = folderChip.ClientSize.Width - 30;
            };
            header.Resize += delegate { layoutHeader(); };
            layouts.Add(layoutHeader);

            Panel rail = new Panel();
            rail.Dock = DockStyle.Top;
            rail.Height = 6;
            rail.BackColor = Color.FromArgb(0xc7, 0xcb, 0xc4);
            Controls.Add(rail);

            // --- низ: состояние и выключение ---
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 42;
            foot.BackColor = Theme.Panel;
            Controls.Add(foot);

            statusLabel = new Label();
            statusLabel.Font = Theme.MonoSmall;
            statusLabel.ForeColor = Theme.InkSoft;
            statusLabel.AutoEllipsis = true;
            statusLabel.SetBounds(14, 12, 600, 18);
            foot.Controls.Add(statusLabel);

            Button shutdown = new Button();
            shutdown.Text = "Выключить сервер";
            shutdown.ForeColor = Theme.Err;
            shutdown.Font = Theme.SansBold;
            shutdown.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            shutdown.Click += delegate { ShutdownClicked(); };
            foot.Controls.Add(shutdown);

            Action layoutFoot = delegate
            {
                shutdown.SetBounds(foot.ClientSize.Width - 12 - 160, 7, 160, 28);
                statusLabel.Width = Math.Max(100, foot.ClientSize.Width - 190);
            };
            foot.Resize += delegate { layoutFoot(); };
            layouts.Add(layoutFoot);

            // --- середина ---
            // внутри середины — тот же порядок: сначала то, что растягивается
            colsPanel = new Panel();
            colsPanel.Dock = DockStyle.Fill;
            colsPanel.Padding = new Padding(0, 10, 0, 0);
            body.Controls.Add(colsPanel);
            colsPanel.Resize += delegate { LayoutColumns(); };

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 316;

            currentBar = Card(null, 0, 0, 0, 0);
            currentBar.Dock = DockStyle.Top;
            currentBar.Height = 46;
            currentBar.Visible = false;
            body.Controls.Add(currentBar);
            body.Controls.Add(top);

            BuildOrderCards(top);
            BuildPhoneCard(top);

            currentName = new Label();
            currentName.Font = new Font(Theme.Mono.FontFamily, 11f, FontStyle.Bold);
            currentName.SetBounds(12, 13, 420, 22);
            currentBar.Controls.Add(currentName);

            Label currentHint = new Label();
            currentHint.Text = "перетащите файлы в нужную колонку или нажмите «Добавить файлы»";
            currentHint.ForeColor = Theme.InkSoft;
            currentHint.Font = Theme.Small;
            currentHint.SetBounds(440, 16, 460, 18);
            currentBar.Controls.Add(currentHint);
        }

        void BuildOrderCards(Panel parent)
        {
            Panel newCard = Card("НОВАЯ ЗАЯВКА", 0, 0, 640, 96);
            newCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(newCard);

            newCard.Controls.Add(Small("Номер заявки", 12, 32));
            orderNum = new TextBox();
            orderNum.Font = Theme.Mono;
            orderNum.SetBounds(12, 50, 110, 26);
            orderNum.KeyDown += OnEnterCreate;
            newCard.Controls.Add(orderNum);

            Label nameCap = Small("Наименование изделия", 134, 32);
            nameCap.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            newCard.Controls.Add(nameCap);
            nameCap.BringToFront();
            orderName = new TextBox();
            orderName.SetBounds(134, 50, 340, 26);
            orderName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            orderName.KeyDown += OnEnterCreate;
            newCard.Controls.Add(orderName);

            Button create = new Button();
            create.Text = "Создать папки";
            create.BackColor = Theme.Tag;
            create.ForeColor = Theme.TagInk;
            create.Font = Theme.SansBold;
            create.FlatStyle = FlatStyle.Flat;
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            create.SetBounds(486, 49, 130, 28);
            create.Click += delegate { CreateOrder(); };
            newCard.Controls.Add(create);

            Panel existing = Card("СУЩЕСТВУЮЩИЕ ЗАЯВКИ", 0, 104, 640, 96);
            existing.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(existing);

            existing.Controls.Add(Small("Заявка (новые сверху)", 12, 32));
            ordersBox = new ComboBox();
            ordersBox.Font = Theme.Mono;
            ordersBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ordersBox.SetBounds(12, 50, 380, 26);
            ordersBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            existing.Controls.Add(ordersBox);

            openButton = new Button();
            openButton.Text = "Открыть";
            openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openButton.SetBounds(404, 49, 100, 28);
            openButton.Click += delegate { OpenSelected(); };
            existing.Controls.Add(openButton);

            Button refresh = new Button();
            refresh.Text = "Обновить";
            refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refresh.SetBounds(512, 49, 104, 28);
            refresh.Click += delegate { RefreshOrders(); SetStatus("Список обновлён.", Theme.Ok); };
            existing.Controls.Add(refresh);

            Action layout = delegate
            {
                int leftW = Math.Max(430, parent.ClientSize.Width - 520);
                newCard.SetBounds(0, 0, leftW, 96);
                existing.SetBounds(0, 104, leftW, 96);
                orderName.Width = Math.Max(120, leftW - 134 - 12 - 142);
                create.Left = leftW - 12 - 130;
                ordersBox.Width = Math.Max(150, leftW - 12 - 12 - 220);
                openButton.Left = leftW - 12 - 208;
                refresh.Left = leftW - 12 - 104;
            };
            parent.Resize += delegate { layout(); };
            layouts.Add(layout);
        }

        void BuildPhoneCard(Panel parent)
        {
            const int cardW = 500;
            Panel card = Card("ПРИЁМ С ТЕЛЕФОНА", 0, 0, cardW, 312);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            parent.Controls.Add(card);

            Panel qrFrame = new Panel();
            qrFrame.SetBounds(12, 30, 190, 190);
            qrFrame.BackColor = Theme.Tag;
            card.Controls.Add(qrFrame);

            qrImage = new PictureBox();
            qrImage.SetBounds(3, 3, 184, 184);
            qrImage.BackColor = Color.White;
            qrImage.SizeMode = PictureBoxSizeMode.Zoom;
            qrFrame.Controls.Add(qrImage);

            Label steps = new Label();
            steps.Text = "1.  Телефон — в той же Wi-Fi сети, что и этот ПК.\n" +
                         "2.  Откройте камеру и наведите на QR-код.\n" +
                         "3.  Номер заявки, категория, «Снять фото» →\n" +
                         "     «Отправить».";
            steps.ForeColor = Theme.InkSoft;
            steps.Font = Theme.Small;
            steps.SetBounds(212, 30, cardW - 224, 62);
            card.Controls.Add(steps);

            phoneUrl = new TextBox();
            phoneUrl.Font = Theme.MonoSmall;
            phoneUrl.ReadOnly = true;
            phoneUrl.BackColor = Theme.Panel2;
            phoneUrl.BorderStyle = BorderStyle.FixedSingle;
            phoneUrl.SetBounds(212, 96, cardW - 224, 22);
            card.Controls.Add(phoneUrl);

            card.Controls.Add(Small("Адрес этого ПК в сети", 212, 124));
            ipInput = new TextBox();
            ipInput.Font = Theme.MonoSmall;
            ipInput.SetBounds(212, 142, cardW - 224, 24);
            card.Controls.Add(ipInput);

            AutoCompleteStringCollection ips = new AutoCompleteStringCollection();
            foreach (Adapter a in NetInfo.AllAdapters()) ips.Add(a.Ip);
            ipInput.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            ipInput.AutoCompleteSource = AutoCompleteSource.CustomSource;
            ipInput.AutoCompleteCustomSource = ips;

            int half = (cardW - 224 - 8) / 2;
            Button saveIp = new Button();
            saveIp.Text = "Сохранить";
            saveIp.SetBounds(212, 170, half, 26);
            saveIp.Click += delegate { SaveIp(); };
            card.Controls.Add(saveIp);

            Button autoIp = new Button();
            autoIp.Text = "Авто";
            autoIp.SetBounds(212 + half + 8, 170, half, 26);
            autoIp.Click += delegate { AutoIp(); };
            card.Controls.Add(autoIp);

            ipMsg = new Label();
            ipMsg.Text = "Если QR не открывается — у ПК несколько сетей.\nВыберите адрес, который видит телефон.";
            ipMsg.ForeColor = Theme.InkSoft;
            ipMsg.Font = Theme.Small;
            ipMsg.SetBounds(212, 200, cardW - 224, 32);
            card.Controls.Add(ipMsg);

            card.Controls.Add(Small("Принято с телефона", 12, 228));
            recentList = new ListBox();
            recentList.Font = Theme.MonoSmall;
            recentList.BorderStyle = BorderStyle.FixedSingle;
            recentList.BackColor = Theme.Panel2;
            recentList.IntegralHeight = false;
            recentList.HorizontalScrollbar = true;
            recentList.SetBounds(12, 246, cardW - 24, 54);
            card.Controls.Add(recentList);

            Action layout = delegate
            {
                card.SetBounds(Math.Max(0, parent.ClientSize.Width - cardW), 0, cardW, 312);
            };
            parent.Resize += delegate { layout(); };
            layouts.Add(layout);
        }

        void ApplyLayout()
        {
            foreach (Action a in layouts) a();
            LayoutColumns();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayout();                      // размеры окончательны только после показа
        }

        Panel Card(string title, int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.SetBounds(x, y, w, h);
            p.BackColor = Theme.Panel;
            p.BorderStyle = BorderStyle.FixedSingle;
            if (title != null)
            {
                Label head = new Label();
                head.Text = title;
                head.Font = Theme.Head;
                head.ForeColor = Theme.InkSoft;
                head.SetBounds(12, 10, 300, 14);
                p.Controls.Add(head);
            }
            return p;
        }

        Label Small(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Small;
            l.ForeColor = Theme.InkSoft;
            l.AutoSize = true;                  // иначе подписи налезают друг на друга
            l.Location = new Point(x, y);
            return l;
        }

        void OnEnterCreate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CreateOrder();
        }

        // ------------------------------------------------------------ данные ----
        void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        void RefreshFolder()
        {
            bool ok = Files.HasBase;
            folderName.Text = ok ? Config.BaseDir : "рабочая папка не выбрана";
            folderDot.BackColor = ok ? Theme.Ok : Theme.Err;
        }

        void RefreshOrders()
        {
            string keep = ordersBox.SelectedItem as string;
            List<string> orders = Files.OrderNames();
            orders.Sort(StringComparer.Ordinal);
            orders.Reverse();                                   // новые сверху
            ordersBox.BeginUpdate();
            ordersBox.Items.Clear();
            foreach (string o in orders) ordersBox.Items.Add(o);
            ordersBox.EndUpdate();
            if (keep != null && orders.Contains(keep)) ordersBox.SelectedItem = keep;
            else if (orders.Count > 0) ordersBox.SelectedIndex = 0;
            openButton.Enabled = orders.Count > 0;
        }

        void RefreshQr()
        {
            string url = server.PhoneUrl();
            phoneUrl.Text = url;
            ipInput.Text = Config.LanIpOverride.Length > 0 ? Config.LanIpOverride : NetInfo.LanIp();
            try
            {
                Image old = qrImage.Image;
                qrImage.Image = Qr.ToBitmap(url, 184);
                if (old != null) old.Dispose();
            }
            catch (Exception e)
            {
                SetStatus("QR не построился: " + e.Message, Theme.Err);
            }
        }

        void RefreshRecent()
        {
            recentList.BeginUpdate();
            recentList.Items.Clear();
            lock (State.Recent)
            {
                foreach (RecentItem it in State.Recent)
                    recentList.Items.Add(string.Format("{0}  {1} / {2} / {3}",
                                                       it.Time, it.Order, it.Cat, it.Name));
            }
            if (recentList.Items.Count == 0) recentList.Items.Add("файлов с телефона пока не было");
            recentList.EndUpdate();
        }

        /// <summary>Пришёл файл с телефона — вызывается из потока сервера.</summary>
        void OnFileReceived(RecentItem item)
        {
            if (IsDisposed) return;
            try { BeginInvoke((Action<RecentItem>)OnFileArrived, item); }
            catch (Exception) { }
        }

        void OnFileArrived(RecentItem item)
        {
            RefreshRecent();
            RefreshOrders();
            if (currentOrder != null && item.Order == currentOrder) RefreshColumns();
        }

        // ------------------------------------------------------------ заявки ----
        void PickFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Где хранить папки заявок?";
                if (Directory.Exists(Config.BaseDir)) dlg.SelectedPath = Config.BaseDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Config.BaseDir = dlg.SelectedPath;
                Config.Save();
            }
            CloseOrder();
            RefreshFolder();
            RefreshOrders();
            SetStatus("Рабочая папка: " + Config.BaseDir, Theme.Ok);
        }

        void CreateOrder()
        {
            if (!RequireBase()) return;
            string num = Util.Sanitize(orderNum.Text);
            string name = Util.Sanitize(orderName.Text);
            if (num.Length == 0 || name.Length == 0)
            {
                SetStatus("Заполните номер заявки и наименование изделия.", Theme.Err);
                return;
            }
            try
            {
                string created = Files.CreateOrder(num, name);
                RefreshOrders();
                ordersBox.SelectedItem = created;
                OpenOrder(created);
                SetStatus("Созданы папки: " + created, Theme.Ok);
                orderNum.Clear();
                orderName.Clear();
                orderNum.Focus();
            }
            catch (Exception e)
            {
                SetStatus("Не удалось создать папки: " + e.Message, Theme.Err);
            }
        }

        void OpenSelected()
        {
            string name = ordersBox.SelectedItem as string;
            if (name == null)
            {
                SetStatus("Выберите заявку из списка.", Theme.Err);
                return;
            }
            OpenOrder(name);
            SetStatus("Открыта заявка: " + name, Theme.Ok);
        }

        void OpenOrder(string name)
        {
            currentOrder = name;
            currentName.Text = name;
            currentBar.Visible = true;
            try { Files.EnsureSubfolders(Files.OrderPath(name)); } catch (Exception) { }
            BuildColumns();
            RefreshColumns();
        }

        void CloseOrder()
        {
            currentOrder = null;
            currentBar.Visible = false;
            colsPanel.Controls.Clear();
            columns.Clear();
        }

        bool RequireBase()
        {
            if (Files.HasBase) return true;
            SetStatus("Сначала выберите рабочую папку.", Theme.Err);
            PickFolder();
            return Files.HasBase;
        }

        // ------------------------------------------------------------ колонки ----
        void BuildColumns()
        {
            colsPanel.SuspendLayout();
            colsPanel.Controls.Clear();
            columns.Clear();
            foreach (string sf in Config.Subfolders)
            {
                ColumnPanel col = new ColumnPanel(sf);
                col.FilesDropped += OnFilesDropped;
                col.DeleteRequested += OnDeleteRequested;
                columns.Add(col);
                colsPanel.Controls.Add(col);
            }
            colsPanel.ResumeLayout();
            LayoutColumns();
        }

        void LayoutColumns()
        {
            if (columns.Count == 0) return;
            int gap = 10;
            int top = colsPanel.Padding.Top;
            int h = colsPanel.ClientSize.Height - top;
            int total = colsPanel.ClientSize.Width;
            int w = (total - gap * (columns.Count - 1)) / columns.Count;
            if (w < 150) w = 150;
            for (int i = 0; i < columns.Count; i++)
                columns[i].SetBounds(i * (w + gap), top, w, Math.Max(120, h));
        }

        void RefreshColumns()
        {
            if (currentOrder == null) return;
            foreach (ColumnPanel col in columns)
            {
                try { col.SetEntries(Files.List(currentOrder, col.Category)); }
                catch (Exception e) { SetStatus("Не удалось прочитать «" + col.Category + "»: " + e.Message, Theme.Err); }
            }
        }

        void OnFilesDropped(string category, string[] paths)
        {
            if (currentOrder == null)
            {
                SetStatus("Сначала откройте заявку.", Theme.Err);
                return;
            }
            if (busy)
            {
                SetStatus("Дождитесь окончания предыдущего копирования.", Theme.Err);
                return;
            }
            busy = true;
            Cursor = Cursors.WaitCursor;
            int done = 0, fail = 0;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    SetStatus(string.Format("Копирую {0} из {1} → «{2}»…", i + 1, paths.Length, category),
                              Theme.InkSoft);
                    statusLabel.Refresh();
                    try { done += Files.Copy(paths[i], currentOrder, category); }
                    catch (Exception) { fail++; }
                }
            }
            finally
            {
                busy = false;
                Cursor = Cursors.Default;
            }
            RefreshColumns();
            SetStatus(fail > 0
                ? string.Format("Скопировано файлов: {0}, не удалось: {1}.", done, fail)
                : string.Format("Скопировано файлов: {0} → «{1}». Оригиналы остались на месте.", done, category),
                fail > 0 ? Theme.Err : Theme.Ok);
        }

        void OnDeleteRequested(string category, Entry entry)
        {
            if (currentOrder == null) return;
            if (MessageBox.Show(this,
                    string.Format("Удалить «{0}» из папки «{1}»?", entry.Name, category),
                    "Проводник", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                Files.Delete(currentOrder, category, entry.Name, entry.IsDir);
                RefreshColumns();
                SetStatus("Удалено: " + entry.Name, Theme.Ok);
            }
            catch (Exception e)
            {
                SetStatus("Не удалось удалить: " + e.Message, Theme.Err);
            }
        }

        // ------------------------------------------------------------ прочее ----
        void SaveIp()
        {
            string value = ipInput.Text.Trim();
            if (!Util.IpRe.IsMatch(value))
            {
                ipMsg.Text = "Это не похоже на IP-адрес (пример: 192.168.3.81).";
                ipMsg.ForeColor = Theme.Err;
                return;
            }
            Config.LanIpOverride = value;
            Config.Save();
            ipMsg.Text = "Сохранено — этот адрес будет использоваться и дальше.";
            ipMsg.ForeColor = Theme.Ok;
            RefreshQr();
        }

        void AutoIp()
        {
            Config.LanIpOverride = "";
            Config.Save();
            ipMsg.Text = "Адрес снова определяется автоматически.";
            ipMsg.ForeColor = Theme.InkSoft;
            RefreshQr();
        }

        void EditSubfolders()
        {
            using (SubfoldersDialog dlg = new SubfoldersDialog(Config.Subfolders))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Config.Subfolders = dlg.Result;
                Config.Save();
                if (currentOrder != null) OpenOrder(currentOrder);
                SetStatus("Список подпапок сохранён.", Theme.Ok);
            }
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "1.  «Выбрать…» — укажите рабочую папку, где лежат папки заявок. Она запомнится.\n\n" +
                "2.  Номер и наименование → «Создать папки». Появится папка «номер - наименование»\n" +
                "     с подпапками и колонки под каждую из них.\n\n" +
                "3.  Перетащите файлы или целые папки из Проводника в нужную колонку — они\n" +
                "     скопируются, оригиналы останутся на месте. Совпадающие имена получают\n" +
                "     « (1)», « (2)» — ничего не затирается.\n\n" +
                "4.  К старой заявке вернитесь через «Существующие заявки». Заявка ищется по\n" +
                "     номеру — это часть имени папки до « - », наименование помнить не нужно.\n" +
                "     Клавиша Delete удаляет выбранный файл из папки заявки.\n\n" +
                "5.  «Приём с телефона»: наведите камеру на QR-код. Откроется страница отправки\n" +
                "     фото — файлы лягут прямо в папку заявки и сразу появятся в колонках.\n\n" +
                "Крестик прячет окно в значок рядом с часами, приём файлов продолжается.\n" +
                "Полностью закрыть — «Выключить сервер».",
                "Как пользоваться", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShutdownClicked()
        {
            if (MessageBox.Show(this,
                    "Выключить сервер и закрыть программу?\n\n" +
                    "Телефон больше не сможет отправлять файлы, пока вы не запустите её снова.",
                    "Проводник", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Shutdown();
        }

        /// <summary>Настоящее завершение — из окна или из меню значка.</summary>
        public void Shutdown()
        {
            State.FileReceived -= OnFileReceived;
            server.Stop();
            State.CleanupParts();               // недокачанные .part не оставляем
            Application.Exit();
        }

        /// <summary>Показать окно — из меню значка или при повторном запуске exe.</summary>
        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            RefreshFolder();
            RefreshOrders();
            RefreshQr();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;                // крестик прячет окно, сервер продолжает работать
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
