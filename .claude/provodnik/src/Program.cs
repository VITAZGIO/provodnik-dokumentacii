// Точка входа.
//
// Обычная сборка поднимает два сервера:
//   • lan     — 0.0.0.0:8765 (или следующий свободный до +10) для телефона по Wi-Fi, лимит 500 МБ
//   • tunnel  — 127.0.0.1:случайный порт, только для cloudflared, лимит 100 МБ
// Разные двери нужны не для красоты: cloudflared работает на этом же ПК и все запросы из
// интернета приходят к нам с адреса 127.0.0.1, так что «свой/чужой» по адресу не отличить.
// Поэтому лимит и доступ определяются тем, в какую дверь постучались, — подделать нельзя.
//
// Сборка LOCAL_ONLY (build-lokalnyy.bat) — без cloudflared, только первый сервер.
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace Provodnik
{
    static class Program
    {
        // По этим именам вторая копия находит первую. Имя одно на обе сборки: обычная
        // и «pro» — это один и тот же сервер на одном порту, вместе им не ужиться.
        const string MutexName = "Provodnik.Server.SingleInstance";
        const string ShowEventName = "Provodnik.Server.ShowWindow";

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
                    // Программа уже работает. Не ругаемся, а просто показываем её окно —
                    // человек запустил ярлык именно затем, чтобы её увидеть.
                    try
                    {
                        EventWaitHandle.OpenExisting(ShowEventName).Set();
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Программа уже запущена — посмотрите значок рядом с часами.",
                                        "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Config.Load();
                if (Config.BaseDir.Length == 0 || !Directory.Exists(Config.BaseDir))
                {
                    if (!AskFolder()) return;
                }

                Server lan;
#if !LOCAL_ONLY
                Server tunnel;
#endif
                try
                {
                    lan = Server.Start(IPAddress.Any, 8765, 10, 500);
#if !LOCAL_ONLY
                    tunnel = Server.Start(IPAddress.Loopback, 0, 0, 100);
#endif
                }
                catch (Exception e)
                {
                    MessageBox.Show("Не удалось запустить сервер:\n\n" + e.Message,
                                    "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

#if LOCAL_ONLY
                form = new MainForm(lan);
#else
                form = new MainForm(lan, tunnel);
#endif
                StartShowListener();
                Application.Run(form);

#if !LOCAL_ONLY
                Tunnel.Stop();
                tunnel.Stop();
#endif
                lan.Stop();
                State.CleanupParts();
            }
        }

        /// <summary>Ждёт сигнала от второй копии и показывает окно.</summary>
        static void StartShowListener()
        {
            EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    handle.WaitOne();
                    try
                    {
                        if (form.IsDisposed) return;
                        form.BeginInvoke((Action)form.ShowFromTray);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool AskFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Где хранить папки заявок?";
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                Config.BaseDir = dlg.SelectedPath;
                Config.Save();
                return true;
            }
        }
    }
}
