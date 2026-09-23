package cn.edu.sicau.autologin;

import android.Manifest;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.location.LocationManager;
import android.net.wifi.ScanResult;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;

import java.util.List;

/**
 * 扫周围的 WiFi，给界面上那个「选择」列表用。
 *
 * ⚠️ 安卓对扫描的限制（写清楚，免得以为是程序坏了）：
 *   1) 要位置类权限: 安卓 12 及以下要 ACCESS_FINE_LOCATION，安卓 13+ 要 NEARBY_WIFI_DEVICES；
 *   2) **系统定位开关必须打开**（安卓 10 起），否则 startScan 不报错但一条结果都不给；
 *   3) 安卓 10 起还有「每次扫描要用户在场」的限流（几秒内只能扫一次，快速连点会被系统挡掉）；
 *   4) **安卓 12 起，扫到的列表被系统过滤**：普通应用只能看到「已保存过的」网络，
 *      周围一堆从没连过的 WiFi 是看不到的。所以列表里大概率只有校园网和你连过的别人家网络 ——
 *      这正是为什么界面上还留着手动输入。
 *
 * 结论：扫描当"帮我省点打字"的辅助，真正可靠的还是让用户手动填一次（或者直接点「连接」）。
 */
public class WifiScan {

    /** 扫描结果按信号从强到弱、同名合并之后回调（主线程）。 */
    public interface Listener {
        void onResult(List<ScanResult> list, String message);
    }

    private static final int SCAN_WAIT_MS = 6000;
    private static BroadcastReceiver receiver;

    private WifiScan() { }

