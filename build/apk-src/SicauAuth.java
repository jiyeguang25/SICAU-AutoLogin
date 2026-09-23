package cn.edu.sicau.autologin;

import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 四川农业大学校园网认证核心逻辑。
 * 不依赖任何 Android API, 可在电脑上直接测试。
 *
 * 原理: 未认证时网关会把 HTTP 请求 302 到
 *   https://portal.sicau.edu.cn/portal.do?wlanuserip=..&wlanacname=..&nasip=..
 * 该页面里的隐藏表单明文 POST 到 /webauth.do?<同一串参数> 即可完成认证。
 */
public class SicauAuth {

    public static class Result {
        public boolean ok;
        public boolean alreadyOnline;
        public String message = "";
    }

    public interface Logger {
        void log(String line);
    }

    public String portalHost = "portal.sicau.edu.cn";
    public String wlanAcName = "";
    public String nasIp = "";
    public String userId = "";
    public String password = "";
    /** 绑定到 WiFi 本地 IP, 让认证/探测走 WiFi 而不是移动流量。 */
    public String bindIp = "";

    private final Logger log;

    public SicauAuth(Logger l) {
        this.log = l;
    }

    private void L(String s) {
        if (log != null) log.log(s);
    }

    private boolean warnedNoBind = false;

    private Http newHttp() {
        Http h = new Http();
        if (bindIp != null && bindIp.length() > 0) {
            h.bindIp = bindIp;
        } else if (!warnedNoBind) {
            warnedNoBind = true;
            L("警告: 没取到 WiFi 的本地 IP, 请求未绑定到 WiFi; 开着移动数据时可能被误判为已联网");
        }
        return h;
    }

    // ---------------------------------------------------------- 工具
    public static String localIpv4(String target, int port) {
        try {
            DatagramSocket s = new DatagramSocket();
            s.connect(new InetSocketAddress(target, port));
            String ip = s.getLocalAddress().getHostAddress();
            s.close();
            if (ip != null && ip.indexOf('.') > 0) return ip;
        } catch (Exception e) { }
        return null;
    }

