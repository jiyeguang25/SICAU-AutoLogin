package cn.edu.sicau.autologin;

/**
 * 校园网相关的「站点常量」全放这一个文件: 域名、WiFi 名、端口、路径前缀。
 *
 * <p>为什么要单拎出来: 这些字面量原来散在 {@link Prefs}、{@link SicauAuth}、{@link WifiPlan}、
 * {@link MainActivity} 里, 换一个学校(或者学校换门户地址、换 WiFi 名)就得满项目找, 漏一处就悄悄认证失败。
 * 现在只改这里, 其它文件一律引用 —— <b>不要再在别处写死</b>。
 * 取值和收敛之前完全一致, 是纯搬家, 不改任何行为。</p>
 *
 * <p>本类是纯常量, 不依赖 {@code android.*}, 所以桌面测试台(jvmtest)也能直接编它。</p>
 */
public final class Site {

    // ---- 认证门户 ----
    /** 认证门户域名。证书是它的上级通配符(见 {@link #PORTAL_DOMAIN})。 */
    public static final String PORTAL_HOST = "portal.sicau.edu.cn";
    /** 证书通配的上级域名({@code *.sicau.edu.cn}), 自解析 SAN 校验时用。 */
    public static final String PORTAL_DOMAIN = "sicau.edu.cn";
    /** HTTPS 端口。 */
    public static final int HTTPS_PORT = 443;
    /** HTTP 端口(网关把未认证请求跳到 http 的门户页时用)。 */
    public static final int HTTP_PORT = 80;
    /** 门户登录页路径(未认证时网关 302 跳到这里, 带一串 wlan* 参数)。 */
    public static final String PORTAL_PATH = "/portal.do";
    /** 提交认证的路径(登录页里的隐藏表单 POST 到这里)。 */
    public static final String WEBAUTH_PATH = "/webauth.do";
    /** 门户根地址(带 https)。 */
    public static final String PORTAL_BASE_URL = "https://" + PORTAL_HOST;

    // ---- 校园 WiFi ----
    /** 学校在用的 WiFi 名(按优先级: 6 代在那栋楼就先连它)。 */
    public static final String[] CAMPUS_SSIDS = { "i_sicau_wifi6", "i_sicau" };

    private Site() { }
}
