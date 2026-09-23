using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Sicau
{
    public class Cfg
    {
        public string UserId = "";
        public string Password = "";
        public string PortalHost = "portal.sicau.edu.cn";
        // 这两个值随接入交换机变化, 不能写死。认证时优先用网关跳转里的真实值,
        // 并自动缓存到 portal-cache.conf; 这里留空表示"没有兜底值, 完全靠网关跳转"。
        public string WlanAcName = "";
        public string NasIp = "";
        public bool ForceIPv4 = true;
        public int IntervalMinutes = 5;
        public bool AutoHotspot = false;
        public string Mode = "auto";   // 老配置里的 auto 表示"没选过"; 用之前一律过 Core.ConcreteMode()
        public string WifiSsid = "";
        public string HotspotBand = "auto";
        public string Theme = "auto";
        /// <summary>silent = 开机静默认证后直接退出; confirm = 认证后弹窗询问</summary>
        public string BootMode = "silent";
        /// <summary>日志保留多少小时, 超时的行自动删; 0 = 不自动删。</summary>
        public int LogKeepHours = 24;
        /// <summary>
        /// 关掉 Windows 的「强制门户探测」。
        ///
        /// 为什么需要它: 连上没有互联网的 WiFi(校园网就是这样)时, Windows 会去探测
        /// www.msftconnecttest.com, 网关把请求劫持到登录页 → 系统认为"这网要登录" →
        /// 自动给你开浏览器/弹窗。这个开关关掉探测(NCSI 的 EnableActiveProbing=0)后就不会再弹。
        ///
        /// 这个值是**全局**的(对所有 WiFi 生效), 而且写在 HKLM 下, **要管理员权限**才能改。
        /// </summary>
        public bool QuiethPortal = false;
    }

    public class HttpResult
    {
        public int Status;
        public string Location = "";
        public string Body = "";
        public string FinalUri = "";
    }

    public static class Core
    {
        public const string AppName = "SICAU-AutoLogin";
        /// <summary>版本号：只在「关于」和卸载列表里显示用。改版时同时改 build-win.ps1 的 $VER。</summary>
        public const string AppVersion = "1.7";

        // ---- 作者标识（用户要求在程序里带上自己的标识）----
        /// <summary>作者网名。</summary>
        public const string AuthorName = "极夜光";
        /// <summary>作者邮箱。</summary>
        public const string AuthorMail = "yeguang225@outlook.com";
        /// <summary>头像嵌在 EXE 里的资源名片段（图片本体是 .build\avatar.png，由 生成头像资源.ps1 生成）。</summary>
        public const string AvatarResource = "avatar";
        public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        public static string Dir = "";
        public static string ConfPath = "";
        public static string LogPath = "";
        public static string TaskName = "SICAU-Campus-AutoLogin";

        public static void InitPaths()
        {
            // 配置和日志固定放在 %APPDATA%\SICAU-AutoLogin\
            // 这样无论程序装在哪个目录(包括 Program Files 这种只读目录)都能正常读写,
            // 卸载时也能干净地一并清理。
            Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            try { Directory.CreateDirectory(Dir); } catch { }
            ConfPath = Path.Combine(Dir, "config.conf");
            LogPath = Path.Combine(Dir, "autologin.log");

            MigrateLegacyConfig();
        }

        /// <summary>老版本把配置放在 EXE 同目录, 这里自动搬一次过来。</summary>
        static void MigrateLegacyConfig()
        {
            try
            {
                string marker = Path.Combine(Dir, ".migrated");
                if (File.Exists(marker)) return;

                string exeDir = "";
                try { exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } catch { }
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string legacy = Path.Combine(exeDir, "config.conf");
                    if (File.Exists(legacy) && !File.Exists(ConfPath)) File.Copy(legacy, ConfPath, false);

                    string legacyCache = Path.Combine(exeDir, "portal-cache.conf");
                    string newCache = Path.Combine(Dir, "portal-cache.conf");
                    if (File.Exists(legacyCache) && !File.Exists(newCache)) File.Copy(legacyCache, newCache, false);
                }
                File.WriteAllText(marker, "1");
            }
            catch { }
        }

        // ------------------------------------------------ 日志
        static readonly object logLock = new object();
        public static Action<string> OnLog;

        public static void Log(string msg, string level = "INFO")
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg;
            if (OnLog != null) { try { OnLog(line); } catch { } }
            try
            {
                lock (logLock)
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    {
                        var all = File.ReadAllLines(LogPath);
                        var tail = all.Skip(Math.Max(0, all.Length - 400));
                        File.WriteAllLines(LogPath, tail, new UTF8Encoding(true));
                    }
                    File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(true));
                }
                MaybePruneLog();
            }
            catch { }
        }

        /// <summary>日志保留小时数(0 = 不自动删)。LoadCfg/SaveCfg 会同步它。</summary>
        public static int LogKeepHours = 24;
        static DateTime lastPrune = DateTime.MinValue;

        /// <summary>按行首时间戳清掉过期日志。每小时最多真扫一次, 不然每写一行都要读整个文件。</summary>
        static void MaybePruneLog()
        {
            if (LogKeepHours <= 0) return;
            if ((DateTime.Now - lastPrune).TotalMinutes < 60) return;
            lastPrune = DateTime.Now;
            int dropped = PruneLog(LogKeepHours);
            if (dropped > 0) Log("日志清理: 删掉了 " + dropped + " 行超过 " + LogKeepHours + " 小时的记录");
        }

        /// <summary>真正清理, 返回删掉的行数。认不出时间戳的行一律保留(多行消息的续行别误删)。</summary>
        public static int PruneLog(int hours)
        {
            if (hours <= 0) return 0;
            try
            {
                if (!File.Exists(LogPath)) return 0;
                DateTime cut = DateTime.Now.AddHours(-hours);
                var keep = new List<string>();
                int dropped = 0;
                foreach (var raw in File.ReadAllLines(LogPath))
                {
                    DateTime ts;
                    if (TryParseLogTime(raw, out ts) && ts < cut) { dropped++; continue; }
                    keep.Add(raw);
                }
                if (dropped == 0) return 0;
                lock (logLock) File.WriteAllLines(LogPath, keep, new UTF8Encoding(true));
                return dropped;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 开机认证前先等网卡拿到 IP: 没 IP 时门户页根本打不开, 直接去认证只会白失败一次。
        /// 每 300ms 看一次, 最多等 seconds 秒; 到点还没 IP 也返回 false(外面照样会试一次)。
        /// </summary>
        public static bool WaitForNetwork(int seconds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if (!string.IsNullOrEmpty(InterfaceIPv4(false)) || !string.IsNullOrEmpty(InterfaceIPv4(true)))
                {
                    Log("网卡已就绪, 等了 " + sw.ElapsedMilliseconds + " ms");
                    return true;
                }
                System.Threading.Thread.Sleep(300);
            }
            Log("等了 " + seconds + " 秒还没拿到网卡 IP, 直接试一次认证", "WARN");
            return false;
        }

        /// <summary>
        /// 这次失败是不是"网络/门户还没准备好"? 这种值得过几秒再试。
        /// 门户明确给了原因(密码错/停机/欠费…)的不重试, 重试也没用。
        /// </summary>
        public static bool LooksLikeNotReady(AuthOutcome o)
        {
            if (o == null) return true;                          // 抛异常了, 多半是网络还没好
            if (!string.IsNullOrEmpty(o.Reason)) return false;
            string m = o.Message ?? "";
            string[] soft = { "无法确定认证页地址", "打开认证页失败", "认证页返回",
                              "提交认证请求失败", "认证后仍未联网" };
            foreach (var s in soft)
                if (m.IndexOf(s, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static bool TryParseLogTime(string line, out DateTime ts)
        {
            ts = DateTime.MinValue;
            if (string.IsNullOrEmpty(line) || line.Length < 19) return false;
            return DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out ts);
        }

        // ------------------------------------------------ 配置
        public static Cfg LoadCfg()
        {
            var c = new Cfg();
            if (!File.Exists(ConfPath)) return c;
            try
            {
                foreach (var raw in File.ReadAllLines(ConfPath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1);
                    switch (k)
                    {
                        case "userid": c.UserId = v.Trim(); break;
                        case "password": c.Password = v; break;
                        case "portalhost": if (v.Length > 0) c.PortalHost = v.Trim(); break;
                        case "wlanacname": c.WlanAcName = v.Trim(); break;
                        case "nasip": c.NasIp = v.Trim(); break;
                        case "forceipv4": c.ForceIPv4 = v.Trim() != "0"; break;
                        case "intervalminutes": int.TryParse(v.Trim(), out c.IntervalMinutes); break;
                        case "autohotspot": c.AutoHotspot = v.Trim() == "1"; break;
                        case "mode": c.Mode = v.Trim().ToLowerInvariant(); break;
                        case "wifissid": c.WifiSsid = v.Trim(); break;
                        case "hotspotband": c.HotspotBand = v.Trim(); break;
                        case "theme": c.Theme = v.Trim().ToLowerInvariant(); break;
                        case "bootmode": c.BootMode = v.Trim().ToLowerInvariant(); break;
                        case "logkeephours": int.TryParse(v.Trim(), out c.LogKeepHours); break;
                        case "portalquiet": c.QuiethPortal = v.Trim() == "1"; break;
                    }
                }
            }
            catch { }
            LogKeepHours = c.LogKeepHours;   // Log() 是静态的, 自动清理由它按这个值判断
            return c;
        }

        public static void SaveCfg(Cfg c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SICAU-AutoLogin 配置 (明文保存, 请勿外传)");
            sb.AppendLine("userid=" + c.UserId);
            sb.AppendLine("password=" + c.Password);
            sb.AppendLine("portalhost=" + c.PortalHost);
            sb.AppendLine("wlanacname=" + c.WlanAcName);
            sb.AppendLine("nasip=" + c.NasIp);
            sb.AppendLine("forceipv4=" + (c.ForceIPv4 ? "1" : "0"));
            sb.AppendLine("intervalminutes=" + c.IntervalMinutes);
            sb.AppendLine("autohotspot=" + (c.AutoHotspot ? "1" : "0"));
            sb.AppendLine("mode=" + ConcreteMode(c.Mode));   // 不再写 auto: 连接方式必须落成具体的"有线/无线"
            sb.AppendLine("wifissid=" + (c.WifiSsid == null ? "" : c.WifiSsid));
            sb.AppendLine("hotspotband=" + (string.IsNullOrEmpty(c.HotspotBand) ? "auto" : c.HotspotBand));
            sb.AppendLine("theme=" + (string.IsNullOrEmpty(c.Theme) ? "auto" : c.Theme));
            sb.AppendLine("bootmode=" + (string.IsNullOrEmpty(c.BootMode) ? "silent" : c.BootMode));
            sb.AppendLine("logkeephours=" + c.LogKeepHours);
            sb.AppendLine("portalquiet=" + (c.QuiethPortal ? "1" : "0"));
            File.WriteAllText(ConfPath, sb.ToString(), new UTF8Encoding(true));
            LogKeepHours = c.LogKeepHours;
        }

        // ------------------------------------------------ HTTP (强制 IPv4)
        /// <summary>手动指定连接方式时固定使用的本地 IP (null = 交给系统路由决定)。</summary>
        public static string PreferredLocalIP = null;

        /// <summary>扫描附近可用的 WiFi 名称(用于界面上的下拉选择)。</summary>
        // ============================================================
        //  无线网络扫描
        //  netsh wlan show networks 在"已连接"状态下只返回缓存里的那一个网络,
        //  所以直接用 wlanapi 强制扫一次, 才能列出周围所有可见的 WiFi。
        // ============================================================
        [DllImport("wlanapi.dll")]
        static extern int WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")]
        static extern int WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")]
        static extern int WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")]
        static extern int WlanScan(IntPtr h, ref Guid guid, IntPtr ssid, IntPtr ie, IntPtr res);
        [DllImport("wlanapi.dll")]
        static extern int WlanGetAvailableNetworkList(IntPtr h, ref Guid guid, uint flags, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")]
        static extern void WlanFreeMemory(IntPtr p);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WlanIface
        {
            public Guid Guid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Desc;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WlanSsid
        {
            public int Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Data;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WlanAvail
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
            public WlanSsid Ssid;
            public int BssType;
            public int NumBssid;
            public int Connectable;
            public int Reason;
            public int NumPhy;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] Phy;
            public int MorePhy;
            public int Quality;
            public int Secure;
            public int AuthAlg;
            public int Cipher;
            public int Flags;
            public int Reserved;
        }

        static List<Guid> WlanInterfaces(IntPtr h)
        {
            var r = new List<Guid>();
            IntPtr p;
            if (WlanEnumInterfaces(h, IntPtr.Zero, out p) != 0) return r;
            try
            {
                int n = Marshal.ReadInt32(p);
                int sz = Marshal.SizeOf(typeof(WlanIface));
                for (int i = 0; i < n; i++)
                {
                    var it = (WlanIface)Marshal.PtrToStructure((IntPtr)(p.ToInt64() + 8 + i * sz), typeof(WlanIface));
                    r.Add(it.Guid);
                }
            }
            finally { WlanFreeMemory(p); }
            return r;
        }

        /// <summary>列出周围可见的 WiFi, 按信号从强到弱; 当前连接的排最前。</summary>
        public static List<string> ScanWifiNetworks()
        {
            var best = new Dictionary<string, int>();   // SSID -> 最强信号
            IntPtr h = IntPtr.Zero;
            try
            {
                uint neg;
                if (WlanOpenHandle(2, IntPtr.Zero, out neg, out h) != 0) h = IntPtr.Zero;

                if (h != IntPtr.Zero)
                {
                    var ifs = WlanInterfaces(h);
                    foreach (var g in ifs) { var gg = g; WlanScan(h, ref gg, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
                    if (ifs.Count > 0) System.Threading.Thread.Sleep(3000);   // 等扫描结果回来

                    foreach (var g in ifs)
                    {
                        var gg = g;
                        IntPtr p;
                        if (WlanGetAvailableNetworkList(h, ref gg, 0, IntPtr.Zero, out p) != 0) continue;
                        try
                        {
                            int n = Marshal.ReadInt32(p);
                            int sz = Marshal.SizeOf(typeof(WlanAvail));
                            for (int i = 0; i < n; i++)
                            {
                                var a = (WlanAvail)Marshal.PtrToStructure((IntPtr)(p.ToInt64() + 8 + i * sz), typeof(WlanAvail));
                                if (a.Ssid.Length <= 0 || a.Ssid.Data == null) continue;
                                int len = Math.Min(a.Ssid.Length, a.Ssid.Data.Length);
                                string s = Encoding.UTF8.GetString(a.Ssid.Data, 0, len).Trim();
                                if (s.Length == 0) continue;
                                int old;
                                if (!best.TryGetValue(s, out old) || a.Quality > old) best[s] = a.Quality;
                            }
                        }
                        finally { WlanFreeMemory(p); }
                    }
                }
            }
            catch { }
            finally { if (h != IntPtr.Zero) { try { WlanCloseHandle(h, IntPtr.Zero); } catch { } } }

            var list = new List<string>(best.Keys);
            list.Sort(delegate (string x, string y) { return best[y].CompareTo(best[x]); });   // 信号强的在前

            // 退路: wlanapi 不可用时退回 netsh
            if (list.Count == 0)
            {
                try
                {
                    if (HasWifiAdapter())
                    {
                        string o = RunCmd("netsh.exe", "wlan show networks mode=bssid");
                        foreach (Match m in Regex.Matches(o, @"(?m)^\s*SSID\s+\d+\s*:\s*(.+)$"))
                        {
                            string s = m.Groups[1].Value.Trim();
                            if (s.Length > 0 && !list.Contains(s)) list.Add(s);
                        }
                    }
                }
                catch { }
            }

            string cur = CurrentWifiSsid();
            if (cur.Length > 0 && list.Contains(cur))
            {
                list.Remove(cur);
                list.Insert(0, cur);   // 当前连接的排最前
            }
            return list;
        }

        /// <summary>本机有没有真正的无线网卡(决定热点功能能不能用)。</summary>
        public static bool HasWifiAdapter()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211) continue;
                    string d = ((ni.Description ?? "") + " " + (ni.Name ?? "")).ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("wi-fi direct") || d.Contains("hosted network")) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>取出有线或无线网卡的 IPv4 地址, 会排除虚拟网卡。</summary>
        public static string InterfaceIPv4(bool wifi)
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;

                    var t = ni.NetworkInterfaceType;
                    bool match = wifi
                        ? (t == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                        : (t == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                           || t == System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet
                           || t == System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT
                           || t == System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx);
                    if (!match) continue;

                    string desc = ((ni.Description ?? "") + " " + (ni.Name ?? "")).ToLowerInvariant();
                    if (desc.Contains("vmware") || desc.Contains("virtual") || desc.Contains("hyper-v")
                        || desc.Contains("loopback") || desc.Contains("bluetooth") || desc.Contains("tap-")
                        || desc.Contains("tunnel") || desc.Contains("wan miniport")) continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        if (s.StartsWith("127.") || s.StartsWith("169.254.")) continue;
                        return s;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>读取当前连接的 WiFi 名称。</summary>
        public static string CurrentWifiSsid()
        {
            try
            {
                string o = RunCmd("netsh.exe", "wlan show interfaces");
                var m = Regex.Match(o, @"(?m)^\s*SSID\s*:\s*(.+)$");
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
            catch { return ""; }
        }

        /// <summary>本机是否保存过该 WiFi 的配置文件(没保存过, netsh connect 必然失败)。</summary>
        public static bool WifiProfileExists(string ssid)
        {
            try
            {
                string o = RunCmd("netsh.exe", "wlan show profiles");
                return o.IndexOf(": " + ssid, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }
        }

        /// <summary>没连到指定 WiFi 时自动连上去。</summary>
        public static bool EnsureWifiConnected(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;
            try
            {
                string cur = CurrentWifiSsid();
                if (string.Equals(cur, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    Log("WiFi 已连接: " + ssid);
                    return true;
                }
                if (!WifiProfileExists(ssid))
                {
                    Log("本机没保存过 WiFi「" + ssid + "」, 请先在系统里手动连一次再试。", "WARN");
                    return false;
                }
                Log("正在连接 WiFi: " + ssid + "  (当前: " + (string.IsNullOrEmpty(cur) ? "未连接" : cur) + ")");
                RunCmd("netsh.exe", "wlan connect name=\"" + ssid + "\"");
                System.Threading.Thread.Sleep(5000);
                string now = CurrentWifiSsid();
                if (string.Equals(now, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    Log("WiFi 连接成功: " + now);
                    return true;
                }
                Log("WiFi 连接失败, 当前: " + (string.IsNullOrEmpty(now) ? "未连接" : now), "WARN");
                return false;
            }
            catch (Exception ex)
            {
                Log("WiFi 连接异常: " + ex.Message, "WARN");
                return false;
            }
        }

        /// <summary>连接方式的中文名(日志和提示用)。</summary>
        public static string ModeCn(string mode)
        {
            return string.Equals(mode, "wifi", StringComparison.OrdinalIgnoreCase) ? "仅无线" : "仅有线";
        }

        /// <summary>
        /// 连接方式只有「有线」「无线」两种 —— 主界面上方的页签就是选择方式, 没有"自动"这一项。
        /// 老配置里可能是 auto(或空值、认不出的值), 这里按当前插着哪块网卡定死一个:
        /// 有有线 IP 就用有线(宿舍/机房普遍插着网线), 没有有线 IP 才用无线。
        /// 两块网卡都还没拿到 IP(刚开机 DHCP 没完成)时按有线, 找不到网卡再回退系统路由。
        /// </summary>
        public static string ConcreteMode(string mode)
        {
            if (string.Equals(mode, "wired", StringComparison.OrdinalIgnoreCase)) return "wired";
            if (string.Equals(mode, "wifi", StringComparison.OrdinalIgnoreCase)) return "wifi";

            if (!string.IsNullOrEmpty(InterfaceIPv4(false))) return "wired";
            if (!string.IsNullOrEmpty(InterfaceIPv4(true))) return "wifi";
            return "wired";
        }

        /// <summary>按配置决定这次认证走哪块网卡。老配置里的 auto 先收敛成具体方式。</summary>
        public static void ApplyConnectionMode(Cfg c)
        {
            PreferredLocalIP = null;
            string raw = (c.Mode ?? "").Trim().ToLowerInvariant();
            string mode = ConcreteMode(raw);
            if (raw != "wired" && raw != "wifi")
                Log("连接方式: 配置里的「" + (raw.Length == 0 ? "空" : raw) + "」已取消, 本次按当前网卡用 "
                    + ModeCn(mode) + "; 主界面点「有线 / 无线」页签可改", "WARN");

            if (mode == "wifi")
            {
                if (!string.IsNullOrEmpty(c.WifiSsid)) EnsureWifiConnected(c.WifiSsid);
                string ip = InterfaceIPv4(true);
                if (!string.IsNullOrEmpty(ip)) { PreferredLocalIP = ip; Log("连接方式: 仅无线, 使用 " + ip); }
                else Log("连接方式: 仅无线, 但没找到可用的无线 IP, 回退自动选择", "WARN");
            }
            else
            {
                string ip = InterfaceIPv4(false);
                if (!string.IsNullOrEmpty(ip)) { PreferredLocalIP = ip; Log("连接方式: 仅有线, 使用 " + ip); }
                else Log("连接方式: 仅有线, 但没找到可用的有线 IP, 回退自动选择", "WARN");
            }
        }

        static IPAddress ResolveIPv4(string host)
        {
            IPAddress parsed;
            if (IPAddress.TryParse(host, out parsed))
                return parsed.AddressFamily == AddressFamily.InterNetwork ? parsed : null;
            try
            {
                foreach (var a in Dns.GetHostAddresses(host))
                    if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            }
            catch { }
            return null;
        }

        public static HttpResult Fetch(string url, string method = "GET", string formBody = null,
                                       CookieContainer jar = null, bool follow = true, int timeoutSec = 10, bool forceV4 = true)
        {
            int hops = 0;
            string cur = url;
            string body = formBody;
            string m = method;
            string lastUri = url;

            while (true)
            {
                var uri = new Uri(cur);
                string targetUrl = cur;
                string hostHeader = null;

                if (forceV4)
                {
                    var ip = ResolveIPv4(uri.Host);
                    IPAddress parsedHost;
                    bool hostIsIpLiteral = IPAddress.TryParse(uri.Host, out parsedHost);
                    if (ip != null && !hostIsIpLiteral)
                    {
                        var b = new UriBuilder(uri) { Host = ip.ToString() };
                        targetUrl = b.Uri.AbsoluteUri;
                        hostHeader = uri.Host;
                    }
                }

                var req = (HttpWebRequest)WebRequest.Create(targetUrl);
                if (hostHeader != null) req.Host = hostHeader;
                req.Method = m;
                req.AllowAutoRedirect = false;
                req.Timeout = timeoutSec * 1000;
                req.ReadWriteTimeout = timeoutSec * 1000;
                req.UserAgent = UA;
                // 显式给一个空 WebProxy: 避免学生机开着全局代理时认证被代理拦掉
                req.Proxy = new WebProxy();
                req.KeepAlive = false;
                req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                if (jar != null) req.CookieContainer = jar;

                if (!string.IsNullOrEmpty(PreferredLocalIP))
                {
                    try
                    {
                        IPAddress lip = IPAddress.Parse(PreferredLocalIP);
                        req.ServicePoint.BindIPEndPointDelegate =
                            (sp, remote, retry) => new IPEndPoint(lip, 0);
                    }
                    catch { }
                }

                if (m == "POST" && body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentType = "application/x-www-form-urlencoded";
                    req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }

                HttpWebResponse resp;
                try { resp = (HttpWebResponse)req.GetResponse(); }
                catch (WebException wex)
                {
                    if (wex.Response == null) throw;
                    resp = (HttpWebResponse)wex.Response;
                }

                string text = "";
                try
                {
                    using (var rs = resp.GetResponseStream())
                        if (rs != null)
                            using (var sr = new StreamReader(rs, Encoding.UTF8, true))
                                text = sr.ReadToEnd();
                }
                catch { }

                int status = (int)resp.StatusCode;
                string loc = resp.Headers["Location"];
                resp.Close();
                lastUri = cur;

                if (follow && status >= 300 && status < 400 && !string.IsNullOrEmpty(loc) && hops < 5)
                {
                    cur = new Uri(uri, loc).AbsoluteUri;
                    hops++;
                    m = "GET";
                    body = null;
                    continue;
                }

                return new HttpResult { Status = status, Location = loc ?? "", Body = text, FinalUri = lastUri };
            }
        }

        static bool IsIp(string s) { IPAddress a; return IPAddress.TryParse(s, out a); }

        // ------------------------------------------------ 联网判定
        public static bool TestOnline(int timeoutSec = 6)
        {
            try { var r = Fetch("http://connect.rom.miui.com/generate_204", "GET", null, null, false, timeoutSec); if (r.Status == 204) return true; } catch { }
            try { var r = Fetch("http://www.msftconnecttest.com/connecttest.txt", "GET", null, null, false, timeoutSec); if (r.Status == 200 && r.Body.Contains("Microsoft Connect Test")) return true; } catch { }
            try { var r = Fetch("http://detectportal.firefox.com/success.txt", "GET", null, null, false, timeoutSec); if (r.Status == 200 && r.Body.Contains("success")) return true; } catch { }
            try { var r = Fetch("https://www.baidu.com/", "GET", null, null, false, timeoutSec); if (r.Status >= 200 && r.Status < 400) return true; } catch { }
            return false;
        }

        // ------------------------------------------------ 认证页定位
        public static string FindPortalUrlInText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string t = text.Replace("&amp;", "&").Replace("\\/", "/");
            var m = Regex.Match(t, "https?://[A-Za-z0-9\\.\\-]+(:[0-9]+)?/portal\\.do\\?[^\\s\"<>\\\\]*", RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;
            m = Regex.Match(t, "/portal\\.do\\?[^\\s\"<>\\\\]*", RegexOptions.IgnoreCase);
            if (m.Success) return "https://" + Core.PortalHostOf() + m.Value;
            return null;
        }

        static string portalHostCached = "portal.sicau.edu.cn";
        public static string PortalHostOf() { return portalHostCached; }
        public static void SetPortalHost(string h) { portalHostCached = h; }

        public static string GetPortalUrlFromGateway(string portalHost, int timeoutSec = 6)
        {
            string[] cands = {
                "http://connect.rom.miui.com/generate_204",
                "http://www.msftconnecttest.com/connecttest.txt",
                "http://www.baidu.com/"
            };
            foreach (var u in cands)
            {
                try
                {
                    var r = Fetch(u, "GET", null, null, false, timeoutSec);
                    if (r.Status >= 300 && r.Status < 400 && !string.IsNullOrEmpty(r.Location))
                    {
                        string loc = r.Location.Replace("&amp;", "&");
                        if (loc.Contains("portal.do?")) return loc;
                        if (loc.StartsWith("/")) return new Uri(new Uri(u), loc).AbsoluteUri;
                    }
                    var found = FindPortalUrlInText(r.Body);
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        public static string GetFallbackPortalUrl(string portalHost, Cfg c)
        {
            string ip = PreferredLocalIP;
            if (string.IsNullOrEmpty(ip)) ip = OutboundIP(portalHost);
            if (ip == null) return null;
            string ac = "", nas = "";
            string cachePath = Path.Combine(Dir, "portal-cache.conf");
            try
            {
                if (File.Exists(cachePath))
                {
                    foreach (var raw in File.ReadAllLines(cachePath, Encoding.UTF8))
                    {
                        var line = raw.Trim();
                        int i = line.IndexOf('=');
                        if (i < 1) continue;
                        string k = line.Substring(0, i).Trim().ToLowerInvariant();
                        string v = line.Substring(i + 1).Trim();
                        if (k == "wlanacname") ac = v;
                        if (k == "nasip") nas = v;
                    }
                }
            }
            catch { }
            if (ac.Length == 0) ac = c.WlanAcName;
            if (nas.Length == 0) nas = c.NasIp;

            string qs = "wlanuserip=" + ip;
            if (ac.Length > 0) qs += "&wlanacname=" + ac;
            if (nas.Length > 0) qs += "&nasip=" + nas;
            return "https://" + portalHost + "/portal.do?" + qs;
        }

        public static string OutboundIP(string target)
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Connect(target, 443);
                    return ((IPEndPoint)udp.Client.LocalEndPoint).Address.ToString();
                }
            }
            catch { }
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Connect("223.5.5.5", 53);
                    return ((IPEndPoint)udp.Client.LocalEndPoint).Address.ToString();
                }
            }
            catch { }
            return null;
        }

        static void SavePortalCache(string urlParameter)
        {
            try
            {
                string ip = Grab(urlParameter, "wlanuserip");
                string ac = Grab(urlParameter, "wlanacname");
                string nas = Grab(urlParameter, "nasip");
                var sb = new StringBuilder();
                sb.AppendLine("updatedat=" + DateTime.Now.ToString("s"));
                sb.AppendLine("wlanuserip=" + ip);
                sb.AppendLine("wlanacname=" + ac);
                sb.AppendLine("nasip=" + nas);
                File.WriteAllText(Path.Combine(Dir, "portal-cache.conf"), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        static string Grab(string qs, string key)
        {
            var m = Regex.Match(qs ?? "", "(?:^|&)" + Regex.Escape(key) + "=([^&]*)");
            return m.Success ? m.Groups[1].Value : "";
        }

        // ------------------------------------------------ 表单解析
        public static Dictionary<string, string> ParseFormFields(string html)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(html)) return map;
            foreach (Match m in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                string tag = m.Value;
                var nm = Regex.Match(tag, "name\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (!nm.Success) nm = Regex.Match(tag, "name\\s*=\\s*'([^']*)'", RegexOptions.IgnoreCase);
                if (!nm.Success) continue;
                string name = nm.Groups[1].Value;
                if (name.Trim().Length == 0) continue;

                var tm = Regex.Match(tag, "type\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                string type = tm.Success ? tm.Groups[1].Value.ToLowerInvariant() : "text";
                if (type == "button" || type == "submit" || type == "reset" || type == "file" || type == "image") continue;

                var vm = Regex.Match(tag, "value\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (!vm.Success) vm = Regex.Match(tag, "value\\s*=\\s*'([^']*)'", RegexOptions.IgnoreCase);
                string val = vm.Success ? vm.Groups[1].Value : "";
                val = val.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
                map[name] = val;
            }
            return map;
        }

        // ------------------------------------------------ 认证主流程
        public class AuthOutcome
        {
            public bool Ok;
            public bool AlreadyOnline;
            public string Message = "";
            public string Reason = "";
        }

        /// <summary>从门户返回的页面里解析出认证失败的具体原因(密码错误/停机/欠费/在线数超限等)。</summary>
        /// <summary>门户把失败原因放在 errMessage 隐藏字段里, 比在正文里瞎猜准得多。</summary>
        public static string ParseErrMessage(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            Match m = Regex.Match(html, "id=\"errMessage\"[^>]*value=\"([^\"]*)\"");
            if (!m.Success) return "";
            try { return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim(); }
            catch { return m.Groups[1].Value.Trim(); }
        }

        /// <summary>密码/账号/停机/欠费/锁定这类硬错误, 再提交一次也没用。</summary>
        static bool IsHardAuthFailure(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            string[] hard = { "密码错误", "密码不正确", "用户名或密码", "账号不存在", "账号已停机",
                              "账号欠费", "余额不足", "账号被锁定", "已锁定", "不允许", "禁用" };
            foreach (var h in hard) if (reason.IndexOf(h, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static string ParseFailureReason(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string t = Regex.Replace(html, "(?s)<script.*?</script>", " ");
            t = Regex.Replace(t, "(?s)<style.*?</style>", " ");
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = Regex.Replace(t, "\\s+", " ");

            // 只放不会在登录页出现的词, 避免误判
            string[,] rules = {
                { "用户名或密码", "账号或密码错误" },
                { "密码错误", "密码错误" },
                { "密码不正确", "密码错误" },
                { "用户不存在", "账号不存在" },
                { "帐号不存在", "账号不存在" },
                { "账号不存在", "账号不存在" },
                { "已停机", "账号已停机" },
                { "账号停机", "账号已停机" },
                { "欠费", "账号欠费" },
                { "余额不足", "账号余额不足" },
                { "被锁定", "账号被锁定" },
                { "已锁定", "账号被锁定" },
                { "在线数", "在线设备数已达上限" },
                { "已达上限", "已达上限" },
                { "超过最大", "在线设备数超限" },
                { "重复认证", "该 IP 已在线, 无需重复认证" }
            };
            for (int i = 0; i < rules.GetLength(0); i++)
                if (t.IndexOf(rules[i, 0], StringComparison.Ordinal) >= 0) return rules[i, 1];
            return "";
        }

        public static AuthOutcome DoAuth(Cfg c, bool force = false)
        {
            var o = new AuthOutcome();
            SetPortalHost(c.PortalHost);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            if (string.IsNullOrWhiteSpace(c.UserId) || string.IsNullOrEmpty(c.Password))
            {
                o.Message = "配置里没有学号或密码";
                Log(o.Message, "ERROR");
                return o;
            }

            ApplyConnectionMode(c);

            bool online = TestOnline();
            if (!force && online)
            {
                o.Ok = true; o.AlreadyOnline = true; o.Message = "当前已联网, 无需认证";
                Log(o.Message);
                return o;
            }
            if (force) Log("(强制) 跳过联网判断, online=" + online);

            string portalUrl = GetPortalUrlFromGateway(c.PortalHost);
            if (portalUrl != null) Log("认证网关跳转: " + portalUrl);
            else
            {
                portalUrl = GetFallbackPortalUrl(c.PortalHost, c);
                if (portalUrl != null) Log("未捕获到网关跳转, 用兜底地址: " + portalUrl);
            }
            if (portalUrl == null) { o.Message = "无法确定认证页地址(可能不在校园网内)"; Log(o.Message, "WARN"); return o; }

            var jar = new CookieContainer();
            HttpResult page;
            try { page = Fetch(portalUrl, "GET", null, jar, true, 12); }
            catch (Exception ex) { o.Message = "打开认证页失败: " + ex.Message; Log(o.Message, "ERROR"); return o; }

            Log("认证页 HTTP " + page.Status + ", 长度 " + page.Body.Length);
            if (page.Status != 200) { o.Message = "认证页返回 " + page.Status; Log(o.Message, "WARN"); return o; }

            var fields = ParseFormFields(page.Body);
            if (fields.Count == 0 || !fields.ContainsKey("urlParameter"))
            {
                o.Message = "认证页里没有解析到登录表单";
                Log(o.Message, "WARN");
                return o;
            }
            Log("解析到表单字段 " + fields.Count + " 个");

            string urlParam = fields["urlParameter"];
            if (!string.IsNullOrEmpty(urlParam)) SavePortalCache(urlParam);

            // 登录表单里 userId/passwd 两个框本身就在, 只是值是空的。
            // 如果再把它们追加一遍, body 里就会出现两个同名参数; 门户用
            // request.getParameter() 只取第一个 -> 拿到空值 -> 报"账号不存在"。
            // 所以这里必须"覆盖", 不能"追加"。
            fields["userId"] = c.UserId;
            fields["passwd"] = c.Password;
            fields["isRemind"] = "1";
            fields["remInfo"] = "on";

            var sb = new StringBuilder();
            foreach (var kv in fields)
            {
                if (kv.Key.Trim().Length == 0) continue;
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }

            // 门户不设 cookie, 会话号是通过 URL 重写带的: /xxx;JSESSIONID-BOSS-0=A1B2...
            // 提交时必须带上, 否则服务端当成一个全新会话, 只会把登录页原样吐回来。
            string sid = "";
            Match msid = Regex.Match(page.Body, ";JSESSIONID[^=;?&\"'<>]*=[^;?&\"'<>]+");
            if (msid.Success) { sid = msid.Value; Log("会话号: " + sid); }
            else Log("页面里没找到会话号, 按无会话提交", "WARN");

            string baseUrl = "https://" + c.PortalHost + "/webauth.do" + sid;
            string target = urlParam.Length > 0 ? baseUrl + "?" + urlParam : baseUrl;

            string postBody = sb.ToString();

            // 校园网同账号只能一台设备在线: 若已有设备在线, 第一次登录会把
            // 对方踢下线, 本机还需要再提交一次才真正认证。所以这里最多提交两次。
            for (int attempt = 1; ; attempt++)
            {
                Log(attempt == 1 ? ("提交认证: " + target) : ("第 " + attempt + " 次提交(切换其他设备): " + target));
                HttpResult r;
                try { r = Fetch(target, "POST", postBody, jar, true, 15); }
                catch (Exception ex) { o.Message = "提交认证请求失败: " + ex.Message; Log(o.Message, "ERROR"); return o; }
                Log("认证响应 HTTP " + r.Status);

                string plain = Regex.Replace(r.Body, "(?s)<script.*?</script>", " ");
                plain = Regex.Replace(plain, "(?s)<style.*?</style>", " ");
                plain = Regex.Replace(plain, "<[^>]+>", " ");
                plain = Regex.Replace(plain, "\\s+", " ").Trim();
                if (plain.Length > 0) Log("响应摘要: " + plain.Substring(0, Math.Min(260, plain.Length)));

                System.Threading.Thread.Sleep(2000);
                if (TestOnline())
                {
                    o.Ok = true;
                    o.Message = attempt == 1 ? "认证成功, 网络已连通" : "认证成功(已将其他设备切换下线)";
                    Log(o.Message);
                    return o;
                }

                // errMessage 是门户自己写的失败原因, 优先用它
                string emsg = ParseErrMessage(r.Body);
                string reason = emsg.Length > 0 ? emsg : ParseFailureReason(r.Body);
                // 密码/账号/停机/欠费/锁定这类硬错误, 再提交一次也没用, 直接失败
                if (IsHardAuthFailure(reason))
                {
                    o.Reason = reason;
                    o.Message = "认证失败: " + reason;
                    Log(o.Message, "WARN");
                    return o;
                }
                if (attempt >= 2)
                {
                    o.Reason = reason;
                    o.Message = reason.Length > 0 ? ("认证失败: " + reason) : "认证后仍未联网, 请检查账号密码或门户提示";
                    Log(o.Message, "WARN");
                    return o;
                }
                Log("首次提交后仍未联网, 可能是有其他设备在线, 自动重试一次");
                System.Threading.Thread.Sleep(2000);
            }
        }

        // ------------------------------------------------ 移动热点
        /// <summary>读取当前热点配置(SSID/密码/频段/状态)。</summary>
        public static bool GetHotspotInfo(out string ssid, out string pass, out string band, out string state, out int clients)
        {
            ssid = ""; pass = ""; band = ""; state = ""; clients = 0;
            try
            {
                string o = RunHotspot("info");
                foreach (var raw in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim();
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    if (k == "ssid") ssid = v;
                    else if (k == "pass") pass = v;
                    else if (k == "band") band = v;
                    else if (k == "state") state = v;
                    else if (k == "clients") int.TryParse(v, out clients);
                }
            }
            catch { }
            return ssid.Length > 0;
        }

        /// <summary>修改热点的名称和密码。</summary>
        public static bool SetHotspotConfig(string ssid, string pass, out string message)
        {
            message = "";
            try
            {
                string args = "set";
                if (!string.IsNullOrWhiteSpace(ssid))
                    args += " -Ssid \"" + ssid.Trim().Replace("\"", "") + "\"";
                if (!string.IsNullOrEmpty(pass))
                    args += " -Pass \"" + pass.Replace("\"", "") + "\"";
                if (args == "set") { message = "没有要修改的内容"; return false; }

                string outp = RunHotspot(args);
                message = outp.Trim();
                if (!string.IsNullOrWhiteSpace(ssid))
                    return outp.IndexOf(ssid.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
                return outp.IndexOf("错误", StringComparison.Ordinal) < 0
                    && outp.IndexOf("失败", StringComparison.Ordinal) < 0;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static string RunHotspot(string action)
        {
            try
            {
                string ps = LoadEmbedded("HotspotScript");
                if (ps == null) return "找不到内置热点脚本";
                string tmp = Path.Combine(Path.GetTempPath(), "sicau-hotspot-" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(tmp, ps, new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + tmp + "\" " + action)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                try { File.Delete(tmp); } catch { }
                return outp.Trim();
            }
            catch (Exception ex) { return "热点操作失败: " + ex.Message; }
        }

        static string LoadEmbedded(string namePart)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in asm.GetManifestResourceNames())
                if (n.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    using (var s = asm.GetManifestResourceStream(n))
                    using (var sr = new StreamReader(s, Encoding.UTF8))
                        return sr.ReadToEnd();
            return null;
        }

        /// <summary>
        /// 按名字片段读嵌入资源, 返回原始字节（图片用这个; 文本用 LoadEmbedded）。
        /// 读不到返回 null。
        /// </summary>
        public static byte[] LoadEmbeddedBytes(string namePart)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var n in asm.GetManifestResourceNames())
                {
                    if (n.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var s = asm.GetManifestResourceStream(n);
                    if (s == null) continue;
                    using (s)
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------ 计划任务
        public static string ExePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        /// <summary>
        /// 开机自启: 往「启动」文件夹放一个快捷方式, 不再用计划任务。
        ///
        /// ⚠️ 2026-09-21 换的实现 —— 原来用 schtasks 注册「登录时启动」的计划任务, 被
        /// Windows Defender 判成 Behavior:Win32/Persistence.A!ml (严重性 5): 未签名程序 +
        /// 建登录任务 + 写 HKLM\...\Schedule\TaskCache, 正是恶意持久化的经典组合,
        /// 结果它把计划任务和桌面上的 EXE 一起删了。启动文件夹是正常软件的做法,
        /// 不需要管理员, 也不碰注册表。
        /// (代价: 启动文件夹没有「每 N 分钟重复触发」, 周期复检交给程序自己的托盘定时器。)
        /// </summary>
        public static string AutostartLnk()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(dir, LinkName);
        }

        public static bool AutostartEnabled()
        {
            try { return File.Exists(AutostartLnk()); } catch { return false; }
        }

        public static bool EnableAutostart(string exePath, bool hotspotOnBoot, out string message)
        {
            message = "";
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    message = "找不到程序文件: " + exePath;
                    return false;
                }
                string args = "--auto" + (hotspotOnBoot ? " --hotspot-on-boot" : "");
                CreateShortcut(AutostartLnk(), exePath, Path.GetDirectoryName(exePath), args);
                if (!File.Exists(AutostartLnk())) { message = "快捷方式没写成功"; return false; }
                message = AutostartLnk();
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static void DisableAutostart()
        {
            try { File.Delete(AutostartLnk()); } catch { }
        }

        /// <summary>
        /// 把老版本留下的计划任务清掉。它已经被杀软标成恶意持久化, 留着不但没用,
        /// 还可能让杀软继续盯着这个程序删。
        /// </summary>
        public static void RemoveLegacyTask()
        {
            try
            {
                int code;
                RunCmd("schtasks.exe", "/Query /TN \"" + TaskName + "\"", out code);
                if (code == 0) RunCmd("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", out code);
            }
            catch { }
        }

        // ================================================ 安装 / 卸载
        public static string CurrentExe()
        {
            try { return Assembly.GetExecutingAssembly().Location; } catch { return ""; }
        }

        public static string CurrentExeDir()
        {
            try { return Path.GetDirectoryName(CurrentExe()); } catch { return ""; }
        }

        public static string DefaultInstallDir()
        {
            string p = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(p, "Programs\\" + AppName);
        }

        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SICAU-AutoLogin";
        const string LinkName = "校园网自动认证.lnk";

        public static string GetInstalledDir()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    if (k == null) return null;
                    object v = k.GetValue("InstallLocation");
                    return v == null ? null : v.ToString();
                }
            }
            catch { return null; }
        }

        public static bool IsInstalled()
        {
            string d = GetInstalledDir();
            if (string.IsNullOrEmpty(d)) return false;
            return string.Equals(d.TrimEnd('\\'), CurrentExeDir().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public class InstallResult
        {
            public bool Ok;
            public string Message = "";
            public string Target = "";
        }

        public static InstallResult InstallTo(string targetDir, bool createShortcuts, bool registerTask)
        {
            var res = new InstallResult();
            try
            {
                if (string.IsNullOrWhiteSpace(targetDir)) { res.Message = "没有选择安装目录"; return res; }
                targetDir = Path.GetFullPath(targetDir.Trim()).TrimEnd('\\');

                string src = CurrentExe();
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) { res.Message = "无法定位当前程序文件"; return res; }

                if (string.Equals(targetDir, CurrentExeDir().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    res.Message = "目标目录就是当前程序所在目录";
                    return res;
                }

                Directory.CreateDirectory(targetDir);
                string dst = Path.Combine(targetDir, AppName + ".exe");

                if (string.Equals(dst, src, StringComparison.OrdinalIgnoreCase)) { res.Message = "源和目标是同一个文件"; return res; }
                File.Copy(src, dst, true);

                WriteUninstallRegistry(targetDir, dst);
                if (createShortcuts) CreateShortcuts(dst, targetDir);

                if (registerTask)
                {
                    var c = LoadCfg();
                    string msg;
                    EnableAutostart(dst, c.AutoHotspot, out msg);
                }

                res.Ok = true;
                res.Target = dst;
                return res;
            }
            catch (Exception ex)
            {
                res.Message = ex.Message;
                return res;
            }
        }

        /// <summary>卸载: 删自启 / 快捷方式 / 注册表项, 可选删配置, 最后自删程序文件。</summary>
        public static string Uninstall(bool removeConfig, bool removeProgramDir)
        {
            string installedDir = GetInstalledDir();
            string exe = CurrentExe();
            string exeDir = CurrentExeDir();

            DisableAutostart();
            RemoveLegacyTask();     // 顺手把老版本的计划任务也清掉
            RemoveShortcuts();
            RemoveUninstallRegistry();

            if (removeConfig)
            {
                try { if (File.Exists(ConfPath)) File.Delete(ConfPath); } catch { }
                try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
                try
                {
                    string cache = Path.Combine(Dir, "portal-cache.conf");
                    if (File.Exists(cache)) File.Delete(cache);
                }
                catch { }
            }

            bool canRemoveDir = removeProgramDir
                && !string.IsNullOrEmpty(installedDir)
                && !string.IsNullOrEmpty(exeDir)
                && string.Equals(installedDir.TrimEnd('\\'), exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            if (canRemoveDir) ScheduleSelfDelete(exe, installedDir);
            else ScheduleSelfDelete(exe, null);

            if (canRemoveDir) return "程序文件将在退出后自动删除：" + installedDir;
            return "程序文件需要你手动删除：" + exeDir;
        }

        // ------------------------------------------------ 强制门户探测开关
        /// <summary>
        /// Windows 存「要不要探测强制门户」的注册表位置。
        /// 这个值只管探测, 改它不会影响上网本身。
        /// </summary>
        const string NcsiKey = @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet";

        /// <summary>
        /// 读当前状态: true = 探测开着(系统会自动弹登录页), false = 已关掉。
        /// 读不到就返回 true(老版本缺这个值时 Windows 默认是开的)。
        /// </summary>
        public static bool ProbeEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(NcsiKey, false))
                {
                    if (k == null) return true;
                    object v = k.GetValue("EnableActiveProbing");
                    if (v == null) return true;
                    return Convert.ToInt32(v) != 0;
                }
            }
            catch { return true; }
        }

        /// <summary>
        /// 写入探测开关。<paramref name="want"/> true = 恢复系统默认(开探测)，
        /// false = 关掉探测(不再自动弹登录页)。
        /// 返回 true 表示写成功；**写 HKLM 需要管理员权限**，没权限时返回 false。
        /// </summary>
        public static bool SetProbe(bool want, out string message)
        {
            message = "";
            try
            {
                int val = want ? 1 : 0;
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(NcsiKey))
                {
                    if (k == null) { message = "打不开注册表键（需要管理员权限）"; return false; }
                    k.SetValue("EnableActiveProbing", val, Microsoft.Win32.RegistryValueKind.DWord);
                }

                // 写进去再读一遍, 确认真的生效了（有些机器被组策略锁住, 写了也会被覆盖）
                if (ProbeEnabled() != want)
                {
                    message = "写进去了但读回来还是旧值，可能被组策略锁定";
                    Log("强制门户探测开关: 写入 " + val + " 后复查不一致", "WARN");
                    return false;
                }
                message = want ? "已恢复系统默认（会探测）" : "已关掉探测，登录页不会再自动弹出";
                Log("强制门户探测开关: " + message);
                return true;
            }
            catch (Exception ex)
            {
                message = "改不了（需要管理员权限）: " + ex.Message;
                Log("强制门户探测开关写入失败: " + ex.Message, "WARN");
                return false;
            }
        }

        /// <summary>本机相关的排查信息, 出问题时打日志用。</summary>
        public static string ProbeInfo()
        {
            bool on = ProbeEnabled();
            bool admin = false;
            try
            {
                var wi = System.Security.Principal.WindowsIdentity.GetCurrent();
                admin = new System.Security.Principal.WindowsPrincipal(wi).IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }
            return "强制门户探测: " + (on ? "开着（系统会自动弹登录页）" : "已关掉")
                 + "；当前" + (admin ? "有" : "没有") + "管理员权限";
        }

        // ------------------------------------------------ 快捷方式
        static string StartMenuLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName);
        }

        static string DesktopLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName);
        }

        public static void CreateShortcuts(string exePath, string workDir)
        {
            try { CreateShortcut(StartMenuLnk(), exePath, workDir); } catch { }
            try { CreateShortcut(DesktopLnk(), exePath, workDir); } catch { }
        }

        public static void RemoveShortcuts()
        {
            try { if (File.Exists(StartMenuLnk())) File.Delete(StartMenuLnk()); } catch { }
            try { if (File.Exists(DesktopLnk())) File.Delete(DesktopLnk()); } catch { }
        }

        static void CreateShortcut(string lnkPath, string target, string workDir, string arguments = null)
        {
            Type shType = Type.GetTypeFromProgID("WScript.Shell");
            if (shType == null) return;
            object shell = Activator.CreateInstance(shType);
            object lnk = shType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { workDir });
            // 快捷方式也要能带参数: 开机自启那条用的是 "--auto [--hotspot-on-boot]"
            if (!string.IsNullOrEmpty(arguments))
                lt.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { arguments });
            lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "四川农业大学校园网自动认证" });
            lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
        }

        // ------------------------------------------------ 注册表(出现在「设置 - 应用」里)
        public static void WriteUninstallRegistry(string installDir, string exePath)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "校园网自动认证 (SICAU-AutoLogin)");
                    k.SetValue("DisplayVersion", AppVersion);
                    k.SetValue("Publisher", "SICAU-AutoLogin");
                    k.SetValue("InstallLocation", installDir);
                    k.SetValue("DisplayIcon", exePath);
                    k.SetValue("UninstallString", "\"" + exePath + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + exePath + "\" --uninstall --yes");
                    k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static void RemoveUninstallRegistry()
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        }

        // ------------------------------------------------ 退出后自删
        public static void ScheduleSelfDelete(string exePath, string dirToRemove)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath)) return;
                string cmd = "/c ping 127.0.0.1 -n 4 > nul & del /f /q \"" + exePath + "\"";
                if (!string.IsNullOrEmpty(dirToRemove))
                    cmd += " & rmdir /s /q \"" + dirToRemove + "\"";
                var psi = new ProcessStartInfo("cmd.exe", cmd)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }

        static string EscapeXml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public static string RunCmd(string file, string args)
        {
            int code;
            return RunCmd(file, args, out code);
        }

        // 注意: 不要指定 StandardOutputEncoding。schtasks/netsh 用的是控制台代码页,
        // 强行按 UTF-8 解码会乱码, 导致按文案判断成败时失效(尤其是非中文系统)。
        public static string RunCmd(string file, string args, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                try { exitCode = p.ExitCode; } catch { }
                return o;
            }
            catch (Exception ex) { return "ERR: " + ex.Message; }
        }
    }
}
