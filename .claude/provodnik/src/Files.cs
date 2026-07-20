// Операции с папками заявок: поиск по номеру, создание, копирование, удаление.
// Одна и та же логика для окна программы и для файлов, приходящих с телефона.
using System;
using System.Collections.Generic;
using System.IO;

namespace Provodnik
{
    class Entry
    {
        public string Name;
        public bool IsDir;
    }

    static class Files
    {
        public static bool HasBase
        {
            get { return Config.BaseDir.Length > 0 && Directory.Exists(Config.BaseDir); }
        }

        /// <summary>Имена папок заявок в рабочей папке.</summary>
        public static List<string> OrderNames()
        {
            List<string> orders = new List<string>();
            if (!HasBase) return orders;
            try
            {
                foreach (string d in Directory.GetDirectories(Config.BaseDir))
                    orders.Add(Path.GetFileName(d));
            }
            catch (Exception) { }
            return orders;
        }

        /// <summary>Папка заявки с таким номером, иначе новая «номер - наименование».</summary>
        public static string FindOrCreateOrder(string num, string name)
        {
            string dir = Util.FindOrderDir(Config.BaseDir, num);
            if (dir != null) return dir;
            num = Util.Sanitize(num);
            name = Util.Sanitize(name);
            return Path.Combine(Config.BaseDir, name.Length > 0 ? num + " - " + name : num);
        }

        /// <summary>Создаёт папку заявки с подпапками. Возвращает её имя.</summary>
        public static string CreateOrder(string num, string name)
        {
            string dir = FindOrCreateOrder(num, name);
            Directory.CreateDirectory(dir);
            EnsureSubfolders(dir);
            return Path.GetFileName(dir);
        }

        public static void EnsureSubfolders(string orderDir)
        {
            foreach (string sf in Config.Subfolders)
            {
                string s = Util.Sanitize(sf);
                if (s.Length > 0) Directory.CreateDirectory(Path.Combine(orderDir, s));
            }
        }

        public static string OrderPath(string orderName)
        {
            return Path.Combine(Config.BaseDir, orderName);
        }

        /// <summary>Что лежит в подпапке заявки: сначала папки, потом файлы.</summary>
        public static List<Entry> List(string orderName, string cat)
        {
            List<Entry> result = new List<Entry>();
            string dir = Path.Combine(OrderPath(orderName), Util.Sanitize(cat));
            if (!Directory.Exists(dir)) return result;
            try
            {
                List<string> dirs = new List<string>(Directory.GetDirectories(dir));
                List<string> files = new List<string>(Directory.GetFiles(dir));
                dirs.Sort(StringComparer.OrdinalIgnoreCase);
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    Entry e = new Entry();
                    e.Name = Path.GetFileName(d);
                    e.IsDir = true;
                    result.Add(e);
                }
                foreach (string f in files)
                {
                    Entry e = new Entry();
                    e.Name = Path.GetFileName(f);
                    e.IsDir = false;
                    result.Add(e);
                }
            }
            catch (Exception) { }
            return result;
        }

        public static void Delete(string orderName, string cat, string name, bool isDir)
        {
            string path = Path.Combine(OrderPath(orderName), Util.Sanitize(cat), name);
            if (isDir) Directory.Delete(path, true);
            else File.Delete(path);
        }

        /// <summary>
        /// Копирует файл или папку целиком в подпапку заявки. Имена-дубликаты получают
        /// « (1)», « (2)» — ничего не затирается. Возвращает число скопированных файлов.
        /// </summary>
        public static int Copy(string source, string orderName, string cat)
        {
            string target = Path.Combine(OrderPath(orderName), Util.Sanitize(cat));
            Directory.CreateDirectory(target);

            if (Directory.Exists(source))
            {
                string name = Util.Sanitize(Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
                if (name.Length == 0) name = "папка";
                string dest = UniqueDir(target, name);
                Directory.CreateDirectory(dest);
                return CopyTree(source, dest);
            }
            if (File.Exists(source))
            {
                CopyFile(source, target);
                return 1;
            }
            return 0;
        }

        static int CopyTree(string source, string dest)
        {
            int count = 0;
            foreach (string f in Directory.GetFiles(source))
            {
                CopyFile(f, dest);
                count++;
            }
            foreach (string d in Directory.GetDirectories(source))
            {
                string name = Util.Sanitize(Path.GetFileName(d));
                if (name.Length == 0) continue;
                string sub = UniqueDir(dest, name);
                Directory.CreateDirectory(sub);
                count += CopyTree(d, sub);
            }
            return count;
        }

        /// <summary>Копирует файл через .part, чтобы при обрыве не осталось половинки.</summary>
        static void CopyFile(string source, string targetDir)
        {
            string name = Util.Sanitize(Path.GetFileName(source));
            if (name.Length == 0) name = "файл";
            string dest = Util.UniqueReserve(targetDir, name);
            string part = dest + ".part";
            lock (State.PartFiles) State.PartFiles.Add(part);
            try
            {
                File.Copy(source, part, true);
                if (File.Exists(dest)) File.Delete(dest);      // пустышка-резерв
                File.Move(part, dest);
            }
            catch (Exception)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch (Exception) { }
                try { if (File.Exists(dest) && new FileInfo(dest).Length == 0) File.Delete(dest); }
                catch (Exception) { }
                throw;
            }
            finally
            {
                lock (State.PartFiles) State.PartFiles.Remove(part);
            }
        }

        static string UniqueDir(string parent, string name)
        {
            string dest = Path.Combine(parent, name);
            int i = 1;
            while (Directory.Exists(dest) || File.Exists(dest))
            {
                dest = Path.Combine(parent, string.Format("{0} ({1})", name, i));
                i++;
            }
            return dest;
        }
    }
}
