// Главное окно: QR-код(ы), выбор адреса, рабочая папка, список принятых файлов.
// Браузер на ПК не нужен — всё здесь. Веб-страница осталась только для телефона.
//
// Собирается в двух видах (см. build.bat и build-lokalnyy.bat):
//   обычный          — два QR: локальный и публичный через интернет;
//   LOCAL_ONLY       — только локальный QR, без cloudflared. Нужен там, где сеть всё
//                      равно блокирует туннель (порт 7844) — чтобы не смущать кнопкой,
//                      которая не может сработать, и не таскать 50 МБ cloudflared.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Provodnik
{
    class MainForm : Form
    {
        // --- размеры окна: без публичного QR правая колонка съезжает влево ---
#if LOCAL_ONLY
        const int FormW = 666;
        const int RightX = 266;                // левый край колонки с пояснениями
#else
        const int FormW = 920;
        const int RightX = 520;
        const int TunnelMinutes = 60;          // публичный доступ живёт час и гаснет сам
#endif
        const int PanelW = FormW - 24;         // ширина карточки
        const int RightW = PanelW - RightX - 12;

        readonly Server lanServer;

        PictureBox picLocal;
        Label lblLocalUrl, lblFolder, lblIpMsg;
        ComboBox cmbIp;
        ListBox lstRecent;
        NotifyIcon tray;
        Timer timer;
        bool reallyExit;

#if !LOCAL_ONLY
        readonly Server tunnelServer;
        PictureBox picPublic;
        Label lblPublicUrl, lblPublicTimer;
        Button btnPublic, btnPublicOff;

        public MainForm(Server lanServer, Server tunnelServer)
        {
            this.tunnelServer = tunnelServer;
#else
        public MainForm(Server lanServer)
        {
#endif
            this.lanServer = lanServer;

            Text = "Проводник · сервер связи с телефоном";
            ClientSize = new Size(FormW, 720);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Enclosure;
            Font = Theme.Sans;

            BuildUi();
            BuildTray();

            State.Changed += OnStateChanged;
#if !LOCAL_ONLY
            Tunnel.Ready += OnTunnelReady;
            Tunnel.Failed += OnTunnelFailed;

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
            timer.Start();
#endif
            RefreshLocal();
            RefreshRecent();
            RefreshFolder();
        }

        // ------------------------------------------------------------ вёрстка ----
        void BuildUi()
        {
            Label brand = new Label();
            brand.Text = "ПРОВОДНИК ДОКУМЕНТАЦИИ · СЕРВЕР СВЯЗИ С ТЕЛЕФОНОМ";
            brand.Font = Theme.Head;
            brand.ForeColor = Theme.Ink;
            brand.SetBounds(14, 10, 600, 16);
            Controls.Add(brand);

            Panel conn = Card("ПОДКЛЮЧЕНИЕ ТЕЛЕФОНА", 12, 32, PanelW, 348);

            // --- левый QR: локальная сеть ---
#if LOCAL_ONLY
            conn.Controls.Add(Caption("Наведите камеру телефона на этот код", 12, 26, 250));
#else
            conn.Controls.Add(Caption("Быстрый способ — телефон в той же Wi-Fi сети", 12, 26, 250));
#endif
            picLocal = Frame(conn, 12, 60, 240);
            lblLocalUrl = new Label();
            lblLocalUrl.Font = new Font(Theme.Mono.FontFamily, 8f);
            lblLocalUrl.ForeColor = Theme.InkSoft;
            lblLocalUrl.SetBounds(12, 304, 240, 32);
            conn.Controls.Add(lblLocalUrl);

#if !LOCAL_ONLY
            // --- правый QR: интернет ---
            conn.Controls.Add(Caption("Универсальный способ — через интернет", 266, 26, 250));
            picPublic = Frame(conn, 266, 60, 240);

            btnPublic = new Button();
            btnPublic.Text = "Публичный QR-код\n(если телефон в другой сети)";
            btnPublic.SetBounds(276, 150, 220, 60);
            btnPublic.BackColor = Theme.Tag;
            btnPublic.ForeColor = Theme.TagInk;
            btnPublic.FlatStyle = FlatStyle.Flat;
            btnPublic.Font = new Font(Theme.Sans.FontFamily, 9f, FontStyle.Bold);
            btnPublic.Click += delegate { PublicClicked(); };
            conn.Controls.Add(btnPublic);
            btnPublic.BringToFront();

            lblPublicUrl = new Label();
            lblPublicUrl.Font = new Font(Theme.Mono.FontFamily, 8f);
            lblPublicUrl.ForeColor = Theme.InkSoft;
            lblPublicUrl.SetBounds(266, 304, 240, 32);
            conn.Controls.Add(lblPublicUrl);

            lblPublicTimer = new Label();
            lblPublicTimer.Font = new Font(Theme.Mono.FontFamily, 8.5f, FontStyle.Bold);
            lblPublicTimer.ForeColor = Theme.Err;
            lblPublicTimer.SetBounds(RightX, 310, 220, 18);
            lblPublicTimer.Visible = false;
            conn.Controls.Add(lblPublicTimer);

            btnPublicOff = new Button();
            btnPublicOff.Text = "Выключить публичный доступ";
            btnPublicOff.SetBounds(PanelW - 148, 304, 136, 26);
            btnPublicOff.Visible = false;
            btnPublicOff.Click += delegate { StopTunnel(); };
            conn.Controls.Add(btnPublicOff);
#endif

            // --- правая колонка: пояснения и адрес ---
            Label steps = new Label();
            steps.Text =
                "1.  Откройте на телефоне камеру и наведите на QR-код.\n" +
                "2.  Перейдите по ссылке — откроется страница отправки фото.\n" +
                "3.  Номер заявки, категория, «Снять фото» → «Отправить».\n\n" +
#if LOCAL_ONLY
                "Телефон должен быть в той же Wi-Fi сети, что и этот\n" +
                "компьютер.\n\n" +
#else
                "Сначала пробуйте левый QR. Если страница не открывается —\n" +
                "телефон и компьютер в разных сетях, тогда правый.\n\n" +
#endif
                "Код в ссылке — защита: без него загрузка невозможна.\n" +
                "При перезапуске программы код меняется.";
            steps.SetBounds(RightX, 60, RightW, 152);
            conn.Controls.Add(steps);

            Label ipCap = Caption("Адрес этого компьютера в локальной сети", RightX, 214, RightW);
            ipCap.Height = 16;
            conn.Controls.Add(ipCap);

            cmbIp = new ComboBox();
            cmbIp.Font = Theme.Mono;
            cmbIp.SetBounds(RightX, 232, 230, 24);
            cmbIp.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (Adapter a in NetInfo.AllAdapters()) cmbIp.Items.Add(a);
            cmbIp.Text = Config.LanIpOverride.Length > 0 ? Config.LanIpOverride : NetInfo.LanIp();
            conn.Controls.Add(cmbIp);

            Button btnSaveIp = new Button();
            btnSaveIp.Text = "Сохранить";
            btnSaveIp.SetBounds(RightX + 236, 231, 78, 26);
            btnSaveIp.Click += delegate { SaveIp(); };
            conn.Controls.Add(btnSaveIp);

            Button btnAutoIp = new Button();
            btnAutoIp.Text = "Авто";
            btnAutoIp.SetBounds(RightX + 320, 231, 44, 26);
            btnAutoIp.Click += delegate { AutoIp(); };
            conn.Controls.Add(btnAutoIp);

            lblIpMsg = new Label();
            lblIpMsg.ForeColor = Theme.InkSoft;
            lblIpMsg.SetBounds(RightX, 262, RightW, 42);
            lblIpMsg.Text = "Если сетей несколько (Docker, VPN, ВМ) —\n" +
                            "выберите из списка адрес, который видит телефон.";
            conn.Controls.Add(lblIpMsg);

            // --- рабочая папка ---
            Panel folder = Card("РАБОЧАЯ ПАПКА", 12, 388, PanelW, 74);

            lblFolder = new Label();
            lblFolder.Font = Theme.Mono;
            lblFolder.AutoEllipsis = true;
            lblFolder.SetBounds(12, 28, PanelW - 172, 20);
            folder.Controls.Add(lblFolder);

            Button btnFolder = new Button();
            btnFolder.Text = "Сменить папку…";
            btnFolder.SetBounds(PanelW - 148, 24, 136, 28);
            btnFolder.Click += delegate { ChooseFolder(); };
            folder.Controls.Add(btnFolder);

            folder.Controls.Add(Caption("Сюда складываются файлы — в те же папки заявок, " +
                                        "с которыми работает программа на ПК.", 12, 52, PanelW - 24));

            // --- принятые файлы ---
            Panel recent = Card("ПРИНЯТЫЕ ФАЙЛЫ", 12, 470, PanelW, 194);

            lstRecent = new ListBox();
            lstRecent.Font = Theme.Mono;
            lstRecent.SetBounds(12, 26, PanelW - 24, 156);
            lstRecent.BorderStyle = BorderStyle.None;
            lstRecent.BackColor = Theme.Panel2;
            recent.Controls.Add(lstRecent);

            // --- выключение ---
            Label hint = new Label();
            hint.Text = "Крестик прячет окно в трей — приём файлов продолжается.";
            hint.ForeColor = Theme.InkSoft;
            hint.SetBounds(14, 680, 400, 32);
            Controls.Add(hint);

            Button btnShutdown = new Button();
            btnShutdown.Text = "Выключить сервер";
            btnShutdown.SetBounds(FormW - 12 - 170, 676, 170, 32);
            btnShutdown.FlatStyle = FlatStyle.Flat;
            btnShutdown.ForeColor = Theme.Err;
            btnShutdown.Font = new Font(Theme.Sans.FontFamily, 9f, FontStyle.Bold);
            btnShutdown.Click += delegate { ShutdownClicked(); };
            Controls.Add(btnShutdown);
        }

        void ShutdownClicked()
        {
            if (MessageBox.Show(this,
                    "Выключить сервер и закрыть программу?\n\n" +
                    "Телефон больше не сможет отправлять файлы, пока вы не запустите её снова.",
                    "Проводник", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            ExitApp();
        }

        Panel Card(string title, int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.SetBounds(x, y, w, h);
            p.BackColor = Theme.Panel;
            p.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(p);

            Label head = new Label();
            head.Text = title;
            head.Font = Theme.Head;
            head.ForeColor = Theme.InkSoft;
            head.SetBounds(12, 8, 400, 14);
            p.Controls.Add(head);
            return p;
        }

        Label Caption(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.InkSoft;
            l.SetBounds(x, y, w, 32);
            return l;
        }

        PictureBox Frame(Panel parent, int x, int y, int size)
        {
            Panel border = new Panel();
            border.SetBounds(x, y, size, size);
            border.BackColor = Theme.Tag;
            parent.Controls.Add(border);

            PictureBox pic = new PictureBox();
            pic.SetBounds(3, 3, size - 6, size - 6);
            pic.BackColor = Color.White;
            pic.SizeMode = PictureBoxSizeMode.CenterImage;
            border.Controls.Add(pic);
            return pic;
        }

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Открыть окно", null, delegate { ShowWindow(); });
            menu.Items.Add("Сменить папку…", null, delegate { ShowWindow(); ChooseFolder(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitApp(); });

            tray = new NotifyIcon();
            tray.Icon = Icon ?? System.Drawing.SystemIcons.Application;
            tray.Text = "Проводник · сервер";
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowWindow(); };
        }

        // ------------------------------------------------------------ действия ----
        string CurrentIp()
        {
            return Config.LanIpOverride.Length > 0 ? Config.LanIpOverride : NetInfo.LanIp();
        }

        void RefreshLocal()
        {
            string url = string.Format("http://{0}:{1}/m?k={2}", CurrentIp(), lanServer.Port, State.Token);
            lblLocalUrl.Text = url;
            try { picLocal.Image = Qr.ToBitmap(url, 230); }
            catch (Exception e) { lblLocalUrl.Text = "QR не построился: " + e.Message; }
        }

        void SaveIp()
        {
            Adapter selected = cmbIp.SelectedItem as Adapter;
            string value = selected != null ? selected.Ip : cmbIp.Text.Trim();
            if (!Util.IpRe.IsMatch(value))
            {
                lblIpMsg.ForeColor = Theme.Err;
                lblIpMsg.Text = "Это не похоже на IP-адрес (пример: 192.168.3.81).";
                return;
            }
            Config.LanIpOverride = value;
            Config.Save();
            cmbIp.Text = value;
            lblIpMsg.ForeColor = Theme.Ok;
            lblIpMsg.Text = "Сохранено. Этот адрес будет использоваться и при следующих запусках.";
            RefreshLocal();
        }

        void AutoIp()
        {
            Config.LanIpOverride = "";
            Config.Save();
            cmbIp.SelectedIndex = -1;
            cmbIp.Text = NetInfo.LanIp();
            lblIpMsg.ForeColor = Theme.InkSoft;
            lblIpMsg.Text = "Адрес снова определяется автоматически.";
            RefreshLocal();
        }

#if !LOCAL_ONLY
        void PublicClicked()
        {
            using (ConfirmDialog dlg = new ConfirmDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            btnPublic.Enabled = false;
            btnPublic.Text = "Подключаюсь к Cloudflare…\n(до минуты)";
            lblPublicUrl.Text = "QR появится, когда связь установится";
            Tunnel.Start(tunnelServer.Port);
        }

        void OnTunnelReady(string url)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)OnTunnelReady, url); return; }
            string full = url + "/m?k=" + State.Token;
            lblPublicUrl.Text = full;
            try { picPublic.Image = Qr.ToBitmap(full, 230); }
            catch (Exception e) { lblPublicUrl.Text = "QR не построился: " + e.Message; }
            btnPublic.Visible = false;
            btnPublicOff.Visible = true;
            lblPublicTimer.Visible = true;
        }

        void OnTunnelFailed(string message)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)OnTunnelFailed, message); return; }
            ResetPublicButton();
            MessageBox.Show(this, message, "Публичный доступ не включился",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void StopTunnel()
        {
            Tunnel.Stop();
            picPublic.Image = null;
            lblPublicTimer.Visible = false;
            btnPublicOff.Visible = false;
            ResetPublicButton();
        }

        void ResetPublicButton()
        {
            lblPublicUrl.Text = "";
            btnPublic.Visible = true;
            btnPublic.Enabled = true;
            btnPublic.Text = "Публичный QR-код\n(если телефон в другой сети)";
        }

        void Tick()
        {
            if (!Tunnel.Running || Tunnel.Url == null) return;
            TimeSpan left = TimeSpan.FromMinutes(TunnelMinutes) - (DateTime.Now - Tunnel.StartedAt);
            if (left <= TimeSpan.Zero)
            {
                StopTunnel();
                tray.ShowBalloonTip(5000, "Проводник",
                    "Публичный доступ выключен — прошёл час.", ToolTipIcon.Info);
                return;
            }
            lblPublicTimer.Text = string.Format("выключится через {0:mm\\:ss}", left);
        }
#endif

        void ChooseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Где хранить папки заявок?";
                dlg.SelectedPath = Config.BaseDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Config.BaseDir = dlg.SelectedPath;
                Config.Save();
                RefreshFolder();
            }
        }

        void RefreshFolder()
        {
            lblFolder.Text = Config.BaseDir.Length > 0 ? Config.BaseDir : "— не выбрана —";
        }

        void OnStateChanged()
        {
            if (InvokeRequired) { BeginInvoke((Action)OnStateChanged); return; }
            RefreshRecent();
        }

        void RefreshRecent()
        {
            lstRecent.BeginUpdate();
            lstRecent.Items.Clear();
            lock (State.Recent)
            {
                foreach (RecentItem it in State.Recent)
                    lstRecent.Items.Add(string.Format("{0}   {1} / {2} / {3}",
                                                      it.Time, it.Order, it.Cat, it.Name));
            }
            if (lstRecent.Items.Count == 0) lstRecent.Items.Add("пока пусто — ждём фото с телефона");
            lstRecent.EndUpdate();
        }

        // ------------------------------------------------------------ окно/трей ----
        /// <summary>Показать окно — из меню значка или когда exe запустили второй раз.</summary>
        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        void ShowWindow()
        {
            ShowFromTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;                       // крестик прячет окно, сервер продолжает работать
                Hide();
                tray.ShowBalloonTip(3000, "Проводник · сервер работает",
                    "Программа свёрнута рядом с часами. Выход — правой кнопкой по значку.",
                    ToolTipIcon.Info);
                return;
            }
            base.OnFormClosing(e);
        }

        void ExitApp()
        {
            reallyExit = true;
#if !LOCAL_ONLY
            Tunnel.Stop();
            tunnelServer.Stop();
#endif
            lanServer.Stop();
            State.CleanupParts();                      // недокачанные .part не оставляем
            tray.Visible = false;
            Application.Exit();
        }
    }
}
