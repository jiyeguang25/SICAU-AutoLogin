package cn.edu.sicau.autologin;

import android.Manifest;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.net.wifi.WifiConfiguration;
import android.net.wifi.WifiInfo;
import android.net.wifi.WifiManager;
import android.net.wifi.WifiNetworkSpecifier;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;

import java.util.List;

/**
 * 切换到指定的校园 WiFi。
 *
 * ⚠️ 先说清楚一件必须知道的事(这是安卓系统的限制, 不是本程序的毛病):
 *   **Android 10 (API 29) 起, 普通应用不能再"悄悄"改 WiFi 了。**
 *   老办法 WifiManager.addNetwork()/enableNetwork() 从 Android 10 起被禁(直接返回 -1),
 *   Android 11 起连改配置都要走系统弹窗。想让手机连到某个 WiFi, 只能:
 *     · 让系统弹一个"是否连接到 xxx"的确认框, 用户点一下(本类在 Android 10+ 用的就是这个:
 *       WifiNetworkSpecifier + ConnectivityManager.requestNetwork);
 *     · 或者跳到系统 WiFi 列表让用户自己点(兜底, 见 openSettings())。
 *   所以界面上那个按钮叫「连接」而不是「静默切换」—— 一定会看到系统弹窗, 点「连接」即可。
 *
 * Android 9 及以下仍然可以静默连(老 API), 这段也保留着。
 *
 * 连接成功后会把整个进程的网络绑到这块 WiFi 网卡上
 * (ConnectivityManager.bindProcessToNetwork), 这样认证请求一定走 WiFi 而不是移动流量。
 */
public class WifiSwitch {

    /** 回调都保证发生在主线程。 */
    public interface Callback {
        /** 本来就连着目标 WiFi(或刚被系统自动切过去), 不用弹窗。who 用来拼日志。 */
        void onAlreadyConnected(String ssid, String ip, String who);
        void onConnected(String ssid, String ip);
        /** 被用户取消, 或者系统没让我们连。 */
        void onDenied(String message);
        void onFailed(String message);
    }

    /** 系统弹窗最多等这么久, 超时就算失败(用户可能直接划掉不理)。 */
    public static final int TIMEOUT_MS = 75000;

    // 保持静态引用: 一旦被回收, 系统会认为这个请求结束了, 网络会被收回。
    private static ConnectivityManager.NetworkCallback activeCallback;
    private static Network boundNetwork;

    private WifiSwitch() { }

    // ================================================================
    //  权限
    // ================================================================
    /**
     * 读 WiFi 名字需要的运行时权限。
     *
     * ⚠️ 这里必须按版本给, 不能两个都要当"必需":
     *   Android 13+  : NEARBY_WIFI_DEVICES 是扫描/连接必需; ACCESS_FINE_LOCATION 只是
     *                  "读当前 SSID"用, 用户不给也得让程序能往下跑（所以它算可选, 见 optionalPermission）。
     *   Android 6~12 : ACCESS_FINE_LOCATION。
     */
    public static String[] neededPermissions() {
        if (Build.VERSION.SDK_INT >= 33) {
            return new String[] { Manifest.permission.NEARBY_WIFI_DEVICES };
        }
        if (Build.VERSION.SDK_INT >= 23) {
            return new String[] { Manifest.permission.ACCESS_FINE_LOCATION };
        }
        return new String[0];   // Android 6 以下装的时候一次给全, 不用运行时申请
    }

    /**
     * 可选权限: 安卓 13+ 上再要一个位置权限, 用来读当前 WiFi 的名字。
     * 拿不到也能用（认网络靠 IP）, 只是界面上看不到名字。
     */
    public static String[] optionalPermissions() {
        if (Build.VERSION.SDK_INT >= 33) {
            return new String[] { Manifest.permission.ACCESS_FINE_LOCATION };
        }
        return new String[0];
    }

    /** 全部要申请的权限（必需 + 可选）。 */
    public static String[] allPermissions() {
        String[] a = neededPermissions();
        String[] b = optionalPermissions();
        String[] all = new String[a.length + b.length];
        System.arraycopy(a, 0, all, 0, a.length);
        System.arraycopy(b, 0, all, a.length, b.length);
        return all;
    }