    private static final Pattern P_INPUT = Pattern.compile("<input\\b[^>]*>", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_NAME_D = Pattern.compile("name\\s*=\\s*\"([^\"]*)\"", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_NAME_S = Pattern.compile("name\\s*=\\s*'([^']*)'", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_TYPE_D = Pattern.compile("type\\s*=\\s*\"([^\"]*)\"", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_VAL_D = Pattern.compile("value\\s*=\\s*\"([^\"]*)\"", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_VAL_S = Pattern.compile("value\\s*=\\s*'([^']*)'", Pattern.CASE_INSENSITIVE);
    private static final Pattern P_PORTAL_ABS =
        Pattern.compile("https?://[A-Za-z0-9\\.\\-]+(:[0-9]+)?/portal\\.do\\?[^\\s\"<>\\\\]*", Pattern.CASE_INSENSITIVE);

    public static Map<String, String> parseFormFields(String html) {
        Map<String, String> map = new LinkedHashMap<String, String>();
        if (html == null) return map;
        Matcher mi = P_INPUT.matcher(html);
        while (mi.find()) {
            String tag = mi.group();
            Matcher mn = P_NAME_D.matcher(tag);
            if (!mn.find()) {
                mn = P_NAME_S.matcher(tag);
                if (!mn.find()) continue;
            }
            String name = mn.group(1);
            if (name == null || name.trim().length() == 0) continue;

            String type = "text";
            Matcher mt = P_TYPE_D.matcher(tag);
            if (mt.find()) type = mt.group(1).toLowerCase();
            if (type.equals("button") || type.equals("submit") || type.equals("reset")
                    || type.equals("file") || type.equals("image")) continue;

            String val = "";
            Matcher mv = P_VAL_D.matcher(tag);
            if (!mv.find()) {
                mv = P_VAL_S.matcher(tag);
                if (mv.find()) val = mv.group(1);
            } else {
                val = mv.group(1);
            }
            val = val.replace("&amp;", "&").replace("&quot;", "\"").replace("&lt;", "<").replace("&gt;", ">");
            map.put(name, val);
        }
        return map;
    }

    private static String grab(String qs, String key) {
        if (qs == null) return "";
        Matcher m = Pattern.compile("(?:^|&)" + Pattern.quote(key) + "=([^&]*)").matcher(qs);
        return m.find() ? m.group(1) : "";
    }

    // ---------------------------------------------------------- 联网判定
    public boolean testOnline(int timeoutSec) {
        Http h = newHttp();
        Http.Resp r = h.request("GET", "http://connect.rom.miui.com/generate_204", null, timeoutSec, false);
        if (r.status == 204) return true;
        r = h.request("GET", "http://www.msftconnecttest.com/connecttest.txt", null, timeoutSec, false);
        if (r.status == 200 && r.body.contains("Microsoft Connect Test")) return true;
        r = h.request("GET", "http://detectportal.firefox.com/success.txt", null, timeoutSec, false);
        if (r.status == 200 && r.body.contains("success")) return true;
        return false;
    }

    // ---------------------------------------------------------- 认证页定位
    public String findPortalUrl(int timeoutSec) {
        String[] cands = {
            "http://connect.rom.miui.com/generate_204",
            "http://www.msftconnecttest.com/connecttest.txt",
            "http://www.baidu.com/"
        };
        Http h = newHttp();
        for (String u : cands) {
            Http.Resp r = h.request("GET", u, null, timeoutSec, false);
            if (r.status >= 300 && r.status < 400 && r.location.length() > 0) {
                String loc = r.location.replace("&amp;", "&");
                if (loc.indexOf("portal.do?") >= 0) return loc;
                if (loc.startsWith("/")) return "http://" + portalHost + loc;
            }
            String body = r.body;
            if (body != null && body.length() > 0) {
                String t = body.replace("&amp;", "&").replace("\\/", "/");
                Matcher m = P_PORTAL_ABS.matcher(t);
                if (m.find()) return m.group();
                Matcher m2 = Pattern.compile("/portal\\.do\\?[^\\s\"<>\\\\]*").matcher(t);
                if (m2.find()) return "https://" + portalHost + m2.group();
            }
        }
        return null;
    }

    public String buildFallbackUrl() {
        String ip = localIpv4(portalHost, 443);
        if (ip == null) return null;
        StringBuilder sb = new StringBuilder("https://" + portalHost + "/portal.do?wlanuserip=" + ip);
        if (wlanAcName != null && wlanAcName.length() > 0) sb.append("&wlanacname=").append(wlanAcName);
        if (nasIp != null && nasIp.length() > 0) sb.append("&nasip=").append(nasIp);
        return sb.toString();
    }

    // ---------------------------------------------------------- 主流程
    public Result doAuth(boolean force) {
        Result res = new Result();
        if (userId == null || userId.trim().length() == 0 || password == null || password.length() == 0) {
            res.message = "配置里没有学号或密码";
            L(res.message);
            return res;
        }

        boolean online = testOnline(6);
        if (!force && online) {
            res.ok = true;
            res.alreadyOnline = true;
            res.message = "当前已联网, 无需认证";
            L(res.message);
            return res;
        }
        if (force) L("(强制) 跳过联网判断, online=" + online);

        String portalUrl = findPortalUrl(6);
        if (portalUrl != null) {
            L("认证网关跳转: " + portalUrl);
        } else {
            portalUrl = buildFallbackUrl();
            if (portalUrl != null) L("未捕获到网关跳转, 用兜底地址: " + portalUrl);
        }
        if (portalUrl == null) {
            res.message = "无法确定认证页地址(可能不在校园网内)";
            L(res.message);
            return res;
        }

        Http http = newHttp();
        Http.Resp page = http.get(portalUrl, 12);
        if (page.error.length() > 0) {
            res.message = "打开认证页失败: " + page.error;
            L(res.message);
            return res;
        }
        L("认证页 HTTP " + page.status + ", 长度 " + page.body.length());
        if (page.status != 200) {
            res.message = "认证页返回 " + page.status;
            L(res.message);
            return res;
        }

        Map<String, String> fields = parseFormFields(page.body);
        if (fields.size() == 0 || !fields.containsKey("urlParameter")) {
            res.message = "认证页里没有解析到登录表单";
            L(res.message);
            return res;
        }
        L("解析到表单字段 " + fields.size() + " 个");

        String urlParam = fields.get("urlParameter");
        if (urlParam == null) urlParam = "";

        // 表单里 userId/passwd 是空值, 若在后面再追加一次就成了两个同名参数;
        // 门户只取第一个 -> 拿到空值 -> 报"账号不存在"。所以覆盖, 不追加。
        fields.put("userId", userId);
        fields.put("passwd", password);
        fields.put("isRemind", "1");
        fields.put("remInfo", "on");

        StringBuilder sb = new StringBuilder();
        try {
            for (Map.Entry<String, String> e : fields.entrySet()) {
                if (e.getKey() == null || e.getKey().trim().length() == 0) continue;
                if (sb.length() > 0) sb.append('&');
                sb.append(java.net.URLEncoder.encode(e.getKey(), "UTF-8"))
                  .append('=')
                  .append(java.net.URLEncoder.encode(e.getValue() == null ? "" : e.getValue(), "UTF-8"));
            }
        } catch (Exception e) {
            res.message = "组装表单失败: " + e.getMessage();
            L(res.message);
            return res;
        }

        // 门户不设 cookie, 会话号通过 URL 重写传: /xxx;JSESSIONID-BOSS-0=A1B2...
        // 不带上的话服务端会当成新会话, 直接把登录页原样返回
        String sid = "";
        java.util.regex.Matcher msid = Pattern
                .compile(";JSESSIONID[^=;?&\"'<>]*=[^;?&\"'<>]+").matcher(page.body);
        if (msid.find()) { sid = msid.group(); L("会话号: " + sid); }
        else L("页面里没找到会话号, 按无会话提交");

        String target = "https://" + portalHost + "/webauth.do" + sid;
        if (urlParam.length() > 0) target = target + "?" + urlParam;
        final String postBody = sb.toString();

        // 同账号只能一台设备在线: 第一次提交把别的设备踢下线, 本机还要再提交一次
        for (int attempt = 1; ; attempt++) {
            L(attempt == 1 ? ("提交认证: " + target) : ("第 " + attempt + " 次提交(切换其他设备): " + target));
            Http.Resp r = http.post(target, postBody, 15);
            if (r.error.length() > 0) {
                res.message = "提交失败: " + r.error;
                L(res.message);
                return res;
            }
            L("认证响应 HTTP " + r.status);

            String plain = r.body.replaceAll("(?s)<script.*?</script>", " ")
                                .replaceAll("(?s)<style.*?</style>", " ")
                                .replaceAll("<[^>]+>", " ")
                                .replaceAll("\\s+", " ").trim();
            if (plain.length() > 0) L("响应摘要: " + plain.substring(0, Math.min(240, plain.length())));

            try { Thread.sleep(2000); } catch (Exception e) { }

            if (testOnline(6)) {
                res.ok = true;
                res.message = attempt == 1 ? "认证成功, 网络已连通" : "认证成功(已将其他设备切换下线)";
                L(res.message);
                return res;
            }

            String emsg = parseErrMessage(r.body);
            String reason = emsg.length() > 0 ? emsg : parseFailReason(r.body);
            if (hardFailure(reason)) {
                res.message = "认证失败: " + reason;
                L(res.message);
                return res;
            }
            if (attempt >= 2) {
                res.message = reason.length() > 0 ? ("认证失败: " + reason) : "认证后仍未联网, 请检查账号密码";
                L(res.message);
                return res;
            }
            L("首次提交后仍未联网, 可能是有其他设备在线, 重试一次");
            try { Thread.sleep(2000); } catch (Exception e) { }
        }
    }

    /** 门户把失败原因写在 errMessage 隐藏字段里, 比在正文里瞎猜准。 */
    private static String parseErrMessage(String html) {
        if (html == null) return "";
        java.util.regex.Matcher m = Pattern
                .compile("id=\"errMessage\"[^>]*value=\"([^\"]*)\"").matcher(html);
        if (!m.find()) return "";
        String v = m.group(1);
        return v.replace("&amp;", "&").replace("&lt;", "<")
                .replace("&gt;", ">").replace("&quot;", "\"").replace("&#39;", "'").trim();
    }

    private static String parseFailReason(String html) {
        if (html == null) return "";
        String t = html.replaceAll("(?s)<script.*?</script>", " ")
                       .replaceAll("(?s)<style.*?</style>", " ")
                       .replaceAll("<[^>]+>", " ")
                       .replaceAll("\\s+", " ");
        String[][] rules = {
            { "用户名或密码", "账号或密码错误" },
            { "密码错误", "密码错误" },
            { "密码不正确", "密码错误" },
            { "用户不存在", "账号不存在" },
            { "帐号不存在", "账号不存在" },
            { "账号不存在", "账号不存在" },
            { "已停机", "账号已停机" },
            { "账号停机", "账号已停机" },
            { "欠费", "账号欠费" },
            { "余额不足", "账号余额不足" },
            { "被锁定", "账号被锁定" },
            { "已锁定", "账号被锁定" },
            { "在线数", "在线设备数已达上限" },
            { "已达上限", "已达上限" },
            { "超过最大", "在线设备数超限" },
            { "重复认证", "该 IP 已在线, 无需重复认证" }
        };
        for (String[] r : rules) {
            if (t.indexOf(r[0]) >= 0) return r[1];
        }
        return "";
    }

    private static boolean hardFailure(String reason) {
        if (reason == null || reason.length() == 0) return false;
        String[] hard = { "密码错误", "密码不正确", "用户名或密码", "账号不存在", "账号已停机",
                          "账号欠费", "余额不足", "账号被锁定", "已锁定", "不允许", "禁用" };
        for (String h : hard) {
            if (reason.indexOf(h) >= 0) return true;
        }
        return false;
    }
}
