// Публичный доступ через интернет — «quick tunnel» Cloudflare.
// Запускаем cloudflared.exe дочерним процессом и вылавливаем из его вывода адрес
// вида https://что-то.trycloudflare.com. Процесс привязан к Job Object: если наша
// программа вылетит или её снимут через диспетчер задач, туннель гарантированно
// умрёт вместе с ней и не оставит компьютер открытым в интернет.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Provodnik
{
    static class Tunnel
    {
        static readonly Regex UrlRe = new Regex(@"https://[a-z0-9\-]+\.trycloudflare\.com");
        static Process process;
        static IntPtr job = IntPtr.Zero;
        static Timer watchdog;
        static string pendingUrl;
        static bool blockedPortSeen;

        // Cloudflare выдаёт адрес сразу, но канал до него поднимается позже — и если сеть
        // блокирует исходящий порт 7844, не поднимется вовсе. Показать QR по одному адресу
        // нельзя: человек отсканирует и получит ошибку 530 вместо страницы. Поэтому ждём
        // строку о реально установленном соединении.
        const int ReadyTimeoutMs = 45000;

        public static string Url { get; private set; }
        public static DateTime StartedAt { get; private set; }
        public static bool Running { get { return process != null && !process.HasExited; } }

        /// <summary>Адрес найден и туннель готов принимать телефон.</summary>
        public static event Action<string> Ready;
        public static event Action<string> Failed;

        public static void Start(int localPort)
        {
            Stop();
            Url = null;
            pendingUrl = null;
            blockedPortSeen = false;

            string exe;
            try { exe = EnsureBinary(); }
            catch (Exception e) { Fail("Не удалось подготовить cloudflared: " + e.Message); return; }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            // именно 127.0.0.1, а не localhost: localhost может разрешиться в IPv6-адрес ::1,
            // а наш сервер слушает только IPv4 — тогда Cloudflare отвечает ошибкой 530
            psi.Arguments = "tunnel --no-autoupdate --url http://127.0.0.1:" + localPort;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            try
            {
                process = Process.Start(psi);
            }
            catch (Exception e)
            {
                Fail("Не удалось запустить cloudflared: " + e.Message);
                return;
            }

            AttachToJob(process);
            StartedAt = DateTime.Now;
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;     // журнал cloudflared идёт именно сюда
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            watchdog = new Timer(OnTimeout, null, ReadyTimeoutMs, System.Threading.Timeout.Infinite);
        }

        static void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null || Url != null) return;

            if (pendingUrl == null)
            {
                Match m = UrlRe.Match(e.Data);
                if (m.Success) pendingUrl = m.Value;
            }
            if (e.Data.Contains("port 7844")) blockedPortSeen = true;

            // соединение с Cloudflare установлено — только теперь адресом можно пользоваться
            if (pendingUrl != null && e.Data.Contains("Registered tunnel connection"))
            {
                StopWatchdog();
                Url = pendingUrl;
                Action<string> h = Ready;
                if (h != null) h(Url);
            }
        }

        static void OnTimeout(object state)
        {
            StopWatchdog();
            if (Url != null) return;
            Stop();
            Fail(blockedPortSeen
                ? "Ваша сеть не пропускает соединение с Cloudflare (исходящий порт 7844).\n\n" +
                  "Обычно так делает корпоративная сеть, антивирус или VPN. Что можно попробовать:\n" +
                  "  • выключить VPN, если он включён;\n" +
                  "  • раздать интернет с телефона и подключить к нему компьютер;\n" +
                  "  • попросить админа разрешить исходящий порт 7844.\n\n" +
                  "Локальный (левый) QR-код при этом работает как обычно."
                : "Туннель не поднялся за " + ReadyTimeoutMs / 1000 + " секунд — нет связи с Cloudflare.\n\n" +
                  "Проверьте интернет на компьютере и попробуйте ещё раз.\n" +
                  "Локальный (левый) QR-код при этом работает как обычно.");
        }

        static void StopWatchdog()
        {
            Timer t = watchdog;
            watchdog = null;
            if (t != null) t.Dispose();
        }

        static void Fail(string message)
        {
            Action<string> h = Failed;
            if (h != null) h(message);
        }

        public static void Stop()
        {
            StopWatchdog();
            Url = null;
            pendingUrl = null;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    // cloudflared может не умереть с первого раза — тогда добиваем вместе
                    // с потомками: оставить его висеть нельзя, он держит канал в интернет
                    if (!process.WaitForExit(3000)) KillTree(process.Id);
                }
            }
            catch (Exception)
            {
                try { KillTree(process.Id); } catch (Exception) { }
            }
            try { process.Dispose(); } catch (Exception) { }
            process = null;
        }

        static void KillTree(int pid)
        {
            ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            if (p != null) p.WaitForExit(3000);
        }

        /// <summary>Достаёт cloudflared.exe: из вшитого ресурса, а если его нет — ищет рядом с программой.</summary>
        static string EnsureBinary()
        {
            string near = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "cloudflared.exe");
            if (File.Exists(near)) return near;

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream src = asm.GetManifestResourceStream("cloudflared.exe"))
            {
                if (src == null)
                    throw new FileNotFoundException(
                        "cloudflared.exe не вшит в программу и не найден рядом с ней");

                string dir = Path.Combine(Path.GetTempPath(), "provodnik");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "cloudflared.exe");
                if (File.Exists(target) && new FileInfo(target).Length == src.Length) return target;
                using (FileStream dst = new FileStream(target, FileMode.Create, FileAccess.Write))
                    src.CopyTo(dst);
                return target;
            }
        }

        // ------------------------------------------------- Job Object (WinAPI) ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr security, string name);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        static void AttachToJob(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int size = Marshal.SizeOf(info);
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, ptr, false);
                        SetInformationJobObject(job, 9 /* ExtendedLimitInformation */, ptr, (uint)size);
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch (Exception)
            {
                // не критично: туннель всё равно закрывается в Stop() при обычном выходе
            }
        }
    }
}