    /** 还缺哪个**必需**权限就返回哪个, 都齐了返回 null。 */
    public static String missingPermission(Context ctx) {
        String[] need = neededPermissions();
        for (int i = 0; i < need.length; i++) {
            try {
                if (ctx.checkSelfPermission(need[i]) != PackageManager.PERMISSION_GRANTED) return need[i];
            } catch (Throwable t) {
                return need[i];
            }
        }
        return null;
    }

    /** 位置权限给了没（安卓 13+ 只影响能不能读 WiFi 名字）。 */
    public static boolean hasLocationPermission(Context ctx) {
        try {
            if (Build.VERSION.SDK_INT < 23) return true;
            return ctx.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                    == PackageManager.PERMISSION_GRANTED;
        } catch (Throwable t) {
            return false;
        }
    }

    /** 权限已经给全了吗(空数组算给全)。 */
    public static boolean hasAllPermissions(Context ctx) {
        String[] need = neededPermissions();
        return need.length == 0 || missingPermission(ctx) == null;
    }

    // ================================================================
    //  读当前状态
    // ================================================================
    /** 当前连着的 WiFi 名字; 拿不到(没权限/没连)返回空串。 */
    public static String currentSsid(Context ctx) {
        try {
            WifiManager wm = (WifiManager) ctx.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
            if (wm == null) return "";
            WifiInfo wi = wm.getConnectionInfo();
            if (wi == null) return "";
            String s = wi.getSSID();
            if (s == null) return "";
            if (s.equals("<unknown ssid>") || s.equals("unknown")) return "";
            return WifiPlan.clean(s);
        } catch (Throwable t) {
            return "";
        }
    }

    /**
     * 读当前连着的 WiFi 名字。**读不到就返回空串, 绝不抛异常**。
     *
     * ⚠️ 这事儿在不同安卓版本/不同手机上差别极大, 所以准备了三层:
     *   1) WifiManager.getConnectionInfo().getSSID()
     *      —— 安卓 8.1 起要位置权限; 安卓 10 起还要**系统定位开关打开**;
     *         安卓 12 起就算有权限, 部分 ROM 也只给 <unknown ssid>。
     *   2) NetworkCapabilities.getTransportInfo() 转 WifiInfo (API 30+)
     *      —— 有些机器上第一条被抹掉了, 这条还给。
     *   3) 从当前 WiFi Network 的 LinkProperties 里取 SSID (API 29+)
     *      —— getLinkProperties().getDomains() 里带 "wlan.xxx" 这种域的时候能捡到。
     * 都读不到就认了, 界面上改用 IP 显示（认网络靠 IP, 不靠名字）。
     */
    public static String readSsid(Context ctx) {
        String ssid = currentSsid(ctx);
        if (!isEmptySsid(ssid)) return ssid;

        try {
            ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network[] nets = cm == null ? null : cm.getAllNetworks();
            for (int i = 0; nets != null && i < nets.length; i++) {
                NetworkCapabilities nc = cm.getNetworkCapabilities(nets[i]);
                if (nc == null || !nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) continue;

                // 2) TransportInfo
                if (Build.VERSION.SDK_INT >= 30) {
                    try {
                        android.net.TransportInfo ti = nc.getTransportInfo();
                        if (ti instanceof WifiInfo) {
                            String s = WifiPlan.clean(((WifiInfo) ti).getSSID());
                            if (!isEmptySsid(s)) return s;
                        }
                    } catch (Throwable t) { }
                }

                // 3) LinkProperties 的 domains（注意 getDomains() 返回的是一个字符串，可能逗号分隔）
                try {
                    LinkProperties lp = cm.getLinkProperties(nets[i]);
                    if (lp != null) {
                        String dom = lp.getDomains();
                        if (dom != null) {
                            String[] parts = dom.split(",");
                            for (int k = 0; k < parts.length; k++) {
                                String t = WifiPlan.clean(parts[k]);
                                if (t.startsWith("wlan.")) {
                                    t = WifiPlan.clean(t.substring("wlan.".length()));
                                    if (!isEmptySsid(t)) return t;
                                }
                            }
                        }
                    }
                } catch (Throwable t) { }
            }
        } catch (Throwable t) { }
        return "";
    }

