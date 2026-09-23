package cn.edu.sicau.autologin;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

public class BootReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context ctx, Intent intent) {
        try {
            AppLog.init(ctx.getFilesDir());
            String action = intent.getAction();
            AppLog.i("收到广播: " + action);
            Prefs p = Prefs.load(ctx);
            if (!p.enabled) return;
            // 开机/升级后先安排一次(等网络就绪后立刻认证), 再安排周期任务
            Scheduler.runOnce(ctx, 10000L);
            Scheduler.ensurePeriodic(ctx, p.intervalMinutes);
            // 开了常驻通知栏就顺便把前台服务拉起来
            if (p.keepAlive) KeepAliveService.start(ctx);
        } catch (Throwable t) { }
    }
}
