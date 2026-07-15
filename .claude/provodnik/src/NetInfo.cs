// Определение локального IP этого ПК.
// Логика перенесена из lan_ip()/_ipconfig_adapters() Python-версии: те же ключевые слова
// виртуальных адаптеров, тот же приоритет Wi-Fi → провод → остальное. Вместо разбора текста
// ipconfig берём те же данные напрямую у Windows (NetworkInterface) — надёжнее и без возни
// с кодировками консоли.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Provodnik
{
    class Adapter
    {
        public string Name;
        public string Ip;
        public override string ToString()
        {
            return Ip + "   —   " + Name;
        }
    }

    static class NetInfo
    {
        // виртуальные/не-Wi-Fi адаптеры — их IP телефону не подойдёт
        static readonly string[] VirtualHints =
        {
            "virtual", "vmware", "vmnet", "virtualbox", "hyper-v", "npcap", "tap-",
            "docker", "wsl", "loopback", "hotspot", "bluetooth", "tailscale", "zerotier",
            "виртуальн", "туннел", "vpn"
        };

        /// <summary>Все живые IPv4-адреса этого ПК — для выпадающего списка в окне.</summary>
        public static List<Adapter> AllAdapters()
        {
            List<Adapter> result = new List<Adapter>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                        Adapter a = new Adapter();
                        a.Name = ni.Name + " / " + ni.Description;
                        a.Ip = ip;
                        result.Add(a);
                    }
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        static bool IsVirtual(string nameLower)
        {
            foreach (string h in VirtualHints)
                if (nameLower.Contains(h)) return true;
            return false;
        }

        /// <summary>IP этого ПК в локальной сети (пакеты никуда не отправляются).</summary>
        public static string LanIp()
        {
            List<string> wifi = new List<string>(), wired = new List<string>(), other = new List<string>();
            foreach (Adapter a in AllAdapters())
            {
                string low = a.Name.ToLowerInvariant();
                if (IsVirtual(low)) continue;
                if (low.Contains("wi-fi") || low.Contains("wifi") || low.Contains("wireless") || low.Contains("беспров"))
                    wifi.Add(a.Ip);
                else if (low.Contains("ethernet") || low.Contains("локальн"))
                    wired.Add(a.Ip);
                else
                    other.Add(a.Ip);
            }
            foreach (string ip in wifi) return ip;      // сначала Wi-Fi,
            foreach (string ip in wired) return ip;     // потом провод,
            foreach (string ip in other) return ip;     // потом всё остальное

            // запасной способ — как в Python-версии: спросить у системы, каким адресом
            // она пошла бы наружу (соединение UDP, реально ничего не отправляется)
            List<string> candidates = new List<string>();
            try
            {
                using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 80);
                    string ip = ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                    if (!candidates.Contains(ip)) candidates.Add(ip);
                }
            }
            catch (Exception)
            {
            }

            foreach (string ip in candidates) if (ip.StartsWith("192.168.")) return ip;
            foreach (string ip in candidates) if (ip.StartsWith("10.")) return ip;
            foreach (string ip in candidates) if (!IsVirtualRange(ip)) return ip;
            return candidates.Count > 0 ? candidates[0] : "127.0.0.1";
        }

        static bool IsVirtualRange(string ip)
        {
            string[] parts = ip.Split('.');
            int second;
            return parts.Length == 4 && parts[0] == "172"
                   && int.TryParse(parts[1], out second) && second >= 16 && second <= 31;
        }
    }
}
