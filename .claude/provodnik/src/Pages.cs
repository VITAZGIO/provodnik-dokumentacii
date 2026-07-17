// Страницы. HTML и CSS лежат отдельными файлами и вшиваются в exe при сборке
// (см. build.bat, ключ /resource) — чтобы их было удобно править как обычную вёрстку.
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Provodnik
{
    static class Pages
    {
        static string admin, mobile;

        /// <summary>Рабочее место на ПК — то, что открывается при запуске программы.</summary>
        public static string Admin()
        {
            if (admin == null)
                admin = Read("admin.html").Replace("%%STYLE%%", Read("style.css"));
            return admin;
        }

        /// <summary>Страница телефона.</summary>
        public static string Mobile()
        {
            if (mobile == null)
                mobile = Read("mobile.html").Replace("%%STYLE%%", Read("style.css"));
            return mobile;
        }

        static string Read(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return "";
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }
    }
}
