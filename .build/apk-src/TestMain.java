package cn.edu.sicau.autologin;

public class TestMain {
    public static void main(String[] args) throws Exception {
        java.io.PrintStream out = new java.io.PrintStream(System.out, true, "UTF-8");
        out.println("localIpv4 = " + SicauAuth.localIpv4("portal.sicau.edu.cn", 443));

        SicauAuth a = new SicauAuth(new SicauAuth.Logger() {
            public void log(String line) { System.out.println("   " + line); }
        });
        a.portalHost = "portal.sicau.edu.cn";
        a.wlanAcName = "WJ-C10-Bras01-ME60-X8A";
        a.nasIp = "5.5.5.18";
        a.userId = "DUMMY_USER";
        a.password = "DUMMY_PASS";

        out.println("testOnline = " + a.testOnline(6));
        out.println("findPortalUrl = " + a.findPortalUrl(6));
        out.println("fallbackUrl = " + a.buildFallbackUrl());

        out.println("--- HTTP GET 认证页 ---");
        Http http = new Http();
        Http.Resp r = http.get(a.buildFallbackUrl(), 12);
        out.println("status = " + r.status + " err = " + r.error + " len = " + r.body.length());
        java.util.Map<String, String> f = SicauAuth.parseFormFields(r.body);
        out.println("fields = " + f.size());
        out.println("urlParameter = " + f.get("urlParameter"));
        out.println("wlanacname = " + f.get("wlanacname") + " | userIp = " + f.get("userIp"));

        out.println("--- 完整认证流程(假账号) ---");
        SicauAuth.Result res = a.doAuth(true);
        out.println("ok=" + res.ok + " alreadyOnline=" + res.alreadyOnline + " msg=" + res.message);
    }
}
