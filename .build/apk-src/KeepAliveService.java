package cn.edu.sicau.autologin;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;

/**
 * 常驻前台服务: 挂一个低优先级通知, 让系统别把进程杀掉。
 * 顺便由它自己按周期跑认证, 比单纯靠 JobScheduler 在国产 ROM 上更稳。
 */
public class KeepAliveService extends Service {

    public static final String CHANNEL_ID = "sicau_keepalive";
    public static final int NOTIFY_ID = 1001;

    private volatile boolean running = false;
    private Thread worker;

    public static void start(Context ctx) {
        try {
            Intent i = new Intent(ctx, KeepAliveService.class);
            if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(i);
            else ctx.startService(i);
        } catch (Throwable t) {
            AppLog.write("ERROR", "启动常驻服务失败: " + t);
        }
    }

    public static void stop(Context ctx) {
        try { ctx.stopService(new Intent(ctx, KeepAliveService.class)); } catch (Throwable t) { }
    }

    @Override
    public void onCreate() {
        super.onCreate();
        AppLog.init(getFilesDir());
        createChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        try {
            startForeground(NOTIFY_ID, buildNotification("正在运行，断网会自动认证"));
        } catch (Throwable t) {
            AppLog.write("ERROR", "前台通知失败: " + t);
        }
        if (!running) {
            running = true;
            worker = new Thread(new Runnable() {
                public void run() { loop(); }
            });
            worker.setDaemon(true);
            worker.start();
            AppLog.i("常驻服务已启动");
        }
        return START_STICKY;
    }

    private void loop() {
        while (running) {
            int mins = 15;
            try {
                Prefs p = Prefs.load(this);
                if (!p.enabled) {
                    sleepMinutes(5);
                    continue;
                }
                SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
                    public void log(String line) { AppLog.i(line); }
                });
                a.portalHost = p.portalHost;
                a.wlanAcName = p.wlanAcName;
                a.nasIp = p.nasIp;
                a.userId = p.userId;
                a.password = p.password;

                SicauAuth.Result r = a.doAuth(false);
                updateNotification(r.ok ? (r.alreadyOnline ? "已联网" : "认证成功") : ("未通过: " + r.message));

                mins = p.intervalMinutes > 0 ? p.intervalMinutes : 15;
                if (mins < 15) mins = 15;
            } catch (Throwable t) {
                AppLog.write("ERROR", "常驻服务异常: " + t);
                mins = 5;
            }
            sleepMinutes(mins);
        }
    }

    /** 分段睡眠, 方便及时响应停止。 */
    private void sleepMinutes(int m) {
        long end = System.currentTimeMillis() + m * 60L * 1000L;
        while (running && System.currentTimeMillis() < end) {
            try { Thread.sleep(5000); } catch (InterruptedException e) { return; }
        }
    }

    @Override
    public void onDestroy() {
        running = false;
        try { stopForeground(true); } catch (Throwable t) { }
        AppLog.i("常驻服务已停止");
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    private void createChannel() {
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
                if (nm != null && nm.getNotificationChannel(CHANNEL_ID) == null) {
                    NotificationChannel ch = new NotificationChannel(CHANNEL_ID, "校园网自动认证",
                            NotificationManager.IMPORTANCE_LOW);
                    ch.setShowBadge(false);
                    ch.setSound(null, null);
                    nm.createNotificationChannel(ch);
                }
            }
        } catch (Throwable t) { }
    }

    private Notification buildNotification(String text) {
        Intent i = new Intent(this, MainActivity.class);
        i.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        int flag = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= 23) flag |= PendingIntent.FLAG_IMMUTABLE;
        PendingIntent pi = PendingIntent.getActivity(this, 0, i, flag);

        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) b = new Notification.Builder(this, CHANNEL_ID);
        else b = new Notification.Builder(this);

        b.setContentTitle("校园网自动认证")
         .setContentText(text)
         .setSmallIcon(R.mipmap.ic_launcher)
         .setOngoing(true)
         .setContentIntent(pi);
        return b.build();
    }

    private void updateNotification(String text) {
        try {
            NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
            if (nm != null) nm.notify(NOTIFY_ID, buildNotification(text));
        } catch (Throwable t) { }
    }
}
