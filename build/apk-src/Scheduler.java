package cn.edu.sicau.autologin;

import android.app.job.JobInfo;
import android.app.job.JobScheduler;
import android.content.ComponentName;
import android.content.Context;

public class Scheduler {
    public static final int JOB_PERIODIC = 1001;
    public static final int JOB_ONCE = 1002;

    public static void ensurePeriodic(Context ctx, int minutes) {
        JobScheduler js = (JobScheduler) ctx.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (js == null) return;
        js.cancel(JOB_PERIODIC);
        if (minutes <= 0) return;
        if (minutes < 15) minutes = 15;
        try {
            JobInfo job = new JobInfo.Builder(JOB_PERIODIC, new ComponentName(ctx, AuthJobService.class))
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .setPersisted(true)
                    .setPeriodic(minutes * 60L * 1000L)
                    .build();
            js.schedule(job);
            AppLog.i("已启用后台自动检查, 周期 " + minutes + " 分钟");
        } catch (Exception e) {
            AppLog.write("ERROR", "安排周期任务失败: " + e);
        }
    }

    public static void runOnce(Context ctx, long delayMs) {
        JobScheduler js = (JobScheduler) ctx.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (js == null) return;
        try {
            JobInfo job = new JobInfo.Builder(JOB_ONCE, new ComponentName(ctx, AuthJobService.class))
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .setMinimumLatency(delayMs)
                    .setOverrideDeadline(delayMs + 60000L)
                    .build();
            js.schedule(job);
        } catch (Exception e) { }
    }

    public static void cancelAll(Context ctx) {
        JobScheduler js = (JobScheduler) ctx.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (js != null) js.cancelAll();
        AppLog.i("已关闭后台自动检查");
    }
}
