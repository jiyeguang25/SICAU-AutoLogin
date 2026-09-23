package cn.edu.sicau.autologin;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.res.Configuration;
import android.graphics.drawable.GradientDrawable;
import android.net.wifi.ScanResult;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.util.TypedValue;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.List;

public class MainActivity extends Activity {

    /** 状态栏那行文字的语义, 决定用什么颜色(跟着主题走)。 */
    private static final int ST_NEUTRAL = 0, ST_OK = 1, ST_ERR = 2;

    private EditText etUser, etPass, etInterval, etWifiSsid;
    private CheckBox cbEnabled, cbKeepAlive, cbWifiAuto, cbQuietPortal;
    private TextView tvStatus, tvLog, title, sub, tvWifi;
    private Button btnSave, btnAuth, btnWifiConnect;
    private LinearLayout cardAccount, cardRun, cardLog;
    private ScrollView logScroll;

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final StringBuilder logBuf = new StringBuilder();

    /** 日志在屏幕上最多留这么多字符(再往前的老日志只留在文件里)。 */
    private static final int LOG_MAX = 8000;
    /** 权限申请用的请求码。 */
    private static final int REQ_WIFI_PERM = 200;
    /** 因为缺权限被推迟的动作。 */
    private static final int PENDING_NONE = 0;
    /** 只连 WiFi。 */
    private static final int PENDING_CONNECT = 1;
    /** 连 WiFi 后认证。 */
    private static final int PENDING_AUTH = 2;
    /** 连 WiFi 后强制认证。 */
    private static final int PENDING_AUTH_FORCE = 3;
    /** 拿到权限后接着弹「选择 WiFi」列表。 */
    private static final int PENDING_PICK = 4;
    private int pendingWifiAction = PENDING_NONE;
    private boolean pendingForce = false;

    // ================================================================
    //  配色: 跟随系统深色/浅色, 与 Windows 版保持一致
    // ================================================================
    private boolean dark;
    private int BG, CARD, FIELD, INK, INK_DIM, LINE, ACCENT, ACCENT_INK, OKC, ERRC;

    private void applyPalette() {
        int mode = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        dark = (mode == Configuration.UI_MODE_NIGHT_YES);
        if (dark) {
            BG = 0xFF202020; CARD = 0xFF2B2B2B; FIELD = 0xFF212121;
            INK = 0xFFFFFFFF; INK_DIM = 0xFFB0B0B0; LINE = 0xFF3A3A3A;
            ACCENT = 0xFF60CDFF; ACCENT_INK = 0xFF001A2A;
            OKC = 0xFF6CCB5F; ERRC = 0xFFFF99A4;
        } else {
            BG = 0xFFF3F3F3; CARD = 0xFFFFFFFF; FIELD = 0xFFFFFFFF;
            INK = 0xFF1A1A1A; INK_DIM = 0xFF616161; LINE = 0xFFE0E0E0;
            ACCENT = 0xFF0067C0; ACCENT_INK = 0xFFFFFFFF;
            OKC = 0xFF0F7B0F; ERRC = 0xFFC42B1C;
        }
    }

    private int dp(float v) {
        return (int) (v * getResources().getDisplayMetrics().density + 0.5f);
    }

