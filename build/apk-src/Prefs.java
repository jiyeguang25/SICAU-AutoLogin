package cn.edu.sicau.autologin;

import android.content.Context;
import android.content.SharedPreferences;

public class Prefs {
    public String userId = "";
    public String password = "";
    public String portalHost = Site.PORTAL_HOST;   // 域名的唯一来源: Site.java
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

    /** 老版本存明文密码用的键，只在迁移时读一次，迁移完立刻删掉。 */
    private static final String KEY_PWD_OLD = "password";
    /** 新键：Keystore 加密后的 Base64 密文（见 Secret.java）。 */
    private static final String KEY_PWD = "passwordEnc";

    public static Prefs load(Context ctx) {
        SharedPreferences sp = ctx.getSharedPreferences("sicau", Context.MODE_PRIVATE);
        Prefs p = new Prefs();
        p.userId = sp.getString("userId", "");

        // 密码：有密文就解密；只有明文（老配置）就先当明文用，结尾立刻加密回写。
        // 解不开（换手机 / 清了 Keystore）就当没存，界面上会显示成没填，重输一次即可。
        boolean needResave = false;
        p.password = Secret.decrypt(sp.getString(KEY_PWD, ""));
        String oldPlain = sp.getString(KEY_PWD_OLD, "");
        if (p.password.length() == 0 && Secret.isLegacyPlain(oldPlain)) {
            p.password = oldPlain;
            needResave = true;
        }

        p.portalHost = sp.getString("portalHost", p.portalHost);
        p.wlanAcName = sp.getString("wlanAcName", p.wlanAcName);
        p.nasIp = sp.getString("nasIp", p.nasIp);
        p.intervalMinutes = sp.getInt("intervalMinutes", 15);
        p.enabled = sp.getBoolean("enabled", true);
        p.keepAlive = sp.getBoolean("keepAlive", false);
        p.wifiSsid = sp.getString("wifiSsid", "");
        p.connectWifi = sp.getBoolean("connectWifi", false);
        p.quietPortal = sp.getBoolean("quietPortal", false);
        if (needResave) {
            try { p.save(ctx); } catch (Throwable ignored) { }   // 迁移失败不影响本次使用
        }
        return p;
    }

    public void save(Context ctx) {
        SharedPreferences.Editor e = ctx.getSharedPreferences("sicau", Context.MODE_PRIVATE).edit();
        e.putString("userId", userId);
        e.remove(KEY_PWD_OLD);   // 不管加密成不成功，旧明文键都得删，别在盘上留明文
        String enc = Secret.encrypt(ctx, password);
        if (enc.length() > 0) e.putString(KEY_PWD, enc);
        else e.remove(KEY_PWD);  // 加密失败就当没存，绝不退回明文
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
