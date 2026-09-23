package cn.edu.sicau.autologin;

import android.app.job.JobParameters;
import android.app.job.JobService;

public class AuthJobService extends JobService {

    @Override
    public boolean onStartJob(final JobParameters params) {
        AppLog.init(getFilesDir());
        new Thread(new Runnable() {
            public void run() {
                try {
                    Prefs p = Prefs.load(AuthJobService.this);
                    AppLog.i("---- 后台自动认证开始 ----");
                    SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
                        public void log(String line) { AppLog.i(line); }
                    });
                    a.portalHost = p.portalHost;
                    a.wlanAcName = p.wlanAcName;
                    a.nasIp = p.nasIp;
                    a.userId = p.userId;
                    a.password = p.password;
                    a.doAuth(false);
                } catch (Throwable t) {
                    AppLog.write("ERROR", "认证异常: " + t);
                }
                jobFinished(params, false);
            }
        }).start();
        return true;
    }

    @Override
    public boolean onStopJob(JobParameters params) {
        return true;
    }
}