    private GradientDrawable round(int fill, int stroke, float radiusDp, float strokeDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fill);
        d.setCornerRadius(dp(radiusDp));
        if (strokeDp > 0) d.setStroke(dp(strokeDp), stroke);
        return d;
    }

    /**
     * 把一张方形头像资源裁成圆形。
     *
     * 资源用 JPEG(比带透明的圆形 PNG 小 6 倍), 圆形在这里裁 —— RoundedBitmapDrawable
     * 是 API 21 起就有的系统类, 不用引任何第三方库(项目一直保持零依赖)。
     */
    private android.graphics.drawable.Drawable circleAvatar(int resId, float sizeDp) {
        try {
            int px = dp(sizeDp);
            android.graphics.Bitmap src = android.graphics.BitmapFactory.decodeResource(getResources(), resId);
            if (src == null) return null;
            android.graphics.Bitmap dst = android.graphics.Bitmap.createBitmap(px, px,
                    android.graphics.Bitmap.Config.ARGB_8888);
            android.graphics.Canvas c = new android.graphics.Canvas(dst);
            android.graphics.Paint p = new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
            p.setShader(new android.graphics.BitmapShader(src,
                    android.graphics.Shader.TileMode.CLAMP, android.graphics.Shader.TileMode.CLAMP));
            float r = px / 2f;
            c.drawCircle(r, r, r, p);
            return new android.graphics.drawable.BitmapDrawable(getResources(), dst);
        } catch (Throwable t) {
            // 裁圆失败也不能让界面崩, 退回方形显示
            try { return getResources().getDrawable(resId); } catch (Throwable t2) { return null; }
        }
    }

    // ================================================================
    //  WiFi 状态与切换
    //  探测和认证都绑定到这个 IP, 让请求走 WiFi 而不是移动流量,
    //  这样开着流量时也不会误判成"已联网"。
    // ================================================================
    /**
     * 取 WiFi 网卡的本地 IPv4。
     *
     * 注意: 以前只用 WifiInfo.getIpAddress(), 而它在 Android 12(API 31) 起已被废弃、
     * 固定返回 0 —— 于是这里永远返回空串, 请求就不会绑定到 WiFi, 会走移动数据的默认路由。
     * 后果是: 开着流量时 testOnline() 走流量返回 true, 直接误判成"已联网, 无需认证";
     * 或者认证请求从流量发出去, 门户认不出这个 IP。表现为"能打开但认证不成功"。
     */
    private String wifiIpv4() {
        return WifiSwitch.currentIp(this);
    }

    private String wifiSsid() {
        return WifiSwitch.readSsid(this);
    }

    private void openWifiSettings() {
        WifiSwitch.openSettings(this);
    }

    /**
     * 把界面上填的目标 WiFi 读成候选列表(支持多个名字和通配符 * ?)。
     * 没填就返回空列表。
     */
    private java.util.List<String> wifiTargets() {
        if (etWifiSsid == null) return new java.util.ArrayList<String>();
        return WifiPlan.parseCandidates(etWifiSsid.getText().toString());
    }

    /**
     * 刷新 WiFi 状态行。
     *
     * 关键点: **读不到 WiFi 名字也算正常**（安卓 12 起不少 ROM 就是不给）。
     * 只要有 WiFi 的 IP, 认证就能正常跑 —— 所以这里不报错, 只说明"系统没给名字, 用 IP 认"。
     */
    private void refreshWifi() {
        if (tvWifi == null) return;
        String ssid = WifiSwitch.readSsid(this);
        String ip = WifiSwitch.currentIp(this);
        List<String> targets = wifiTargets();

        if (ssid.length() > 0) {
            String extra = "";
            if (targets.size() > 0) {
                extra = WifiPlan.matchesAny(targets, ssid) ? "（目标 WiFi）" : "（不是目标 WiFi）";
            }
            tvWifi.setText("WiFi：" + ssid + (ip.length() > 0 ? "  " + ip : "") + extra);
            boolean hit = targets.size() == 0 || WifiPlan.matchesAny(targets, ssid);
            tvWifi.setTextColor(hit ? INK_DIM : ERRC);
        } else if (ip.length() > 0) {
            tvWifi.setText("已连 WiFi（" + ip + "），系统没给 WiFi 名字 —— 不影响认证，程序按 IP 认");
            tvWifi.setTextColor(INK_DIM);
        } else {
            tvWifi.setText("未连接 WiFi");
            tvWifi.setTextColor(ERRC);
        }
    }

    /** 把「为什么读不到 WiFi 名字」摊开给人看，用户直接截图就能反馈。 */
    private void showWifiDiag() {
        final String text = WifiSwitch.diagnose(this);
        AppLog.i("WiFi 诊断:\n" + text);
        new AlertDialog.Builder(this)
                .setTitle("WiFi 诊断（截图发我就行）")
                .setMessage(text)
                .setPositiveButton("知道了", null)
                .show();
    }

    // ================================================================
    //  生命周期
    // ================================================================
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        AppCtx.set(this);
        applyPalette();
        AppLog.init(getFilesDir());
        buildUi();
        loadToUi();

        AppLog.setListener(new AppLog.Listener() {
            public void onLog(final String line) {
                ui.post(new Runnable() { public void run() { appendLog(line); } });
            }
        });
        // 把之前(比如常驻服务在后台跑的时候)写下的日志补上, 免得打开界面是一片空白
        for (String l : AppLog.recent()) appendLog(l);
        setStatus("就绪", ST_NEUTRAL);
        askWifiPermissions(false);
    }

    @Override
    protected void onDestroy() {
        // 界面没了就别再占着系统的 WiFi 申请回调了, 否则那块网络会一直被本进程绑着
        WifiSwitch.releaseActive();
        super.onDestroy();
    }

    @Override
    protected void onResume() {
        super.onResume();
        loadToUi();
        refreshWifi();
        askWifiPermissions(false);
    }

    /**
     * 系统切换深色/浅色时不重建 Activity(免得输了一半的账号被清掉),
     * 而是把界面按新配色重新搭一遍, 再把原有内容填回去。
     */
    @Override
    public void onConfigurationChanged(Configuration nc) {
        super.onConfigurationChanged(nc);

        String u  = etUser     == null ? "" : etUser.getText().toString();
        String p  = etPass     == null ? "" : etPass.getText().toString();
        String iv = etInterval == null ? "" : etInterval.getText().toString();
        String ws = etWifiSsid == null ? "" : etWifiSsid.getText().toString();
        boolean en = cbEnabled   != null && cbEnabled.isChecked();
        boolean ka = cbKeepAlive != null && cbKeepAlive.isChecked();
        boolean wa = cbWifiAuto  != null && cbWifiAuto.isChecked();
        boolean qp = cbQuietPortal != null && cbQuietPortal.isChecked();
        String st  = tvStatus  == null ? "" : tvStatus.getText().toString();
        String lg  = logBuf.toString();

        applyPalette();
        buildUi();

        if (u.length()  > 0) etUser.setText(u);
        if (p.length()  > 0) etPass.setText(p);
        if (iv.length() > 0) etInterval.setText(iv);
        if (ws.length() > 0) etWifiSsid.setText(ws);
        cbEnabled.setChecked(en);
        cbKeepAlive.setChecked(ka);
        cbWifiAuto.setChecked(wa);
        cbQuietPortal.setChecked(qp);
        tvLog.setText(lg);
        scrollLogToBottom();
        setStatusRaw(st);
    }

    // ================================================================
    //  界面
    // ================================================================
    private TextView label(String text, float size, int color) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, size);
        t.setTextColor(color);
        t.setPadding(0, dp(8), 0, dp(3));
        return t;
    }

    private LinearLayout card() {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setBackground(round(CARD, LINE, 10, 1));
        c.setPadding(dp(14), dp(12), dp(14), dp(12));
        LinearLayout.LayoutParams lp =
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 0, 0, dp(10));
        c.setLayoutParams(lp);
        return c;
    }

    private EditText field(int inputType) {
        EditText e = new EditText(this);
        e.setInputType(inputType);
        e.setSingleLine(true);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        e.setTextColor(INK);
        e.setHintTextColor(INK_DIM);
        e.setBackground(round(FIELD, LINE, 6, 1));
        e.setPadding(dp(10), dp(8), dp(10), dp(8));
        return e;
    }

    private CheckBox check(String text) {
        CheckBox c = new CheckBox(this);
        c.setText(text);
        c.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        c.setTextColor(INK);
        c.setPadding(0, dp(6), 0, dp(2));
        return c;
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(BG);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(BG);
        root.setPadding(dp(16), dp(16), dp(16), dp(20));
        scroll.addView(root, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // ---- 标题（左边小头像 + 名称，下面作者标识）----
        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setOrientation(LinearLayout.HORIZONTAL);
        titleRow.setGravity(android.view.Gravity.CENTER_VERTICAL);

        ImageView avatar = new ImageView(this);
        avatar.setImageDrawable(circleAvatar(R.drawable.ic_avatar, 38));
        int av = dp(38);
        avatar.setLayoutParams(new LinearLayout.LayoutParams(av, av));
        titleRow.addView(avatar);

        LinearLayout titleText = new LinearLayout(this);
        titleText.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams ttlp =
                new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        ttlp.setMargins(dp(10), 0, 0, 0);
        titleText.setLayoutParams(ttlp);

        title = new TextView(this);
        title.setText("校园网自动认证");
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        title.setTextColor(INK);
        title.setTypeface(title.getTypeface(), android.graphics.Typeface.BOLD);
        titleText.addView(title);

        TextView byline = new TextView(this);
        byline.setText("极夜光  ·  yeguang225@outlook.com");
        byline.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        byline.setTextColor(INK_DIM);
        byline.setPadding(0, dp(2), 0, 0);
        titleText.addView(byline);

        titleRow.addView(titleText);
        root.addView(titleRow);

        sub = new TextView(this);
        sub.setText("portal.sicau.edu.cn  ·  开机自动登录，不用再手动认证");
        sub.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        sub.setTextColor(INK_DIM);
        // 左边空出头像那一条(38dp + 10dp 间距), 让两行文字对齐
        sub.setPadding(dp(48), dp(4), 0, dp(14));
        root.addView(sub);

        // ---- WiFi 状态 + 切换（一个按钮；诊断改成点状态文字，长按也行）----
        LinearLayout wifiRow = new LinearLayout(this);
        wifiRow.setOrientation(LinearLayout.HORIZONTAL);
        wifiRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        wifiRow.setPadding(0, 0, 0, dp(6));

        tvWifi = new TextView(this);
        tvWifi.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        tvWifi.setTextColor(INK_DIM);
        tvWifi.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        // 点/长按状态文字 = 诊断（这样就不用再占一个按钮）
        tvWifi.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { showWifiDiag(); }
        });
        wifiRow.addView(tvWifi);

        btnWifiConnect = new Button(this);
        btnWifiConnect.setText("连接 WiFi");
        btnWifiConnect.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        btnWifiConnect.setAllCaps(false);
        btnWifiConnect.setBackground(round(CARD, LINE, 6, 1));
        btnWifiConnect.setTextColor(INK);
        btnWifiConnect.setPadding(dp(12), dp(6), dp(12), dp(6));
        LinearLayout.LayoutParams wlp1 =
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        wlp1.setMargins(dp(6), 0, 0, 0);
        btnWifiConnect.setLayoutParams(wlp1);
        btnWifiConnect.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { doConnectWifi(true); }
        });
        wifiRow.addView(btnWifiConnect);
        root.addView(wifiRow);

        // ---- 账号卡片（WiFi 名字 + 学号 + 密码）----
        cardAccount = card();
        cardAccount.addView(label("要连接的 WiFi 名称", 13, INK_DIM));

        // 校园网两个名字做成按钮：点一下就填上并连，不用打字、不用等扫描
        LinearLayout presetRow = new LinearLayout(this);
        presetRow.setOrientation(LinearLayout.HORIZONTAL);
        presetRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        for (int i = 0; i < WifiPlan.CAMPUS_PRESETS.length; i++) {
            final String campus = WifiPlan.CAMPUS_PRESETS[i];
            Button pb = new Button(this);
            pb.setText(campus);
            pb.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
            pb.setAllCaps(false);
            pb.setBackground(round(FIELD, LINE, 6, 1));
            pb.setTextColor(INK);
            pb.setPadding(dp(10), dp(8), dp(10), dp(8));
            LinearLayout.LayoutParams plp =
                    new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            plp.setMargins(0, 0, dp(6), 0);
            pb.setLayoutParams(plp);
            pb.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) { onPickCampus(campus); }
            });
            presetRow.addView(pb);
        }
        cardAccount.addView(presetRow);

        // 输入框：不用点「选择」，点一下输入框就弹出可选列表（当前连着 / 校园网 / 扫到的 / 手打的）。
        // 想直接打字也行；别的 WiFi 扫不到时还能手输。
        etWifiSsid = field(InputType.TYPE_CLASS_TEXT);
        etWifiSsid.setHint("点这里选一个，或直接打字");
        etWifiSsid.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { showWifiPicker(); }
        });
        cardAccount.addView(etWifiSsid);

        cardAccount.addView(label("学号", 13, INK_DIM));
        etUser = field(InputType.TYPE_CLASS_TEXT);
        cardAccount.addView(etUser);

        cardAccount.addView(label("密码", 13, INK_DIM));
        etPass = field(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        cardAccount.addView(etPass);

        cbWifiAuto = check("打开界面 / 点认证时自动连到上面的 WiFi");
        cardAccount.addView(cbWifiAuto);
        cbWifiAuto.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { onWifiAutoToggled(); }
        });
        root.addView(cardAccount);

        // ---- 运行卡片 ----
        cardRun = card();
        cardRun.addView(label("检查周期（分钟，最小 15）", 13, INK_DIM));
        etInterval = field(InputType.TYPE_CLASS_NUMBER);
        cardRun.addView(etInterval);

        cbEnabled = check("开机自动认证 + 后台定时检查");
        cardRun.addView(cbEnabled);

        cbKeepAlive = check("常驻通知栏（后台更稳，国产手机建议开）");
        cardRun.addView(cbKeepAlive);

        // 免打扰: 连上校园网后系统会发「登录到网络」提醒/弹窗, 那是系统的强制门户检测,
        // 应用没权限关掉它 —— 这个开关只是记下你的意愿, 并给一句"去哪关"的提示。
        cbQuietPortal = check("减少「登录到网络」打扰（见下方提示）");
        cardRun.addView(cbQuietPortal);
        cbQuietPortal.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { onQuietToggled(); }
        });

        TextView quietHint = new TextView(this);
        quietHint.setText("那个登录提醒是安卓系统的强制门户检测发的，应用关不掉它。"
                + "点上面的勾会告诉你怎么关；它只是记个意愿，认证本身不受影响。");
        quietHint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        quietHint.setTextColor(INK_DIM);
        quietHint.setPadding(dp(2), 0, 0, dp(4));
        cardRun.addView(quietHint);
        root.addView(cardRun);

        // ---- 按钮（「检测状态」去掉了：认证/日志里本来就会说通没通）----
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, dp(2), 0, dp(6));
        btnSave = mkBtn("保存并应用", true);
        btnAuth = mkBtn("立即认证", false);
        row.addView(btnSave);
        row.addView(btnAuth);
        root.addView(row);

        // ---- 状态行 ----
        tvStatus = new TextView(this);
        tvStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        tvStatus.setTextColor(INK_DIM);
        tvStatus.setPadding(dp(2), dp(4), 0, dp(10));
        root.addView(tvStatus);

        // ---- 日志卡片 ----
        cardLog = card();
        TextView logTitle = new TextView(this);
        logTitle.setText("运行日志");
        logTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        logTitle.setTextColor(INK_DIM);
        logTitle.setPadding(0, 0, 0, dp(6));
        cardLog.addView(logTitle);

        // 日志区: 自己带滚动条, 新日志进来会自动滚到底(否则一直是盯着最上面几行, 看着像"没更新")
        logScroll = new ScrollView(this);
        tvLog = new TextView(this);
        tvLog.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        tvLog.setTextColor(INK_DIM);
        tvLog.setTypeface(android.graphics.Typeface.MONOSPACE);
        tvLog.setTextIsSelectable(true);
        logScroll.addView(tvLog);
        cardLog.addView(logScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(180)));
        root.addView(cardLog);

        btnSave.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { doSave(); }
        });
        btnAuth.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { doAuth(false); }
        });
        setContentView(scroll);
    }

    private Button mkBtn(String text, boolean primary) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        b.setAllCaps(false);
        if (primary) {
            b.setBackground(round(ACCENT, ACCENT, 6, 0));
            b.setTextColor(ACCENT_INK);
        } else {
            b.setBackground(round(CARD, LINE, 6, 1));
            b.setTextColor(INK);
        }
        b.setPadding(0, dp(8), 0, dp(8));
        LinearLayout.LayoutParams lp =
                new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        lp.setMargins(dp(3), 0, dp(3), 0);
        b.setLayoutParams(lp);
        return b;
    }

    // ================================================================
    //  数据读写
    // ================================================================
    private void loadToUi() {
        Prefs p = Prefs.load(this);
        if (etUser.getText().length() == 0) etUser.setText(p.userId);
        if (etPass.getText().length() == 0) etPass.setText(p.password);
        if (etInterval.getText().length() == 0) etInterval.setText(String.valueOf(p.intervalMinutes));
        if (etWifiSsid.getText().length() == 0) etWifiSsid.setText(p.wifiSsid);
        cbEnabled.setChecked(p.enabled);
        cbKeepAlive.setChecked(p.keepAlive);
        cbWifiAuto.setChecked(p.connectWifi);
        cbQuietPortal.setChecked(p.quietPortal);
    }

    private boolean collect(Prefs p) {
        p.userId = etUser.getText().toString().trim();
        p.password = etPass.getText().toString();
        try { p.intervalMinutes = Integer.parseInt(etInterval.getText().toString().trim()); }
        catch (Exception e) { p.intervalMinutes = 15; }
        if (p.intervalMinutes < 15) p.intervalMinutes = 15;
        p.enabled = cbEnabled.isChecked();
        p.keepAlive = cbKeepAlive.isChecked();
        p.wifiSsid = etWifiSsid.getText().toString().trim();
        p.connectWifi = cbWifiAuto.isChecked();
        p.quietPortal = cbQuietPortal.isChecked();
        if (p.userId.length() == 0 || p.password.length() == 0) {
            setStatus("请先填写学号和密码", ST_ERR);
            return false;
        }
        if (p.connectWifi && WifiPlan.parseCandidates(p.wifiSsid).isEmpty()) {
            setStatus("勾了自动连 WiFi，就要在上面填 WiFi 名称", ST_ERR);
            return false;
        }
        return true;
    }

    // ================================================================
    //  状态与日志
    // ================================================================
    private void setStatus(final String text, final int kind) {
        ui.post(new Runnable() {
            public void run() { showStatus(text, kind); }
        });
    }

    private void showStatus(String text, int kind) {
        int c = INK_DIM;
        if (kind == ST_OK) c = OKC;
        else if (kind == ST_ERR) c = ERRC;
        tvStatus.setText("●  " + text);
        tvStatus.setTextColor(c);
    }

    /** 供 onConfigurationChanged 用: 状态文字已经有了圆点, 直接原样贴回。 */
    private void setStatusRaw(String text) {
        if (text == null || text.length() == 0) return;
        tvStatus.setText(text);
        // 颜色按关键字重新判断一次, 保证换主题后仍然对
        int kind = ST_NEUTRAL;
        if (text.indexOf("成功") >= 0 || text.indexOf("已联网") >= 0 || text.indexOf("已启用") >= 0) kind = ST_OK;
        else if (text.indexOf("失败") >= 0 || text.indexOf("未联网") >= 0 || text.indexOf("请先") >= 0) kind = ST_ERR;
        int c = INK_DIM;
        if (kind == ST_OK) c = OKC;
        else if (kind == ST_ERR) c = ERRC;
        tvStatus.setTextColor(c);
    }

    private void appendLog(final String line) {
        ui.post(new Runnable() {
            public void run() {
                logBuf.append(line).append('\n');
                if (logBuf.length() > LOG_MAX) logBuf.delete(0, logBuf.length() - LOG_MAX);
                if (tvLog == null) return;
                tvLog.setText(logBuf.toString());
                scrollLogToBottom();
            }
        });
    }

    /**
     * 把日志区滚到最底下。
     *
     * 这是"日志看着不更新"的根因: 那 180dp 高的框一直停在最上面几行,
     * 新日志其实写进去了, 只是追加在可视区下面看不见。
     */
    private void scrollLogToBottom() {
        if (logScroll == null || tvLog == null) return;
        logScroll.post(new Runnable() {
            public void run() {
                try { logScroll.fullScroll(View.FOCUS_DOWN); } catch (Throwable t) { }
            }
        });
    }

    // ================================================================
    //  动作
    // ================================================================
    private void doSave() {
        Prefs p = new Prefs();
        if (!collect(p)) return;
        p.save(this);
        AppLog.i("配置已保存");
        if (p.enabled) {
            Scheduler.ensurePeriodic(this, p.intervalMinutes);
            Scheduler.runOnce(this, 2000L);
            if (p.keepAlive) { requestNotifyPermission(); KeepAliveService.start(this); }
            else { KeepAliveService.stop(this); }
            setStatus("已保存，开机自动认证已启用", ST_OK);
        } else {
            Scheduler.cancelAll(this);
            KeepAliveService.stop(this);
            setStatus("已保存，后台自动检查已关闭", ST_NEUTRAL);
        }
    }

    /** Android 13+ 需要运行时申请通知权限, 否则常驻通知不显示。 */
    private void requestNotifyPermission() {
        try {
            if (Build.VERSION.SDK_INT >= 33
                    && checkSelfPermission("android.permission.POST_NOTIFICATIONS")
                       != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[] { "android.permission.POST_NOTIFICATIONS" }, 100);
            }
        } catch (Throwable t) { }
    }

    /**
     * 只探测通不通, 不提交认证。
     *
     * 界面上已经没有「检测状态」按钮了（认证和日志本来就会说通没通），
     * 这个方法留给"勾了自动连 WiFi 时，连上之后先探一下"用。
     */
    private void doProbe() {
        setStatus("正在检测…", ST_NEUTRAL);
        new Thread(new Runnable() {
            public void run() {
                refreshWifiOnUi();
                SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
                    public void log(String line) { AppLog.i(line); }
                });
                a.bindIp = wifiIpv4();
                AppLog.i("WiFi 本地 IP: " + (a.bindIp.length() > 0 ? a.bindIp : "(没取到! 请求不会走 WiFi)"));
                final boolean on = a.testOnline(6);
                AppLog.i(on ? "检测结果: 已联网" : "检测结果: 未联网(需要认证)");
                setStatus(on ? "已联网" : "未联网，需要认证", on ? ST_OK : ST_ERR);
            }
        }).start();
    }

    /** 认证: 勾了"自动连 WiFi"就先确保连到目标 WiFi, 再提交(否则会走错网卡)。 */
    private void doAuth(final boolean force) {
        Prefs p = new Prefs();
        if (!collect(p)) return;
        if (p.connectWifi) {
            if (WifiSwitch.missingPermission(this) != null) {
                // 没有权限就既读不到名字也连不了, 先要权限, 拿到之后再继续
                pendingWifiAction = force ? PENDING_AUTH_FORCE : PENDING_AUTH;
                pendingForce = force;
                askWifiPermissions(true);
                return;
            }
            ensureWifiThenAuth(p, force, true);
        } else {
            runAuth(p, force, true);
        }
    }

    /** 真正跑认证的那段(后台线程)。 */
    private void runAuth(final Prefs p, final boolean force, final boolean loud) {
        setStatus("正在认证…", ST_NEUTRAL);
        new Thread(new Runnable() {
            public void run() {
                refreshWifiOnUi();
                SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
                    public void log(String line) { AppLog.i(line); }
                });
                a.portalHost = p.portalHost;
                a.wlanAcName = p.wlanAcName;
                a.nasIp = p.nasIp;
                a.userId = p.userId;
                a.password = p.password;
                a.bindIp = wifiIpv4();
                AppLog.i("WiFi 本地 IP: " + (a.bindIp.length() > 0 ? a.bindIp : "(没取到! 请求不会走 WiFi)"));
                SicauAuth.Result r = a.doAuth(force);
                if (r.ok && r.alreadyOnline) setStatus("已联网，无需认证", ST_OK);
                else if (r.ok) setStatus("认证成功", ST_OK);
                else setStatus(r.message, ST_ERR);
            }
        }).start();
    }

    // ================================================================
    //  WiFi 连接
    // ================================================================
    /**
     * 确保连到目标 WiFi, 连上之后再跑认证。
     * loud=true 表示是用户手动点的, 失败时要多说两句。
     */
    private void ensureWifiThenAuth(final Prefs p, final boolean authForce, final boolean loud) {
        final java.util.List<String> targets = WifiPlan.parseCandidates(p.wifiSsid);
        if (targets.isEmpty()) {
            AppLog.write("WARN", "没有填写要连接的 WiFi 名称, 直接认证");
            runAuth(p, authForce, loud);
            return;
        }

        setStatus("正在连接 WiFi " + WifiPlan.describe(targets, 20) + "…", ST_NEUTRAL);
        AppLog.i("准备连接 WiFi: " + WifiPlan.describe(targets, 60));
        ui.post(new Runnable() {
            public void run() {
                WifiSwitch.connect(MainActivity.this, targets, new WifiSwitch.Callback() {
                    public void onAlreadyConnected(String ssid, String ip, String who) {
                        AppLog.i("已经在 " + ssid + " 上" + (ip.length() > 0 ? "（" + ip + "）" : "") + ", 不用切");
                        refreshWifi();
                        runAuth(p, authForce, loud);
                    }

                    public void onConnected(String ssid, String ip) {
                        setStatus("已连接 " + ssid, ST_OK);
                        refreshWifi();
                        // 刚拿到 IP 那一瞬间还不一定能通, 给系统 1.5 秒再认证
                        ui.postDelayed(new Runnable() {
                            public void run() { runAuth(p, authForce, loud); }
                        }, 1500);
                    }

                    public void onDenied(String message) {
                        AppLog.write("WARN", "连接 WiFi 被取消或没成功: " + message);
                        setStatus(message, ST_ERR);
                        if (loud) runAuth(p, authForce, false);
                    }

                    public void onFailed(String message) {
                        AppLog.write("ERROR", "连接 WiFi 失败: " + message);
                        setStatus(message, ST_ERR);
                        if (loud) runAuth(p, authForce, false);
                    }
                });
            }
        });
    }

    /**
     * 「减少登录提醒打扰」被勾上/取消时。
     *
     * 说实话: 那条「登录到网络」提醒是**安卓系统的强制门户检测**发的, 应用没有权限关掉它
     * （要用户自己去系统设置里关通知, 或者用 adb 关掉强制门户检测）。
     * 所以这里不装样子, 直接把"去哪儿关"告诉用户, 并把意愿存下来。
     */
    private void onQuietToggled() {
        boolean on = cbQuietPortal.isChecked();
        Prefs p = Prefs.load(this);
        p.quietPortal = on;
        p.save(this);
        if (!on) {
            AppLog.i("已关闭「减少登录提醒」");
            setStatus("已关闭", ST_NEUTRAL);
            return;
        }
        AppLog.i("想减少「登录到网络」打扰 —— 提醒用户去系统设置里关通知");
        new AlertDialog.Builder(this)
                .setTitle("怎么关掉「登录到网络」提醒")
                .setMessage("先说清楚：那条提醒是**安卓系统**的强制门户检测发的，"
                        + "应用没有权限关掉它。要彻底不弹，有两个办法：\n\n"
                        + "【办法一 · 只关提醒，最简单】\n"
                        + "点下面的「去关通知」，进去把和「网络登录 / 登录到网络」有关的那一类通知关掉。\n\n"
                        + "【办法二 · 彻底关掉门户检测，要电脑】\n"
                        + "手机开 USB 调试，连电脑后执行：\n"
                        + "adb shell settings put global captive_portal_mode 0\n"
                        + "（想更彻底再加一条：\n"
                        + "adb shell pm disable-user --user 0 com.android.captiveportallogin）\n\n"
                        + "⚠️ 关掉之后系统不再提示你去登录，登录就完全靠本应用了 —— "
                        + "记得把「开机自动认证」勾上。")
                .setPositiveButton("去关通知", new android.content.DialogInterface.OnClickListener() {
                    public void onClick(android.content.DialogInterface d, int w) {
                        WifiSwitch.openNotificationSettings(MainActivity.this);
                    }
                })
                .setNegativeButton("知道了", null)
                .show();
    }

    /** 用户点「连接 WiFi」按钮: 只连 WiFi, 不认证。 */
    private void doConnectWifi(boolean requireTarget) {
        final java.util.List<String> targets = wifiTargets();
        if (targets.isEmpty()) {
            setStatus("先在上面填要连接的 WiFi 名称", ST_ERR);
            return;
        }
        if (requireTarget && WifiSwitch.missingPermission(this) != null) {
            pendingWifiAction = PENDING_CONNECT;
            askWifiPermissions(true);
            return;
        }
        setStatus("正在连接 " + WifiPlan.describe(targets, 20) + "…", ST_NEUTRAL);
        WifiSwitch.connect(this, targets, new WifiSwitch.Callback() {
            public void onAlreadyConnected(String ssid, String ip, String who) {
                AppLog.i("已经在 " + ssid + " 上, 不用切");
                setStatus("已经连着 " + ssid, ST_OK);
                refreshWifi();
            }

            public void onConnected(String ssid, String ip) {
                setStatus("已连接 " + ssid, ST_OK);
                refreshWifi();
            }

            public void onDenied(String message) { setStatus(message, ST_ERR); }
            public void onFailed(String message) { setStatus(message, ST_ERR); }
        });
    }

    // ================================================================
    //  WiFi 选择（扫描列表）
    // ================================================================
    /** 点「选择」：扫一下周围的 WiFi，弹个列表让你点。 */
    private void showWifiPicker() {
        String miss = WifiScan.missingPermission(this);
        if (miss != null) {
            pendingWifiAction = PENDING_PICK;      // 拿到权限后自动接着扫
            requestPermissions(WifiSwitch.allPermissions(), REQ_WIFI_PERM);
            setStatus("先给一下权限，才能看周围有哪些 WiFi", ST_NEUTRAL);
            return;
        }
        // 必需权限有了。顺手把"读 WiFi 名字"的位置权限也申请上（不给也能扫，只是看不到名字）
        if (!WifiSwitch.hasLocationPermission(this)) {
            requestPermissions(WifiSwitch.optionalPermissions(), REQ_WIFI_PERM);
        }
        startWifiScan();
    }

    /** 真正去扫，然后弹列表。 */
    private void startWifiScan() {
        setStatus("正在扫描周围的 WiFi…", ST_NEUTRAL);
        AppLog.i("开始扫描周围的 WiFi");
        WifiScan.scan(this, new WifiScan.Listener() {
            public void onResult(java.util.List<android.net.wifi.ScanResult> list, String message) {
                if (message != null) AppLog.write("WARN", message);
                else AppLog.i("扫描到 " + list.size() + " 个 WiFi");
                buildWifiPicker(list, message).show();
            }
        });
    }

    /**
     * 拼出那个选择列表。
     *
     * 说明两件事（都写在弹窗标题里，免得用户以为是程序坏了）：
     *   · 安卓 12 起，系统只让应用看到「连过的」WiFi，没连过的不给看；
     *   · 所以列表空了不代表周围没 WiFi，手动输入永远留着。
     */
    private AlertDialog buildWifiPicker(final List<ScanResult> scanList, final String scanMessage) {
        final String cur = WifiSwitch.readSsid(this);
        final String curIp = WifiSwitch.currentIp(this);

        // 扫描结果拆成"名字 + 说明"两个平行列表，交给 WifiPlan 排顺序
        List<String> scanNames = new ArrayList<String>();
        List<String> scanLabels = new ArrayList<String>();
        if (scanList != null) {
            for (int i = 0; i < scanList.size(); i++) {
                ScanResult r = scanList.get(i);
                scanNames.add(r.SSID);
                scanLabels.add(WifiScan.levelText(r.level) + (WifiScan.isSecured(r) ? "" : "・开放网络"));
            }
        }

        // 顺序（纯逻辑，有单元测试）：当前连着 → 校园网预设 → 扫描到的 → 自己填的
        final List<WifiPlan.Labeled> entries =
                WifiPlan.pickerEntries(cur, wifiTargets(), scanNames, scanLabels);

        final List<String> names = new ArrayList<String>();
        List<CharSequence> labels = new ArrayList<CharSequence>();
        for (int i = 0; i < entries.size(); i++) {
            names.add(entries.get(i).name);
            labels.add(entries.get(i).text());
        }

        AlertDialog.Builder b = new AlertDialog.Builder(this);
        b.setTitle("选择要连接的 WiFi");
        if (!names.isEmpty()) {
            b.setItems(labels.toArray(new CharSequence[0]),
                    new android.content.DialogInterface.OnClickListener() {
                        public void onClick(android.content.DialogInterface d, int which) {
                            if (which >= 0 && which < names.size()) onPickedSsid(names.get(which));
                        }
                    });
        } else {
            String why = scanMessage != null ? scanMessage
                    : "没扫到任何 WiFi（安卓 12 起，系统只让应用看到「连过的」网络，没连过的看不到）";
            if (cur.length() == 0 && curIp.length() > 0) {
                why = "系统没给 WiFi 名字，但确实连着 WiFi（" + curIp + "）。\n" + why;
            }
            b.setMessage(why + "\n\n可以点下面的「手动输入」自己填名字，或者用「系统设置」手动连一次。"
                    + "\n\n想查为什么读不到名字，点上面的「诊断」。");
        }
        b.setPositiveButton("重新扫描", new android.content.DialogInterface.OnClickListener() {
            public void onClick(android.content.DialogInterface d, int w) { startWifiScan(); }
        });
        b.setNeutralButton("手动输入", new android.content.DialogInterface.OnClickListener() {
            public void onClick(android.content.DialogInterface d, int w) {
                setStatus("在上面那个框里直接打字填 WiFi 名字就行", ST_NEUTRAL);
            }
        });
        b.setNegativeButton("系统设置", new android.content.DialogInterface.OnClickListener() {
            public void onClick(android.content.DialogInterface d, int w) { openWifiSettings(); }
        });
        return b.create();
    }

    /** 点校园网预设按钮（i_sicau_wifi6 / i_sicau）：填上、存下来、直接连。 */
    private void onPickCampus(final String campus) {
        AppLog.i("点了校园网预设: " + campus);
        onPickedSsid(campus);
    }

    /** 点列表里某一项（或校园网按钮）之后：填进输入框、存下来、然后连。 */
    private void onPickedSsid(final String ssid) {
        etWifiSsid.setText(ssid);
        Prefs p = Prefs.load(this);
        p.wifiSsid = ssid;
        // 选了具体名字就顺手把"自动连"打开 —— 用户既然专门挑了，八成就是要它
        p.connectWifi = true;
        p.save(this);
        cbWifiAuto.setChecked(true);
        setStatus("正在连接 " + ssid + "…", ST_NEUTRAL);

        List<String> one = new ArrayList<String>();
        one.add(ssid);
        WifiSwitch.connect(this, one, new WifiSwitch.Callback() {
            public void onAlreadyConnected(String s, String ip, String who) {
                setStatus("已经连着 " + s, ST_OK);
                refreshWifi();
            }

            public void onConnected(String s, String ip) {
                setStatus("已连接 " + s, ST_OK);
                refreshWifi();
            }

            public void onDenied(String message) { setStatus(message, ST_ERR); }
            public void onFailed(String message) { setStatus(message, ST_ERR); }
        });
    }

    /** 勾/取消「自动连 WiFi」时提示一下, 并顺手存下来。 */
    private void onWifiAutoToggled() {
        boolean on = cbWifiAuto.isChecked();
        if (on && wifiTargets().isEmpty()) {
            AppLog.write("WARN", "勾了自动连 WiFi, 但还没填 WiFi 名称");
            setStatus("还要在上面填一下要连接的 WiFi 名称", ST_ERR);
            return;
        }
        Prefs p = new Prefs();
        p.userId = etUser.getText().toString().trim();
        p.password = etPass.getText().toString();
        p.wifiSsid = etWifiSsid.getText().toString().trim();
        p.connectWifi = on;
        p.save(this);
        AppLog.i(on ? "已开启: 打开界面 / 认证前自动连 WiFi"
                    : "已关闭: 不再自动连 WiFi");
        setStatus(on ? "已开启自动连 WiFi" : "已关闭自动连 WiFi", ST_NEUTRAL);
    }

    /**
     * 申请 WiFi 相关的运行时权限。
     * 没有权限时 Android 会把 WiFi 名字藏起来(返回 <unknown ssid>), 界面上就看不到连的是哪个网。
     */
    private void askWifiPermissions(boolean force) {
        try {
            String miss = WifiSwitch.missingPermission(this);
            if (miss == null) return;
            if (!force) {
                Prefs p = Prefs.load(this);
                boolean needIt = p.connectWifi || !p.wifiSsid.isEmpty();
                if (!needIt) return;      // 没用到 WiFi 名字就不打扰用户
                if (p.wifiSsid.isEmpty()) return;
            }
            requestPermissions(WifiSwitch.allPermissions(), REQ_WIFI_PERM);
        } catch (Throwable t) { }
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        super.onRequestPermissionsResult(code, perms, results);
        if (code != REQ_WIFI_PERM) return;

        int act = pendingWifiAction;
        boolean force = pendingForce;
        pendingWifiAction = PENDING_NONE;

        String miss = WifiSwitch.missingPermission(this);
        if (miss != null) {
            AppLog.write("WARN", "没有拿到 WiFi 权限, 读不到 WiFi 名字, 也不能自动连");
            setStatus("没有授予权限，读不到 WiFi 名字；可以在系统设置里手动连", ST_ERR);
            return;
        }

        AppLog.i("WiFi 权限已获得");
        refreshWifi();
        if (act == PENDING_CONNECT) {
            doConnectWifi(false);
        } else if (act == PENDING_AUTH || act == PENDING_AUTH_FORCE) {
            Prefs p = new Prefs();
            if (collect(p)) ensureWifiThenAuth(p, force, true);
        } else if (act == PENDING_PICK) {
            startWifiScan();
        }
    }

    /** 后台线程里也能安全地刷一下 WiFi 状态行。 */
    private void refreshWifiOnUi() {
        ui.post(new Runnable() {
            public void run() { refreshWifi(); }
        });
    }
}
