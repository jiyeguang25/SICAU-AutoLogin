package cn.edu.sicau.autologin;

public class TestReal {
    public static void main(String[] args) throws Exception {
        java.io.PrintStream out = new java.io.PrintStream(System.out, true, "UTF-8");

        java.util.Properties p = new java.util.Properties();
        java.io.FileInputStream fis = new java.io.FileInputStream(args[0]);
        p.load(new java.io.InputStreamReader(fis, "UTF-8"));
        fis.close();
        String user = p.getProperty("userid", "").trim();
        String pass = p.getProperty("password", "");
        out.println("账号 = " + user + "   密码长度 = " + pass.length());

        out.println("=== 安卓认证代码, 在桌面 JVM 上真实跑一遍 ===");
        out.println("localIpv4 = " + SicauAuth.localIpv4("portal.sicau.edu.cn", 443));

        SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
            public void log(String line) { System.out.println("   " + line); }
        });
        a.portalHost = "portal.sicau.edu.cn";
        a.wlanAcName = "WJ-C10-Bras01-ME60-X8A";
        a.nasIp = "5.5.5.18";
        a.userId = user;
        a.password = pass;

        out.println("testOnline = " + a.testOnline(6));
        out.println("findPortalUrl = " + a.findPortalUrl(6));

        out.println("--- 完整认证流程 (force=true, 真实账号) ---");
        SicauAuth.Result res = a.doAuth(true);
        out.println("返回: ok=" + res.ok + "  alreadyOnline=" + res.alreadyOnline + "  msg=" + res.message);
    }
}