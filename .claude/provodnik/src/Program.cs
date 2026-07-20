// Точка входа.
//
// Программа — окно с папками заявок плюс маленький сервер для телефона.
// Сервер слушает 8765 (если занят — следующий свободный до +10) на всех интерфейсах:
// страница телефона и приём файлов отдаются только по одноразовому коду в ссылке.
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Provodnik
{
    static class Program
    {
        const string MutexName = "Provodnik.SingleInstance";
        const string ShowEventName = "Provodnik.ShowWindow";

        static MainForm form;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Программа уже работает. Не ругаемся, а показываем её окно —
                    // человек запустил ярлык именно затем, чтобы её увидеть.
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                    catch (Exception)
                    {
                        MessageBox.Show("Программа уже запущена — посмотрите значок рядом с часами.",
                                        "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Config.Load();

                Server server;
                try
                {
                    server = Server.Start(8765, 10);
                }
                catch (Exception e)
                {
                    MessageBox.Show("Не удалось запустить приём файлов с телефона:\n\n" + e.Message,
                                    "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                form = new MainForm(server);
                StartShowListener();
                Application.Run(new TrayApp(form));

                server.Stop();
                State.CleanupParts();
            }
        }

        /// <summary>
        /// Ждёт сигнала от второй копии. Само окно трогать отсюда нельзя — это чужой поток,
        /// поэтому просто поднимаем флажок, а окно подхватит его своим таймером.
        /// </summary>
        static void StartShowListener()
        {
            EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    handle.WaitOne();
                    State.ShowWindowRequested = true;
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
