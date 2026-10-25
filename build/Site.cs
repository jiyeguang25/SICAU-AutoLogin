using System;

namespace Sicau
{
    /// <summary>
    /// 校园网相关的「站点常量」全放这一个文件: 域名、WiFi 名、端口、路径前缀。
    ///
    /// 为什么要单拎出来: 这些字面量原来散在 Core.cs / App.cs / 安卓的好几个文件里,
    /// 换一个学校(或者学校换门户地址、换 WiFi 名)就得满项目找, 漏一处就悄悄认证失败。
    /// 现在只改这里, 其它文件一律引用 —— **不要再在别处写死**。
    /// 取值和收敛之前完全一致, 是纯搬家, 不改任何行为。
    /// </summary>
    public static class Site
    {
        // ---- 认证门户 ----
        /// <summary>认证门户域名。证书是它的上级通配符(见 <see cref="PortalDomain"/>)。</summary>
        public const string PortalHost = "portal.sicau.edu.cn";
        /// <summary>证书通配的上级域名(<c>*.sicau.edu.cn</c>), 自解析 SAN 校验时用。</summary>
        public const string PortalDomain = "sicau.edu.cn";
        /// <summary>HTTPS 端口。</summary>
        public const int HttpsPort = 443;
        /// <summary>HTTP 端口(网关把未认证请求跳到 http 的门户页时用)。</summary>
        public const int HttpPort = 80;
        /// <summary>门户登录页路径(未认证时网关 302 跳到这里, 带一串 wlan* 参数)。</summary>
        public const string PortalPath = "/portal.do";
        /// <summary>提交认证的路径(登录页里的隐藏表单 POST 到这里)。</summary>
        public const string WebAuthPath = "/webauth.do";
        /// <summary>门户根地址(带 https)。</summary>
        public const string PortalBaseUrl = "https://" + PortalHost;

        // ---- 校园 WiFi ----
        /// <summary>学校在用的 WiFi 名(按优先级: 6 代在那栋楼就先连它)。</summary>
        public static readonly string[] CampusSsids = { "i_sicau_wifi6", "i_sicau" };
    }
}
