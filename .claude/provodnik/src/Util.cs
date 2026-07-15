// Проводник документации — вспомогательные функции.
// Перенос из provodnik_server.py: sanitize / order_token / unique_reserve.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Provodnik
{
    static class Util
    {
        static readonly Regex BadChars = new Regex("[<>:\"/\\\\|?*]");
        static readonly Regex Spaces = new Regex("\\s+");
        static readonly object NameLock = new object();

        public static readonly Regex IpRe = new Regex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$");

        /// <summary>Убирает из имени символы, недопустимые в именах файлов Windows.</summary>
        public static string Sanitize(string s)
        {
            if (s == null) return "";
            s = BadChars.Replace(s, " ");
            s = Spaces.Replace(s, " ").Trim().TrimEnd('.');
            return s;
        }

        /// <summary>Номер заявки — часть имени папки до « - ».</summary>
        public static string OrderToken(string name)
        {
            if (name == null) return "";
            name = name.Trim();
            int i = name.IndexOf(" - ", StringComparison.Ordinal);
            return (i > 0 ? name.Substring(0, i) : name).Trim();
        }

        /// <summary>Существующая папка заявки с таким номером (наименование не важно).</summary>
        public static string FindOrderDir(string baseDir, string num)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return null;
            num = Sanitize(num);
            string[] dirs = Directory.GetDirectories(baseDir);
            Array.Sort(dirs, StringComparer.Ordinal);
            foreach (string d in dirs)
            {
                if (OrderToken(Path.GetFileName(d)) == num) return d;
            }
            return null;
        }

        /// <summary>
        /// Подбирает свободное имя («файл (1).jpg») и сразу занимает его пустым файлом,
        /// чтобы два одновременных запроса не выбрали одно и то же имя.
        /// </summary>
        public static string UniqueReserve(string folder, string filename)
        {
            string ext = Path.GetExtension(filename);
            string stem = Path.GetFileNameWithoutExtension(filename);
            lock (NameLock)
            {
                string dest = Path.Combine(folder, filename);
                int i = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(folder, string.Format("{0} ({1}){2}", stem, i, ext));
                    i++;
                }
                using (File.Create(dest)) { }
                return dest;
            }
        }

        // ---------------------------------------------------------------- JSON --
        // Мини-сериализатор: конфиг и ответы сервера маленькие, тянуть библиотеку незачем.

        public static string JsonString(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);           // кириллицу оставляем как есть (ensure_ascii=False)
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string JsonArray(IEnumerable<string> items)
        {
            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            foreach (string s in items)
            {
                if (!first) sb.Append(',');
                sb.Append(JsonString(s));
                first = false;
            }
            return sb.Append(']').ToString();
        }

        /// <summary>Разбор плоского JSON-объекта. Понимает строки и массивы строк — больше в конфиге нечего.</summary>
        public static Dictionary<string, object> JsonParseObject(string text)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return result;
            i++;
            while (true)
            {
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] == '}') break;
                if (text[i] != '"') break;
                string key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') break;
                i++;
                SkipWs(text, ref i);
                if (i >= text.Length) break;
                if (text[i] == '"')
                {
                    result[key] = ReadString(text, ref i);
                }
                else if (text[i] == '[')
                {
                    i++;
                    List<string> list = new List<string>();
                    while (true)
                    {
                        SkipWs(text, ref i);
                        if (i >= text.Length || text[i] == ']') { i++; break; }
                        if (text[i] == '"') list.Add(ReadString(text, ref i));
                        else SkipValue(text, ref i);
                        SkipWs(text, ref i);
                        if (i < text.Length && text[i] == ',') i++;
                    }
                    result[key] = list;
                }
                else
                {
                    SkipValue(text, ref i);
                }
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                break;
            }
            return result;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static void SkipValue(string s, ref int i)
        {
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    if (depth == 0) return;
                    depth--;
                }
                else if (c == ',' && depth == 0) return;
                i++;
            }
        }

        static string ReadString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;                                     // открывающая кавычка
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char e = s[i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 < s.Length)
                    {
                        sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber));
                        i += 4;
                    }
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;                                     // закрывающая кавычка
            return sb.ToString();
        }
    }
}
