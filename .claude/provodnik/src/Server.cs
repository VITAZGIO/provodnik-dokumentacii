// HTTP-сервер на «голом» TcpListener.
// Специально не HttpListener: тот работает через системную службу HTTP.SYS и требует прав
// администратора на регистрацию порта. TcpListener открывает порт как обычная программа —
// ровно так же, как это делал Python-прототип, без всяких прав.
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
        public static event Action Changed;

        public static void NotifyChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }

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
            NotifyChanged();
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
        readonly TcpListener listener;
        readonly long maxUpload;
        readonly Thread thread;
        volatile bool running = true;

        public int Port { get; private set; }

        /// <param name="address">0.0.0.0 — для локальной сети, 127.0.0.1 — для туннеля</param>
        /// <param name="maxUploadMb">лимит на файл: 500 по локальной сети, 100 через туннель</param>
        Server(IPAddress address, int port, int maxUploadMb)
        {
            listener = new TcpListener(address, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            maxUpload = (long)maxUploadMb * 1024 * 1024;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>Занимает первый свободный порт из диапазона; 0 — любой свободный.</summary>
        public static Server Start(IPAddress address, int firstPort, int tries, int maxUploadMb)
        {
            for (int p = firstPort; p <= firstPort + tries; p++)
            {
                try { return new Server(address, p, maxUploadMb); }
                catch (SocketException) { if (firstPort == 0) throw; }
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

            // тело, уже прочитанное вместе с заголовками
            byte[] pending = new byte[headLen - bodyStart];
            Array.Copy(head, bodyStart, pending, 0, pending.Length);

            Route(stream, method, path, query, headers, pending);
        }

        static string UrlDecode(string s)
        {
            return Uri.UnescapeDataString(s.Replace("+", " "));
        }

        // ---------------------------------------------------------- маршруты ----
        void Route(NetworkStream stream, string method, string path,
                   Dictionary<string, string> query, Dictionary<string, string> headers, byte[] pending)
        {
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
                SendJson(stream, 200, StateJson());
                return;
            }
            if (method == "POST" && path == "/api/upload")
            {
                if (!tokenOk) { Forbid(stream); return; }
                Upload(stream, query, headers, pending);
                return;
            }
            Send(stream, 404, "нет такой страницы", "text/html; charset=utf-8");
        }

        string StateJson()
        {
            List<string> orders = new List<string>();
            if (!string.IsNullOrEmpty(Config.BaseDir) && Directory.Exists(Config.BaseDir))
            {
                try
                {
                    foreach (string d in Directory.GetDirectories(Config.BaseDir))
                        orders.Add(Path.GetFileName(d));
                    orders.Sort(StringComparer.Ordinal);
                    orders.Reverse();
                }
                catch (Exception) { }
            }
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"folder\":").Append(Util.JsonString(Config.BaseDir)).Append(",");
            sb.Append("\"subfolders\":").Append(Util.JsonArray(Config.Subfolders)).Append(",");
            sb.Append("\"limit_mb\":").Append(maxUpload / 1024 / 1024).Append(",");
            sb.Append("\"orders\":").Append(Util.JsonArray(orders));
            return sb.Append("}").ToString();
        }

        // ------------------------------------------------------- приём файла ----
        void Upload(NetworkStream stream, Dictionary<string, string> query,
                    Dictionary<string, string> headers, byte[] pending)
        {
            if (string.IsNullOrEmpty(Config.BaseDir) || !Directory.Exists(Config.BaseDir))
            {
                SendJson(stream, 409, "{\"error\":\"на ПК не выбрана рабочая папка\"}");
                return;
            }

            string num = Util.Sanitize(Get(query, "num"));
            if (num.Length == 0)
            {
                SendJson(stream, 400, "{\"error\":\"не указан номер заявки\"}");
                return;
            }
            string name = Util.Sanitize(Get(query, "name"));
            string cat = Util.Sanitize(Get(query, "cat"));
            if (cat.Length == 0) cat = "Фото";
            string fn = Util.Sanitize(Path.GetFileName(Get(query, "fn")));
            if (fn.Length == 0) fn = "файл";

            long length = 0;
            if (headers.ContainsKey("Content-Length")) long.TryParse(headers["Content-Length"], out length);
            if (length <= 0)
            {
                SendJson(stream, 400, "{\"error\":\"пустой файл\"}");
                return;
            }
            if (length > maxUpload)
            {
                SendJson(stream, 413, string.Format(
                    "{{\"error\":\"файл больше {0} МБ\"}}", maxUpload / 1024 / 1024));
                return;
            }

            string order = Util.FindOrderDir(Config.BaseDir, num);
            if (order == null)
                order = Path.Combine(Config.BaseDir, name.Length > 0 ? num + " - " + name : num);
            string folder = Path.Combine(order, cat);

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
            item.Order = Path.GetFileName(order);
            item.Cat = cat;
            item.Name = Path.GetFileName(dest);
            State.AddRecent(item);

            SendJson(stream, 200, "{\"saved\":" + Util.JsonString(item.Name) +
                                  ",\"order\":" + Util.JsonString(item.Order) + "}");
        }

        static string Get(Dictionary<string, string> d, string key)
        {
            return d.ContainsKey(key) ? d[key] : "";
        }

        // ---------------------------------------------------------- ответы ----
        static void Send(NetworkStream stream, int code, string body, string contentType)
        {
            byte[] data = Encoding.UTF8.GetBytes(body);
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
