// Настройки программы. Файл тот же, что у Python-версии: %USERPROFILE%\provodnik_config.json
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Provodnik
{
    static class Config
    {
        public static readonly string[] DefaultSubfolders =
            { "Схема", "Кембрики", "Маркировка клемм", "Маркировка оборудования", "Фото" };

        public static string BaseDir = "";
        public static List<string> Subfolders = new List<string>(DefaultSubfolders);
        public static string LanIpOverride = "";

        public static string Path_
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return System.IO.Path.Combine(home, "provodnik_config.json");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(Path_)) return;
                string text = File.ReadAllText(Path_, Encoding.UTF8);
                Dictionary<string, object> data = Util.JsonParseObject(text);

                object v;
                if (data.TryGetValue("base_dir", out v) && v is string) BaseDir = (string)v ?? "";

                if (data.TryGetValue("subfolders", out v) && v is List<string>)
                {
                    List<string> subs = new List<string>();
                    foreach (string s in (List<string>)v)
                    {
                        string clean = Util.Sanitize(s);
                        if (clean.Length > 0) subs.Add(clean);
                    }
                    if (subs.Count > 0) Subfolders = subs;
                }

                if (data.TryGetValue("lan_ip_override", out v) && v is string)
                {
                    string ip = ((string)v ?? "").Trim();
                    LanIpOverride = Util.IpRe.IsMatch(ip) ? ip : "";
                }
            }
            catch (Exception)
            {
                // битый конфиг — работаем на значениях по умолчанию
            }
        }

        public static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"base_dir\": ").Append(Util.JsonString(BaseDir)).Append(",\n");
                sb.Append("  \"subfolders\": ").Append(Util.JsonArray(Subfolders)).Append(",\n");
                sb.Append("  \"lan_ip_override\": ").Append(Util.JsonString(LanIpOverride)).Append("\n");
                sb.Append("}\n");
                File.WriteAllText(Path_, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }
    }
}
