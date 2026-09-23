package cn.edu.sicau.autologin;

import android.content.Context;

/**
 * 存一份 ApplicationContext, 给那些拿不到 Context 的地方用
 * (比如 WifiSwitch.releaseActive() 要注销回调, 但那一刻手上没有 Context)。
 * 在 MainActivity.onCreate 里塞进去即可; 进程活着它就活着。
 */
public final class AppCtx {
    private static Context app;

    private AppCtx() { }

    public static void set(Context c) {
        if (c != null) app = c.getApplicationContext();
    }

    public static Context get() {
        return app;
    }
}