    /** 系统的占位值不算名字。 */
    static boolean isEmptySsid(String s) {
        return WifiPlan.isEmptyName(s);
    }

    /**
     * 「读不到 WiFi 名字」到底卡在哪一步 —— 把能查的都查一遍打成几行文字,
     * 用户可以直接截图发出来, 不用连电脑抓 logcat。
     */
    public static String diagnose(Context ctx) {
        StringBuilder sb = new StringBuilder();
        sb.append("安卓版本: ").append(Build.VERSION.RELEASE)
          .append(" (API ").append(Build.VERSION.SDK_INT).append(")")
          .append("  机型: ").append(Build.MANUFACTURER).append(' ').append(Build.MODEL).append('\n');

        sb.append("权限 附近的WiFi设备: ").append(permText(ctx, "android.permission.NEARBY_WIFI_DEVICES")).append('\n');
        sb.append("权限 位置信息: ").append(permText(ctx, Manifest.permission.ACCESS_FINE_LOCATION)).append('\n');

        boolean loc = WifiScan.locationOn(ctx);
        sb.append("系统定位开关: ").append(loc ? "已打开" : "**没打开**(安卓 10 起必须打开, 否则读不到 WiFi 名字)").append('\n');

        try {
            WifiManager wm = (WifiManager) ctx.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
            sb.append("WiFi 开关: ").append(wm != null && wm.isWifiEnabled() ? "已打开" : "**关着**").append('\n');
            if (wm != null) {
                WifiInfo wi = wm.getConnectionInfo();
                String raw = (wi == null) ? "(取不到)" : String.valueOf(wi.getSSID());
                sb.append("getSSID() 原始返回: ").append(raw).append('\n');
                if (wi != null) sb.append("getSSID() 长度: ").append(wi.getSSID() == null ? -1 : wi.getSSID().length()).append('\n');
                try {
                    sb.append("扫描结果条数: ").append(wm.getScanResults() == null ? "null" : String.valueOf(wm.getScanResults().size())).append('\n');
                } catch (Throwable t) {
                    sb.append("扫描结果条数: 取不到(").append(t.getClass().getSimpleName()).append(")\n");
                }
            }
        } catch (Throwable t) {
            sb.append("WiFi 服务: 取不到 ").append(t).append('\n');
        }

        String ssid = readSsid(ctx);
        String ip = currentIp(ctx);
        sb.append("三层读法结果: ").append(ssid.length() > 0 ? ssid : "(还是空)").append('\n');
        sb.append("WiFi IP: ").append(ip.length() > 0 ? ip : "(取不到 —— 可能没连 WiFi)").append('\n');

        try {
            ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network[] nets = cm == null ? null : cm.getAllNetworks();
            int nWifi = 0;
            for (int i = 0; nets != null && i < nets.length; i++) {
                NetworkCapabilities nc = cm.getNetworkCapabilities(nets[i]);
                if (nc != null && nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) nWifi++;
            }
            sb.append("WiFi 类型的网络个数: ").append(nWifi).append('\n');
        } catch (Throwable t) { }

        sb.append("→ 结论: ");
        if (ssid.length() > 0) {
            sb.append("能读到名字, 一切正常。");
        } else if (ip.length() > 0) {
            sb.append("读不到名字但**有 WiFi IP** —— 认证不受影响, 程序会按 IP 认网络。")
              .append("名字读不到一般是: 系统定位没开 / 位置权限没给 / 这个 ROM 就是不给。");
        } else {
            sb.append("既没名字也没 IP, 说明现在根本没连上 WiFi。先去系统设置里连上校园 WiFi。");
        }
        return sb.toString();
    }

    private static String permText(Context ctx, String perm) {
        try {
            if (Build.VERSION.SDK_INT < 23) return "系统自动授予";
            return ctx.checkSelfPermission(perm) == PackageManager.PERMISSION_GRANTED ? "已授予" : "**没给**";
        } catch (Throwable t) {
            return "查不了";
        }
    }

