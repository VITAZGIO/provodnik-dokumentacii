// HTTP-сервер на «голом» TcpListener.
// Специально не HttpListener: тот работает через системную службу HTTP.SYS и требует прав
// администратора на регистрацию порта. TcpListener открывает порт как обычная программа —
// ровно так же, как это делал Python-прототип, без всяких прав.
//
// Две группы маршрутов:
//   /api/admin/... — рабочее место на этом ПК. Пускаем только с 127.0.0.1;
//   /m, /api/state, /api/upload — телефон. Пускаем только с одноразовым кодом в ссылке.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Provodnik
{
    class RecentItem
    {
        public string Time;
        public string Order;
        public string Cat;
        public string Name;
    }

    /// <summary>Общее состояние сервера.</summary>
    static class State
    {
        public static string Token = MakeToken();
        public static readonly LinkedList<RecentItem> Recent = new LinkedList<RecentItem>();
        public static readonly List<string> PartFiles = new List<string>();   // недокачанные — чистим при выходе

        /// <summary>Просьба открыть диалог выбора папки — её выполняет главный поток.</summary>
        public static volatile bool PickFolderRequested;

        /// <summary>Просьба выключить программу — со страницы в браузере.</summary>
        public static volatile bool ShutdownRequested;

        static string MakeToken()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            byte[] bytes = new byte[11];
            using (System.Security.Cryptography.RNGCryptoServiceProvider rng =
                   new System.Security.Cryptography.RNGCryptoServiceProvider())
                rng.GetBytes(bytes);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        public static void AddRecent(RecentItem item)
        {
            lock (Recent)
            {
                Recent.AddFirst(item);
                while (Recent.Count > 60) Recent.RemoveLast();
            }
        }

        public static void CleanupParts()
        {
            lock (PartFiles)
            {
                foreach (string p in PartFiles)
                {
                    try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
                    try
                    {
                        // рядом лежит пустышка, которой резервировали имя
                        string dest = p.Substring(0, p.Length - ".part".Length);
                        if (File.Exists(dest) && new FileInfo(dest).Length == 0) File.Delete(dest);
                    }
                    catch (Exception) { }
                }
                PartFiles.Clear();
            }
        }
    }

    class Server
    {
        const long MaxUpload = 500L * 1024 * 1024;

        readonly TcpListener listener;
        readonly Thread thread;
        volatile bool running = true;

        public int Port { get; private set; }

        Server(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>Занимает первый свободный порт из диапазона.</summary>
        public static Server Start(int firstPort, int tries)
        {
            for (int p = firstPort; p <= firstPort + tries; p++)
            {
                try { return new Server(p); }
                catch (SocketException) { }
            }
            throw new IOException(string.Format("Не удалось открыть порт {0}–{1}", firstPort, firstPort + tries));
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch (Exception) { }
        }

        void Loop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { break; }
                Thread t = new Thread(HandleSafe);
                t.IsBackground = true;
                t.Start(client);
            }
        }

        void HandleSafe(object state)
        {
            TcpClient client = (TcpClient)state;
            try { Handle(client); }
            catch (Exception) { }
            finally { try { client.Close(); } catch (Exception) { } }
        }

        // ------------------------------------------------------------ разбор ----
        void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 120000;
            client.SendTimeout = 120000;
            NetworkStream stream = client.GetStream();

            bool isLocal = IPAddress.IsLoopback(((IPEndPoint)client.Client.RemoteEndPoint).Address);

            byte[] head = new byte[16384];
            int headLen = 0, bodyStart = -1;
            while (headLen < head.Length)
            {
                int read = stream.Read(head, headLen, head.Length - headLen);
                if (read <= 0) return;
                headLen += read;
                for (int i = 3; i < headLen; i++)
                {
                    if (head[i - 3] == 13 && head[i - 2] == 10 && head[i - 1] == 13 && head[i] == 10)
                    {
                        bodyStart = i + 1;
                        break;
                    }
                }
                if (bodyStart >= 0) break;
            }
            if (bodyStart < 0) return;

            string headText = Encoding.UTF8.GetString(head, 0, bodyStart);
            string[] lines = headText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2) return;
            string method = parts[0];
            string target = parts[1];

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            string path = target;
            Dictionary<string, string> query = new Dictionary<string, string>();
            int qm = target.IndexOf('?');
            if (qm >= 0)
            {
                path = target.Substring(0, qm);
                foreach (string pair in target.Substring(qm + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string key = eq < 0 ? pair : pair.Substring(0, eq);
                    string value = eq < 0 ? "" : pair.Substring(eq + 1);
                    query[UrlDecode(key)] = UrlDecode(value);
                }
            }

            byte[] pending = new byte[headLen - bodyStart];       // тело, прочитанное вместе с заголовками
            Array.Copy(head, bodyStart, pending, 0, pending.Length);

            Route(stream, method, path, query, headers, pending, isLocal);
        }

        static string UrlDecode(string s)
        {
            return Uri.UnescapeDataString(s.Replace("+", " "));
        }

        // ---------------------------------------------------------- маршруты ----
        void Route(NetworkStream stream, string method, string path,
                   Dictionary<string, string> query, Dictionary<string, string> headers,
                   byte[] pending, bool isLocal)
        {
            // --- рабочее место на этом ПК ---
            if (path == "/" || path.StartsWith("/api/admin/"))
            {
                if (!isLocal) { Forbid(stream); return; }
                AdminRoute(stream, method, path, query, headers, pending);
                return;
            }

            // --- телефон ---
            bool tokenOk = query.ContainsKey("k") && query["k"] == State.Token;
            if (method == "GET" && path == "/m")
            {
                if (!tokenOk) { Forbid(stream); return; }
                Send(stream, 200, Pages.Mobile(), "text/html; charset=utf-8");
                return;
            }
            if (method == "GET" && path == "/api/state")
            {
                if (!tokenOk) { Forbid(stream); return; }
                SendJson(stream, 200, PhoneStateJson());
                return;
            }
            if (method == "POST" && path == "/api/upload")
            {
                if (!tokenOk) { Forbid(stream); return; }
                PhoneUpload(stream, query, headers, pending);
                return;
            }
            Send(stream, 404, "нет такой страницы", "text/html; charset=utf-8");
        }

        void AdminRoute(NetworkStream stream, string method, string path,
                        Dictionary<string, string> query, Dictionary<string, string> headers, byte[] pending)
        {
            if (method == "GET" && path == "/")
            {
                Send(stream, 200, Pages.Admin(), "text/html; charset=utf-8");
                return;
            }
            if (method == "GET" && path == "/api/admin/qr.png")
            {
                SendBytes(stream, 200, QrPng(), "image/png");
                return;
            }
            if (method == "GET" && path == "/api/admin/state")
            {
                SendJson(stream, 200, AdminStateJson());
                return;
            }
            if (method == "GET" && path == "/api/admin/files")
            {
                SendJson(stream, 200, FilesJson(Get(query, "order"), Get(query, "cat")));
                return;
            }
            if (method == "POST" && path == "/api/admin/choose")
            {
                State.PickFolderRequested = true;
                SendJson(stream, 202, "{\"status\":\"открываю выбор папки\"}");
                return;
            }
            if (method == "POST" && path == "/api/admin/shutdown")
            {
                State.ShutdownRequested = true;
                SendJson(stream, 200, "{\"status\":\"выключаюсь\"}");
                return;
            }
            if (method == "POST" && path == "/api/admin/ip")
            {
                string raw = Encoding.UTF8.GetString(ReadBody(stream, headers, pending)).Trim();
                if (raw.Length > 0 && !Util.IpRe.IsMatch(raw))
                {
                    SendJson(stream, 400, "{\"error\":\"не похоже на IP-адрес\"}");
                    return;
                }
                Config.LanIpOverride = raw;
                Config.Save();
                SendJson(stream, 200, "{\"lan_ip_override\":" + Util.JsonString(raw) + "}");
                return;
            }
            if (method == "POST" && path == "/api/admin/subfolders")
            {
                string raw = Encoding.UTF8.GetString(ReadBody(stream, headers, pending));
                List<string> subs = new List<string>();
                foreach (string line in raw.Split('\n'))
                {
                    string s = Util.Sanitize(line);
                    if (s.Length > 0 && !subs.Contains(s)) subs.Add(s);
                }
                if (subs.Count == 0)
                {
                    SendJson(stream, 400, "{\"error\":\"список подпапок пуст\"}");
                    return;
                }
                Config.Subfolders = subs;
                Config.Save();
                SendJson(stream, 200, "{\"subfolders\":" + Util.JsonArray(subs) + "}");
                return;
            }
            if (method == "POST" && path == "/api/admin/order")
            {
                CreateOrder(stream, query);
                return;
            }
            if (method == "POST" && path == "/api/admin/upload")
            {
                AdminUpload(stream, query, headers, pending);
                return;
            }
            if (method == "POST" && path == "/api/admin/delete")
            {
                DeleteEntry(stream, query);
                return;
            }
            Send(stream, 404, "нет такой страницы", "text/html; charset=utf-8");
        }

        // ------------------------------------------------------------ данные ----
        static string PhoneUrl()
        {
            return string.Format("http://{0}:{1}/m?k={2}", CurrentIp(), CurrentPort, State.Token);
        }

        static string CurrentIp()
        {
            return Config.LanIpOverride.Length > 0 ? Config.LanIpOverride : NetInfo.LanIp();
        }

        public static int CurrentPort;                 // ставится при запуске, нужен для ссылки телефона

        byte[] QrPng()
        {
            using (System.Drawing.Bitmap bmp = Qr.ToBitmap(PhoneUrl(), 260))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static List<string> OrderNames()
        {
            List<string> orders = new List<string>();
            if (Config.BaseDir.Length > 0 && Directory.Exists(Config.BaseDir))
            {
                try
                {
                    foreach (string d in Directory.GetDirectories(Config.BaseDir))
                        orders.Add(Path.GetFileName(d));
                }
                catch (Exception) { }
            }
            return orders;
        }

        string AdminStateJson()
        {
            List<string> ips = new List<string>();
            foreach (Adapter a in NetInfo.AllAdapters()) ips.Add(a.Ip + "  —  " + a.Name);

            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"folder\":").Append(Util.JsonString(Config.BaseDir)).Append(",");
            sb.Append("\"subfolders\":").Append(Util.JsonArray(Config.Subfolders)).Append(",");
            sb.Append("\"orders\":").Append(Util.JsonArray(OrderNames())).Append(",");
            sb.Append("\"phone_url\":").Append(Util.JsonString(PhoneUrl())).Append(",");
            sb.Append("\"ip\":").Append(Util.JsonString(CurrentIp())).Append(",");
            sb.Append("\"ip_override\":").Append(Util.JsonString(Config.LanIpOverride)).Append(",");
            sb.Append("\"adapters\":").Append(Util.JsonArray(ips)).Append(",");
            sb.Append("\"recent\":[");
            lock (State.Recent)
            {
                bool first = true;
                foreach (RecentItem it in State.Recent)
                {
                    if (!first) sb.Append(',');
                    sb.Append("{\"time\":").Append(Util.JsonString(it.Time));
                    sb.Append(",\"order\":").Append(Util.JsonString(it.Order));
                    sb.Append(",\"cat\":").Append(Util.JsonString(it.Cat));
                    sb.Append(",\"name\":").Append(Util.JsonString(it.Name)).Append('}');
                    first = false;
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        string PhoneStateJson()
        {
            List<string> orders = OrderNames();
            orders.Sort(StringComparer.Ordinal);
            orders.Reverse();
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"folder\":").Append(Util.JsonString(Config.BaseDir)).Append(",");
            sb.Append("\"subfolders\":").Append(Util.JsonArray(Config.Subfolders)).Append(",");
            sb.Append("\"limit_mb\":").Append(MaxUpload / 1024 / 1024).Append(",");
            sb.Append("\"orders\":").Append(Util.JsonArray(orders));
            return sb.Append("}").ToString();
        }

        string FilesJson(string order, string cat)
        {
            StringBuilder sb = new StringBuilder("{\"items\":[");
            string dir = SafeOrderPath(order);
            if (dir != null && cat.Length > 0) dir = Path.Combine(dir, Util.Sanitize(cat));
            if (dir != null && Directory.Exists(dir))
            {
                try
                {
                    List<string> entries = new List<string>();
                    foreach (string d in Directory.GetDirectories(dir)) entries.Add(Path.GetFileName(d) + "/");
                    foreach (string f in Directory.GetFiles(dir)) entries.Add(Path.GetFileName(f));
                    entries.Sort(StringComparer.OrdinalIgnoreCase);
                    bool first = true;
                    foreach (string e in entries)
                    {
                        if (!first) sb.Append(',');
                        bool isDir = e.EndsWith("/");
                        sb.Append("{\"name\":").Append(Util.JsonString(isDir ? e.Substring(0, e.Length - 1) : e));
                        sb.Append(",\"dir\":").Append(isDir ? "true" : "false").Append('}');
                        first = false;
                    }
                }
                catch (Exception) { }
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>Путь папки заявки. null, если имя пытается увести за пределы рабочей папки.</summary>
        static string SafeOrderPath(string order)
        {
            if (Config.BaseDir.Length == 0 || !Directory.Exists(Config.BaseDir)) return null;
            string name = Util.Sanitize(Path.GetFileName(order ?? ""));
            if (name.Length == 0) return null;
            return Path.Combine(Config.BaseDir, name);
        }

        // ------------------------------------------------------------ действия ----
        void CreateOrder(NetworkStream stream, Dictionary<string, string> query)
        {
            if (Config.BaseDir.Length == 0 || !Directory.Exists(Config.BaseDir))
            {
                SendJson(stream, 409, "{\"error\":\"не выбрана рабочая папка\"}");
                return;
            }
            string num = Util.Sanitize(Get(query, "num"));
            string name = Util.Sanitize(Get(query, "name"));
            if (num.Length == 0 || name.Length == 0)
            {
                SendJson(stream, 400, "{\"error\":\"нужны номер заявки и наименование\"}");
                return;
            }
            try
            {
                // если заявка с таким номером уже есть — открываем её, а не плодим вторую
                string dir = Util.FindOrderDir(Config.BaseDir, num);
                if (dir == null) dir = Path.Combine(Config.BaseDir, num + " - " + name);
                Directory.CreateDirectory(dir);
                foreach (string sf in Config.Subfolders)
                    Directory.CreateDirectory(Path.Combine(dir, Util.Sanitize(sf)));
                SendJson(stream, 200, "{\"name\":" + Util.JsonString(Path.GetFileName(dir)) + "}");
            }
            catch (Exception e)
            {
                SendJson(stream, 500, "{\"error\":" + Util.JsonString(e.Message) + "}");
            }
        }

        void DeleteEntry(NetworkStream stream, Dictionary<string, string> query)
        {
            string dir = SafeOrderPath(Get(query, "order"));
            if (dir == null)
            {
                SendJson(stream, 400, "{\"error\":\"не указана заявка\"}");
                return;
            }
            string target = ResolveInside(dir, Get(query, "path"));
            if (target == null)
            {
                SendJson(stream, 400, "{\"error\":\"недопустимый путь\"}");
                return;
            }
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                else if (File.Exists(target)) File.Delete(target);
                else
                {
                    SendJson(stream, 404, "{\"error\":\"уже удалено\"}");
                    return;
                }
                SendJson(stream, 200, "{\"deleted\":true}");
            }
            catch (Exception e)
            {
                SendJson(stream, 500, "{\"error\":" + Util.JsonString(e.Message) + "}");
            }
        }

        /// <summary>
        /// Склеивает путь из сегментов, чистя каждый. Возвращает null, если результат
        /// вылезает за пределы папки заявки: имя приходит снаружи, ему нельзя доверять.
        /// </summary>
        static string ResolveInside(string root, string relative)
        {
            if (relative == null) return null;
            string path = root;
            foreach (string seg in relative.Replace('\\', '/').Split('/'))
            {
                if (seg.Length == 0) continue;
                string s = Util.Sanitize(seg);
                if (s.Length == 0 || s == "." || s == "..") return null;
                path = Path.Combine(path, s);
            }
            if (path == root) return null;
            string full = Path.GetFullPath(path);
            string rootFull = Path.GetFullPath(root);
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            return full;
        }

        /// <summary>Файл из окна программы: путь внутри заявки задаёт страница.</summary>
        void AdminUpload(NetworkStream stream, Dictionary<string, string> query,
                         Dictionary<string, string> headers, byte[] pending)
        {
            string orderDir = SafeOrderPath(Get(query, "order"));
            if (orderDir == null)
            {
                UploadError(stream, 409, "{\"error\":\"не выбрана заявка\"}", headers, pending);
                return;
            }
            string dest = ResolveInside(orderDir, Get(query, "path"));
            if (dest == null)
            {
                UploadError(stream, 400, "{\"error\":\"недопустимое имя файла\"}", headers, pending);
                return;
            }
            SaveUpload(stream, headers, pending, Path.GetDirectoryName(dest), Path.GetFileName(dest),
                       Path.GetFileName(orderDir), NearestCat(orderDir, dest));
        }

        static string NearestCat(string orderDir, string dest)
        {
            string rel = dest.Substring(orderDir.Length).TrimStart(Path.DirectorySeparatorChar);
            int i = rel.IndexOf(Path.DirectorySeparatorChar);
            return i > 0 ? rel.Substring(0, i) : "";
        }

        /// <summary>Файл с телефона: заявку ищем по номеру, как в прежней программе.</summary>
        void PhoneUpload(NetworkStream stream, Dictionary<string, string> query,
                         Dictionary<string, string> headers, byte[] pending)
        {
            if (Config.BaseDir.Length == 0 || !Directory.Exists(Config.BaseDir))
            {
                UploadError(stream, 409, "{\"error\":\"на ПК не выбрана рабочая папка\"}", headers, pending);
                return;
            }
            string num = Util.Sanitize(Get(query, "num"));
            if (num.Length == 0)
            {
                UploadError(stream, 400, "{\"error\":\"не указан номер заявки\"}", headers, pending);
                return;
            }
            string name = Util.Sanitize(Get(query, "name"));
            string cat = Util.Sanitize(Get(query, "cat"));
            if (cat.Length == 0) cat = "Фото";
            string fn = Util.Sanitize(Path.GetFileName(Get(query, "fn")));
            if (fn.Length == 0) fn = "файл";

            string order = Util.FindOrderDir(Config.BaseDir, num);
            if (order == null)
                order = Path.Combine(Config.BaseDir, name.Length > 0 ? num + " - " + name : num);

            SaveUpload(stream, headers, pending, Path.Combine(order, cat), fn, Path.GetFileName(order), cat);
        }

        void SaveUpload(NetworkStream stream, Dictionary<string, string> headers, byte[] pending,
                        string folder, string fn, string orderName, string cat)
        {
            long length = 0;
            if (headers.ContainsKey("Content-Length")) long.TryParse(headers["Content-Length"], out length);
            if (length <= 0)
            {
                SendJson(stream, 400, "{\"error\":\"пустой файл\"}");
                return;
            }
            if (length > MaxUpload)
            {
                UploadError(stream, 413, string.Format(
                    "{{\"error\":\"файл больше {0} МБ\"}}", MaxUpload / 1024 / 1024), headers, pending);
                return;
            }

            string dest = null, part = null;
            try
            {
                Directory.CreateDirectory(folder);
                dest = Util.UniqueReserve(folder, fn);
                part = dest + ".part";
                lock (State.PartFiles) State.PartFiles.Add(part);

                using (FileStream f = new FileStream(part, FileMode.Create, FileAccess.Write))
                {
                    long remaining = length;
                    int take = (int)Math.Min(pending.Length, remaining);
                    if (take > 0)
                    {
                        f.Write(pending, 0, take);
                        remaining -= take;
                    }
                    byte[] buffer = new byte[65536];
                    while (remaining > 0)
                    {
                        int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) throw new IOException("соединение оборвалось");
                        f.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }
                if (File.Exists(dest)) File.Delete(dest);      // пустышка-резерв
                File.Move(part, dest);
                lock (State.PartFiles) State.PartFiles.Remove(part);
            }
            catch (Exception e)
            {
                try { if (part != null && File.Exists(part)) File.Delete(part); } catch (Exception) { }
                try { if (dest != null && File.Exists(dest)) File.Delete(dest); } catch (Exception) { }
                lock (State.PartFiles) State.PartFiles.Remove(part);
                SendJson(stream, 500, "{\"error\":" + Util.JsonString("не удалось сохранить: " + e.Message) + "}");
                return;
            }

            RecentItem item = new RecentItem();
            item.Time = DateTime.Now.ToString("HH:mm:ss");
            item.Order = orderName;
            item.Cat = cat;
            item.Name = Path.GetFileName(dest);
            State.AddRecent(item);

            SendJson(stream, 200, "{\"saved\":" + Util.JsonString(item.Name) +
                                  ",\"order\":" + Util.JsonString(item.Order) + "}");
        }

        /// <summary>
        /// Отказ в приёме файла. Перед ответом дочитываем и выбрасываем тело: если оборвать
        /// соединение, пока браузер ещё шлёт файл, он покажет «сеть отвалилась» вместо нашего
        /// объяснения — человек так и не узнает, что файл, например, слишком большой.
        /// Очень большие тела не тянем: там всё равно понятно, что ответ не дошёл.
        /// </summary>
        static void UploadError(NetworkStream stream, int code, string json,
                                Dictionary<string, string> headers, byte[] pending)
        {
            const long DrainCap = 64L * 1024 * 1024;
            long length = 0;
            if (headers.ContainsKey("Content-Length")) long.TryParse(headers["Content-Length"], out length);
            long remaining = length - pending.Length;
            if (remaining > 0 && length <= DrainCap)
            {
                byte[] buffer = new byte[65536];
                try
                {
                    while (remaining > 0)
                    {
                        int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) break;
                        remaining -= read;
                    }
                }
                catch (Exception) { }
            }
            SendJson(stream, code, json);
        }

        static byte[] ReadBody(NetworkStream stream, Dictionary<string, string> headers, byte[] pending)
        {
            long length = 0;
            if (headers.ContainsKey("Content-Length")) long.TryParse(headers["Content-Length"], out length);
            if (length <= 0) return new byte[0];
            if (length > 1024 * 1024) length = 1024 * 1024;
            byte[] body = new byte[length];
            int have = (int)Math.Min(pending.Length, length);
            Array.Copy(pending, body, have);
            while (have < length)
            {
                int read = stream.Read(body, have, (int)length - have);
                if (read <= 0) break;
                have += read;
            }
            return body;
        }

        static string Get(Dictionary<string, string> d, string key)
        {
            return d.ContainsKey(key) ? d[key] : "";
        }

        // ---------------------------------------------------------- ответы ----
        static void SendBytes(NetworkStream stream, int code, byte[] data, string contentType)
        {
            StringBuilder head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(code).Append(" ").Append(Reason(code)).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(data.Length).Append("\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        static void Send(NetworkStream stream, int code, string body, string contentType)
        {
            SendBytes(stream, code, Encoding.UTF8.GetBytes(body), contentType);
        }

        static void SendJson(NetworkStream stream, int code, string json)
        {
            Send(stream, code, json, "application/json; charset=utf-8");
        }

        static void Forbid(NetworkStream stream)
        {
            SendJson(stream, 403, "{\"error\":\"нет доступа\"}");
        }

        static string Reason(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 409: return "Conflict";
                case 413: return "Payload Too Large";
                default: return "Internal Server Error";
            }
        }
    }
}
