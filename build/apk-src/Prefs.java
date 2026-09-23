package cn.edu.sicau.autologin;

import android.content.Context;
import android.content.SharedPreferences;

public class Prefs {
    public String userId = "";
    public String password = "";
    public String portalHost = "portal.sicau.edu.cn";
    public String wlanAcName = "WJ-C10-Bras01-ME60-X8A";
    public String nasIp = "5.5.5.18";
    public int intervalMinutes = 15;
    public boolean enabled = true;
    public boolean keepAlive = false;
    /** 要连接的 WiFi 名字(可多个, 逗号分隔, 支持 * ? 通配符)。 */
    public String wifiSsid = "";
    /** 打开界面 / 点认证时, 自动连到这个 WiFi。 */
    public boolean connectWifi = false;
    /**
     * 「减少登录提醒打扰」。
     *
     * ⚠️ 这只是记下用户的意愿：连上校园网后那条「登录到网络」提醒/弹窗是**安卓系统的
     * 强制门户检测**发的，第三方应用关不掉（要用户自己去系统设置里关通知，
     * 或者用 adb 关掉强制门户检测）。所以这个值目前只用来决定"要不要提示用户去关"。
     */
    public boolean quietPortal = false;

    public static Prefs load(Context ctx) {
        SharedPreferences sp = ctx.getSharedPreferences("sicau", Context.MODE_PRIVATE);
        Prefs p = new Prefs();
        p.userId = sp.getString("userId", "");
        p.password = sp.getString("password", "");
        p.portalHost = sp.getString("portalHost", p.portalHost);
        p.wlanAcName = sp.getString("wlanAcName", p.wlanAcName);
        p.nasIp = sp.getString("nasIp", p.nasIp);
        p.intervalMinutes = sp.getInt("intervalMinutes", 15);
        p.enabled = sp.getBoolean("enabled", true);
        p.keepAlive = sp.getBoolean("keepAlive", false);
        p.wifiSsid = sp.getString("wifiSsid", "");
        p.connectWifi = sp.getBoolean("connectWifi", false);
        p.quietPortal = sp.getBoolean("quietPortal", false);
        return p;
    }

    public void save(Context ctx) {
        SharedPreferences.Editor e = ctx.getSharedPreferences("sicau", Context.MODE_PRIVATE).edit();
        e.putString("userId", userId);
        e.putString("password", password);
        e.putString("portalHost", portalHost);
        e.putString("wlanAcName", wlanAcName);
        e.putString("nasIp", nasIp);
        e.putInt("intervalMinutes", intervalMinutes);
        e.putBoolean("enabled", enabled);
        e.putBoolean("keepAlive", keepAlive);
        e.putString("wifiSsid", wifiSsid);
        e.putBoolean("connectWifi", connectWifi);
        e.putBoolean("quietPortal", quietPortal);
        e.commit();
    }
}
