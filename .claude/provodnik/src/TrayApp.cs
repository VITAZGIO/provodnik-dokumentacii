// Значок рядом с часами: программа живёт здесь, пока открыта её страница в браузере.
// Своего окна у неё нет — рабочее место это страница, которую отдаёт сервер.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Provodnik
{
    class TrayApp : ApplicationContext
    {
        readonly Server server;
        readonly NotifyIcon tray;
        readonly Timer timer;

        public TrayApp(Server server)
        {
            this.server = server;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Открыть Проводник", null, delegate { OpenPage(); });
            menu.Items.Add("Сменить рабочую папку…", null, delegate { PickFolder(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выключить сервер", null, delegate { Shutdown(); });

            tray = new NotifyIcon();
            tray.Icon = LoadIcon();
            tray.Text = "Проводник документации";
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenPage(); };

            // Просьбы со страницы приходят в потоке сервера, а диалог выбора папки
            // умеет открываться только здесь, в главном потоке. Поэтому — флажок и опрос.
            timer = new Timer();
            timer.Interval = 250;
            timer.Tick += delegate { Poll(); };
            timer.Start();
        }

        static Icon LoadIcon()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                Icon icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        public void OpenPage()
        {
            try { Process.Start("http://127.0.0.1:" + server.Port + "/"); }
            catch (Exception e)
            {
                MessageBox.Show("Не удалось открыть страницу:\n\n" + e.Message +
                                "\n\nОткройте в браузере вручную:\nhttp://127.0.0.1:" + server.Port + "/",
                                "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Poll()
        {
            if (State.PickFolderRequested)
            {
                State.PickFolderRequested = false;
                PickFolder();
            }
            if (State.ShutdownRequested)
            {
                State.ShutdownRequested = false;
                Shutdown();
            }
        }

        void PickFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Где хранить папки заявок?";
                if (Directory.Exists(Config.BaseDir)) dlg.SelectedPath = Config.BaseDir;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                Config.BaseDir = dlg.SelectedPath;
                Config.Save();
            }
        }

        void Shutdown()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            server.Stop();
            State.CleanupParts();               // недокачанные .part не оставляем
            ExitThread();
        }
    }
}
