// Значок рядом с часами. Крестик прячет окно сюда — приём файлов продолжается.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Provodnik
{
    class TrayApp : ApplicationContext
    {
        readonly MainForm form;
        readonly NotifyIcon tray;

        public TrayApp(MainForm form)
        {
            this.form = form;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Открыть окно", null, delegate { form.ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выключить сервер", null, delegate { form.Shutdown(); });

            tray = new NotifyIcon();
            tray.Icon = form.Icon ?? SystemIcons.Application;
            tray.Text = "Проводник документации";
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { form.ShowFromTray(); };

            form.VisibleChanged += delegate
            {
                // подсказываем, куда делось окно, — но только один раз
                if (!form.Visible && !hinted)
                {
                    hinted = true;
                    tray.ShowBalloonTip(3000, "Проводник работает",
                        "Окно свёрнуто рядом с часами. Приём файлов с телефона продолжается.",
                        ToolTipIcon.Info);
                }
            };

            Application.ApplicationExit += delegate
            {
                tray.Visible = false;
                tray.Dispose();
            };

            form.Show();
        }

        bool hinted;
    }
}
