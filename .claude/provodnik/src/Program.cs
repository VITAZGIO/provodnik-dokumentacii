// Точка входа.
//
// Программа — это локальный сервер плюс значок в трее. Рабочее место (создание заявок,
// раскладка файлов по колонкам, QR для телефона) — страница, которую сервер отдаёт на
// http://127.0.0.1:8765/ и которая открывается в браузере при запуске.
//
// Сервер слушает 0.0.0.0:8765 (если занят — следующий свободный до +10):
//   • страница и всё управление — только с этого ПК (127.0.0.1);
//   • страница телефона и приём файлов — по одноразовому коду в ссылке.
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Provodnik
{
    static class Program
    {
        const string MutexName = "Provodnik.Server.SingleInstance";
        const string ShowEventName = "Provodnik.Server.ShowWindow";

        static TrayApp app;

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
                    // Программа уже работает. Не ругаемся, а открываем её страницу —
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
                    Server.CurrentPort = server.Port;
                }
                catch (Exception e)
                {
                    MessageBox.Show("Не удалось запустить сервер:\n\n" + e.Message,
                                    "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                app = new TrayApp(server);
                StartShowListener();
                app.OpenPage();
                Application.Run(app);

                server.Stop();
                State.CleanupParts();
            }
        }

        /// <summary>Ждёт сигнала от второй копии и открывает страницу.</summary>
        static void StartShowListener()
        {
            EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    handle.WaitOne();
                    try { app.OpenPage(); }
                    catch (Exception) { return; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