    /** 取某张网卡上的 IPv4; net 传 null 就找默认那张 WiFi 卡。 */
    private static String ipv4Of(Context ctx, Network net) {
        try {
            ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm == null) return "";
            if (net != null) {
                String s = pickIpv4(cm.getLinkProperties(net));
                if (s.length() > 0) return s;
            }
            Network[] nets = cm.getAllNetworks();
            for (int i = 0; nets != null && i < nets.length; i++) {
                NetworkCapabilities nc = cm.getNetworkCapabilities(nets[i]);
                if (nc == null || !nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) continue;
                String s = pickIpv4(cm.getLinkProperties(nets[i]));
                if (s.length() > 0) return s;
            }
        } catch (Throwable t) { }
        return "";
    }

    private static String pickIpv4(LinkProperties lp) {
        if (lp == null) return "";
        for (LinkAddress la : lp.getLinkAddresses()) {
            java.net.InetAddress a = la.getAddress();
            if (a instanceof java.net.Inet4Address && !a.isLoopbackAddress()) {
                return a.getHostAddress();
            }
        }
        return "";
    }

    /** 当前 WiFi 网卡的 IPv4; 拿不到返回空串。 */
    public static String currentIp(Context ctx) {
        return ipv4Of(ctx, boundNetwork);
    }

    /** 跳系统 WiFi 列表/面板, 让用户自己点。 */
    public static void openSettings(Context ctx) {
        openIntent(ctx, wifiSettingsIntent(), "打不开系统 WiFi 设置");
    }

    /** 跳到本应用的"通知"设置页 —— 关掉「登录到网络」这类提醒用。 */
    public static void openNotificationSettings(Context ctx) {
        Intent it;
        if (Build.VERSION.SDK_INT >= 26) {
            it = new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS);
            it.putExtra(Settings.EXTRA_APP_PACKAGE, ctx.getPackageName());
        } else if (Build.VERSION.SDK_INT >= 21) {
            it = new Intent("android.settings.APP_NOTIFICATION_SETTINGS");
            it.putExtra("app_package", ctx.getPackageName());
            it.putExtra("app_uid", ctx.getApplicationInfo().uid);
        } else {
            it = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            it.setData(android.net.Uri.parse("package:" + ctx.getPackageName()));
        }
        openIntent(ctx, it, "打不开通知设置");
    }

    private static Intent wifiSettingsIntent() {
        if (Build.VERSION.SDK_INT >= 29) return new Intent(Settings.Panel.ACTION_WIFI);
        return new Intent(Settings.ACTION_WIFI_SETTINGS);
    }

    private static void openIntent(Context ctx, Intent it, String failMsg) {
        try {
            it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            ctx.startActivity(it);
        } catch (Throwable t) {
            try {
                Intent fallback = new Intent(Settings.ACTION_WIFI_SETTINGS);
                fallback.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                ctx.startActivity(fallback);
            } catch (Throwable t2) {
                AppLog.write("ERROR", failMsg + ": " + t2);
            }
        }
    }

    // ================================================================
    //  连接
    // ================================================================
    /**
     * 请求连接到 targets 里的任意一个 WiFi(系统会把候选一起放进弹窗让你选)。
     *
     * 已经连着其中一个的话直接回调 onAlreadyConnected, 不弹窗。
     * 否则 Android 10+ 弹系统确认框; Android 9 及以下静默连。
     */
    public static void connect(final Context ctx, final List<String> targets, final Callback cb) {
        connect(ctx, targets, readSsid(ctx), cb);
    }

    /**
     * 同上, 但当前 SSID 由调用方给（避免重复去读, 也方便测试）。
     * curSsid 为空表示"读不到当前 WiFi 名字" —— 这时不当作"已经在目标上", 直接申请连接。
     */
    public static void connect(final Context ctx, final List<String> targets, String curSsid, final Callback cb) {
        if (targets == null || targets.isEmpty()) {
            cb.onFailed("没有填写要连接的 WiFi 名称");
            return;
        }

        String cur = WifiPlan.clean(curSsid);
        if (!isEmptySsid(cur) && WifiPlan.matchesAny(targets, cur)) {
            cb.onAlreadyConnected(cur, currentIp(ctx), "已经在这个 WiFi 上");
            return;
        }

        if (Build.VERSION.SDK_INT >= 29) connectModern(ctx, targets, cb);
        else connectLegacy(ctx, targets, cb);
    }

    // ------------------------------------------------ Android 10+
    /**
     * Android 10 起的正规做法: 用 WifiNetworkSpecifier 向系统申请这块 WiFi,
     * 系统会弹一个确认框, 用户点「连接」后才真正连上。
     */
    private static void connectModern(final Context ctx, final List<String> targets, final Callback cb) {
        final ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm == null) { cb.onFailed("取不到网络服务"); return; }

        releaseActive();   // 上一次没结束的请求先收掉, 免得回调串台

        final Handler main = new Handler(Looper.getMainLooper());
        final boolean[] settled = new boolean[1];

        ConnectivityManager.NetworkCallback nc = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onAvailable(Network n) {
                synchronized (settled) {
                    if (settled[0]) return;
                    settled[0] = true;
                }
                try { cm.bindProcessToNetwork(n); } catch (Throwable t) { }
                boundNetwork = n;
                // 这时候 WifiInfo 可能还没反应过来, 先给个短延时再读, 读不到就退回候选名
                final String ip = ipv4Of(ctx, n);
                final String name = describeTargets(targets);
                AppLog.i("WiFi 已连上: " + name + (ip.length() > 0 ? "（" + ip + "）" : ""));
                main.post(new Runnable() {
                    public void run() { cb.onConnected(name, ip); }
                });
            }

            @Override
            public void onUnavailable() {
                // 用户点了取消, 或者这块 WiFi 不在范围内
                synchronized (settled) {
                    if (settled[0]) return;
                    settled[0] = true;
                }
                releaseActive();
                AppLog.write("WARN", "系统没有连上 " + describeTargets(targets) + "(可能不在范围内, 或被取消)");
                main.post(new Runnable() {
                    public void run() {
                        cb.onDenied("没有连上 " + describeTargets(targets)
                                + "（可能是信号太弱，或者你在弹窗里点了取消）");
                    }
                });
            }

            @Override
            public void onLost(Network n) {
                AppLog.write("WARN", "WiFi 连接断开了: " + describeTargets(targets));
            }
        };

        NetworkRequest req;
        try {
            NetworkRequest.Builder rb = new NetworkRequest.Builder();
            rb.addTransportType(NetworkCapabilities.TRANSPORT_WIFI);
            // 校园网 WiFi 在认证前是不通的, 去掉 INTERNET 要求系统才肯让我们连
            rb.removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET);

            WifiNetworkSpecifier.Builder sb = new WifiNetworkSpecifier.Builder();
            // 系统只让设一个 SSID(或一个 SSID 模式), 所以这里只提交第一个候选。
            // 想换另一个名字, 把它挪到第一个就行; 候选里其余的名字仍然用于"已经连上了没"的判断。
            String first = targets.get(0);
            if (WifiPlan.isPattern(first)) {
                // 通配符交给系统的 glob 匹配。注意第二参数是 PATTERN_ADVANCED_GLOB:
                //   不传类型的话系统按字面量处理, 通配符就白写了;
                //   给正则也不行 —— 系统那边吃的是 glob(* 任意多个, . 任意一个字符)。
                sb.setSsidPattern(new android.os.PatternMatcher(
                        WifiPlan.toGlob(first), android.os.PatternMatcher.PATTERN_ADVANCED_GLOB));
            } else {
                sb.setSsid(first);
            }
            try { sb.setIsHiddenSsid(false); } catch (Throwable t) { }
            rb.setNetworkSpecifier(sb.build());
            req = rb.build();
        } catch (Throwable t) {
            AppLog.write("ERROR", "构造 WiFi 申请失败: " + t);
            cb.onFailed("这些 WiFi 名字没法提交给系统（名字里有不支持的字符？）");
            return;
        }

        if (targets.size() > 1) {
            AppLog.i("候选有 " + targets.size() + " 个, 这次只向系统申请第一个 "
                    + targets.get(0) + "(想换就把另一个挪到最前面)");
        }

        activeCallback = nc;
        try {
            cm.requestNetwork(req, nc);
            AppLog.i("已请求系统连接 " + describeTargets(targets) + ", 请在弹窗里点「连接」");
        } catch (Throwable t) {
            activeCallback = null;
            AppLog.write("ERROR", "请求连接 WiFi 失败: " + t);
            cb.onFailed("请求连接失败: " + t);
            return;
        }

        // 超时兜底: 用户把系统弹窗划掉时, onUnavailable 不一定回调
        main.postDelayed(new Runnable() {
            public void run() {
                synchronized (settled) {
                    if (settled[0]) return;
                    settled[0] = true;
                }
                releaseActive();
                AppLog.write("WARN", "等待连接超时(" + (TIMEOUT_MS / 1000) + " 秒)");
                cb.onFailed("等太久了，没连上 " + describeTargets(targets)
                        + "。可以试试右上角的「系统设置」自己手动连一次");
            }
        }, TIMEOUT_MS);
    }

    // ------------------------------------------------ Android 9 及以下
    /**
     * 老系统可以静默连: 把配置写进系统, 再 enableNetwork + reconnect。
     * 新系统上这条路已经封了, 所以只到 Android 9 才走。
     */
    @SuppressWarnings("deprecation")
    private static void connectLegacy(final Context ctx, final List<String> targets, final Callback cb) {
        final WifiManager wm = (WifiManager) ctx.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        if (wm == null) { cb.onFailed("取不到 WiFi 服务"); return; }

        String ssid = null;
        for (int i = 0; i < targets.size(); i++) {
            if (!WifiPlan.isPattern(targets.get(i))) { ssid = targets.get(i); break; }
        }
        if (ssid == null) {
            cb.onFailed("带通配符的名字在这个安卓版本上没法静默连接，请填完整的 WiFi 名称");
            return;
        }

        try {
            if (!wm.isWifiEnabled()) wm.setWifiEnabled(true);

            int netId = -1;
            List<WifiConfiguration> list = wm.getConfiguredNetworks();
            for (int i = 0; list != null && i < list.size(); i++) {
                WifiConfiguration c = list.get(i);
                if (c == null || c.SSID == null) continue;
                if (WifiPlan.clean(c.SSID).equalsIgnoreCase(ssid)) { netId = c.networkId; break; }
            }

            if (netId < 0) {
                WifiConfiguration c = new WifiConfiguration();
                c.SSID = "\"" + ssid + "\"";
                c.allowedKeyManagement.set(WifiConfiguration.KeyMgmt.NONE);
                netId = wm.addNetwork(c);
            }
            if (netId < 0) {
                cb.onFailed("系统不允许自动添加 WiFi，请用「系统设置」手动连接一次");
                return;
            }
            wm.disconnect();
            wm.enableNetwork(netId, true);
            wm.reconnect();
            AppLog.i("已请求连接 " + ssid + "(老系统静默连接)");

            new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
                public void run() {
                    String now = currentSsid(ctx);
                    if (WifiPlan.matchesAny(targets, now)) cb.onConnected(now, currentIp(ctx));
                    else cb.onFailed("没能连上 " + describeTargets(targets) + "，请用「系统设置」手动连一次");
                }
            }, 8000);
        } catch (Throwable t) {
            AppLog.write("ERROR", "连接 WiFi 失败: " + t);
            cb.onFailed("连接失败: " + t);
        }
    }

    /** 收掉还在挂着的请求(界面销毁 / 重新连接前调用)。 */
    public static void releaseActive() {
        ConnectivityManager.NetworkCallback c = activeCallback;
        activeCallback = null;
        boundNetwork = null;
        if (c == null) return;
        try {
            Context app = AppCtx.get();
            if (app != null) {
                ConnectivityManager cm = (ConnectivityManager) app.getSystemService(Context.CONNECTIVITY_SERVICE);
                if (cm != null) cm.unregisterNetworkCallback(c);
            }
        } catch (Throwable t) { }
    }

    /** 连上之后, 让认证请求也走这块 WiFi。 */
    public static boolean bindProcess(Context ctx, boolean on) {
        try {
            ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm == null) return false;
            return cm.bindProcessToNetwork(on ? boundNetwork : null);
        } catch (Throwable t) {
            return false;
        }
    }

    private static String describeTargets(List<String> targets) {
        return WifiPlan.describe(targets, 40);
    }
}