    /** 系统定位开关开着吗？安卓 10 起没这个开关就扫不到东西。 */
    public static boolean locationOn(Context ctx) {
        try {
            LocationManager lm = (LocationManager) ctx.getSystemService(Context.LOCATION_SERVICE);
            if (lm == null) return true;
            if (Build.VERSION.SDK_INT >= 28) return lm.isLocationEnabled();
            return lm.isProviderEnabled(LocationManager.GPS_PROVIDER)
                    || lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER);
        } catch (Throwable t) {
            return true;
        }
    }

    public static void openLocationSettings(Context ctx) {
        try {
            Intent it = new Intent(Settings.ACTION_LOCATION_SOURCE_SETTINGS);
            it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            ctx.startActivity(it);
        } catch (Throwable t) { }
    }

    /**
     * 扫描需要的运行时权限（跟 {@link WifiSwitch#neededPermissions()} 是同一套规则：
     * 安卓 13+ 只给 NEARBY_WIFI_DEVICES，12 及以下才是 ACCESS_FINE_LOCATION）。
     */
    public static String[] neededPermissions() {
        if (Build.VERSION.SDK_INT >= 33) {
            return new String[] { Manifest.permission.NEARBY_WIFI_DEVICES };
        }
        if (Build.VERSION.SDK_INT >= 23) {
            return new String[] { Manifest.permission.ACCESS_FINE_LOCATION };
        }
        return new String[0];
    }

    /** 权限齐不齐。不齐就把缺的那个返回回去，让界面去申请。 */
    public static String missingPermission(Context ctx) {
        try {
            if (Build.VERSION.SDK_INT >= 33) {
                if (ctx.checkSelfPermission(Manifest.permission.NEARBY_WIFI_DEVICES)
                        != PackageManager.PERMISSION_GRANTED) {
                    return Manifest.permission.NEARBY_WIFI_DEVICES;
                }
                return null;
            }
            if (Build.VERSION.SDK_INT >= 23
                    && ctx.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                       != PackageManager.PERMISSION_GRANTED) {
                return Manifest.permission.ACCESS_FINE_LOCATION;
            }
        } catch (Throwable t) {
            return null;
        }
        return null;
    }

    /**
     * 扫一次。回调一定会来（成功给列表，失败给一句人话说明为什么）。
     * 不用 startScan 的返回值当结果，因为很多 ROM 上它先返回 true、结果晚几秒才来，
     * 这里两种都兜：等 SCAN_SCAN_RESULTS_AVAILABLE 广播，超时就再读一次缓存。
     */
    public static void scan(final Context ctx, final Listener cb) {
        final Handler main = new Handler(Looper.getMainLooper());
        final WifiManager wm = (WifiManager) ctx.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        if (wm == null) { cb.onResult(null, "这台设备没有 WiFi 模块"); return; }

        String miss = missingPermission(ctx);
        if (miss != null) {
            cb.onResult(null, "还没有给「附近的 WiFi 设备 / 位置信息」权限，扫不了。可以点界面上的「连接」直接连");
            return;
        }
        if (!wm.isWifiEnabled()) {
            cb.onResult(null, "WiFi 开关是关着的，先打开 WiFi 再扫");
            return;
        }
        if (!locationOn(ctx)) {
            cb.onResult(null, "系统「定位」开关没开，安卓从 10 开始就不给扫描结果了。可以到系统设置里打开定位，或者直接点「连接」");
            return;
        }

        // 上一次没结束的接收器先摘掉
        stopReceiver(ctx);

        final boolean[] done = new boolean[1];
        final Runnable finish = new Runnable() {
            public void run() {
                synchronized (done) {
                    if (done[0]) return;
                    done[0] = true;
                }
                stopReceiver(ctx);
                List<ScanResult> raw;
                try { raw = wm.getScanResults(); } catch (Throwable t) { raw = null; }
                final List<ScanResult> merged = merge(raw);
                final String msg = merged.isEmpty()
                        ? "没扫到 WiFi。可能周围信号弱、系统刚扫过还在限流（等几秒再点一下），或者这台手机不允许应用看附近网络 —— 直接点「连接」或手动填名字都行"
                        : null;
                main.post(new Runnable() {
                    public void run() { cb.onResult(merged, msg); }
                });
            }
        };

        try {
            receiver = new BroadcastReceiver() {
                @Override
                public void onReceive(Context c, Intent i) {
                    main.removeCallbacks(finish);
                    main.postDelayed(finish, 400);   // 等系统把结果写进去
                }
            };
            ctx.registerReceiver(receiver, new IntentFilter(WifiManager.SCAN_RESULTS_AVAILABLE_ACTION));
        } catch (Throwable t) {
            receiver = null;
        }

        main.postDelayed(finish, SCAN_WAIT_MS);      // 超时兜底
        try {
            boolean started = wm.startScan();
            if (!started) {
                // 有的 ROM 返回 false 但其实已经在扫了, 所以不直接放弃, 只把等待时间缩短
                AppLog.write("WARN", "系统说这次扫描启动失败(可能在限流), 还是等一下结果");
            }
        } catch (Throwable t) {
            AppLog.write("WARN", "启动扫描失败: " + t);
        }
    }

    private static void stopReceiver(Context ctx) {
        BroadcastReceiver r = receiver;
        receiver = null;
        if (r == null) return;
        try { ctx.unregisterReceiver(r); } catch (Throwable t) { }
    }

    /**
     * 整理扫描结果：同一个 WiFi 名（多个接入点）只留信号最强的那条，然后按信号从强到弱排。
     * 真正的去重/排序逻辑在 {@link WifiPlan#mergeKeepStrongest} 里（纯 Java，有单元测试）。
     */
    static List<ScanResult> merge(List<ScanResult> raw) {
        return WifiPlan.mergeKeepStrongest(raw,
                new WifiPlan.Key<ScanResult>() {
                    public String of(ScanResult r) { return r.SSID; }
                },
                new WifiPlan.Score<ScanResult>() {
                    public int of(ScanResult r) { return r.level; }
                });
    }

    /** 信号格数 -> 一句人话。 */
    public static String levelText(int level) {
        if (level >= -55) return "信号很好";
        if (level >= -70) return "信号一般";
        if (level >= -85) return "信号较弱";
        return "信号很弱";
    }

    /** 名字是不是 WPA/WPA2/WPA3 加密的。 */
    public static boolean isSecured(ScanResult r) {
        if (r == null) return false;
        String caps = r.capabilities == null ? "" : r.capabilities;
        return caps.contains("WPA") || caps.contains("WEP") || caps.contains("EAP")
                || caps.contains("SAE") || caps.contains("OWE");
    }
}
