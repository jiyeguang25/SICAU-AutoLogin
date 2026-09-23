package cn.edu.sicau.autologin;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.net.URL;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.SSLSocketFactory;

/**
 * 极简 HTTP 客户端。
 * 关键点: 强制走 IPv4 (校园网的 IPv6 路由不通, 系统默认会先试 IPv6 导致长时间卡住)。
 * 只依赖 java.* , 不依赖任何 Android API, 因此可以在电脑上直接跑测试。
 */
public class Http {

    public static class Resp {
        public int status = 0;
        public String location = "";
        public String body = "";
        public String finalUrl = "";
        public String error = "";
    }

    /** 绑定到指定本地 IPv4, 让请求走 WiFi 而不是移动流量。空串 = 不绑定。 */
    public String bindIp = null;
    /** 绑定失败的原因。以前这里被静默吞掉, 出了问题完全看不出线索。 */
    public String bindError = "";

    public static final String UA =
        "Mozilla/5.0 (Linux; Android 13; zh-CN) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    private final Map<String, String> cookies = new LinkedHashMap<String, String>();

    public Resp get(String url, int timeoutSec) {
        return request("GET", url, null, timeoutSec, true);
    }

    public Resp post(String url, String body, int timeoutSec) {
        return request("POST", url, body, timeoutSec, true);
    }

    public Resp request(String method, String url, String body, int timeoutSec, boolean follow) {
        String cur = url;
        String m = method;
        String b = body;
        int hops = 0;
        while (true) {
            Resp r = once(m, cur, b, timeoutSec);
            if (r.error.length() > 0) { r.finalUrl = cur; return r; }
            if (follow && r.status >= 300 && r.status < 400 && r.location.length() > 0 && hops < 5) {
                try {
                    cur = new URL(new URL(cur), r.location).toString();
                } catch (Exception e) {
                    r.finalUrl = cur;
                    return r;
                }
                hops++;
                m = "GET";
                b = null;
                continue;
            }
            r.finalUrl = cur;
            return r;
        }
    }

    /** 自己实现主机名校验: 检查 SAN 的 DNS 项(通配符只匹配一级), 没有 SAN 时退回 CN。 */
    public static boolean hostnameMatches(String host, java.security.cert.X509Certificate cert) {
        java.util.List<String> names = new ArrayList<String>();
        try {
            java.util.Collection<java.util.List<?>> san = cert.getSubjectAlternativeNames();
            if (san != null) {
                for (java.util.List<?> e : san) {
                    Object type = e.get(0);
                    Object val = e.get(1);
                    if (type instanceof Integer && ((Integer) type).intValue() == 2 && val instanceof String) {
                        names.add((String) val);
                    }
                }
            }
            if (names.isEmpty()) {
                java.util.regex.Matcher m = java.util.regex.Pattern
                        .compile("CN=([^,]*)").matcher(cert.getSubjectX500Principal().getName());
                if (m.find()) names.add(m.group(1));
            }
        } catch (Exception e) {
            return false;
        }
        String h = host.toLowerCase();
        for (String n : names) {
            String nn = n.toLowerCase().trim();
            if (nn.equals(h)) return true;
            if (nn.startsWith("*.")) {
                String suffix = nn.substring(1);
                if (h.endsWith(suffix)) {
                    String label = h.substring(0, h.length() - suffix.length());
                    if (label.length() > 0 && label.indexOf('.') < 0) return true;
                }
            }
        }
        return false;
    }

    private static InetAddress pickIPv4(String host) throws IOException {
        InetAddress[] all = InetAddress.getAllByName(host);
        for (int i = 0; i < all.length; i++) {
            if (all[i].getAddress().length == 4) return all[i];
        }
        if (all.length > 0) return all[0];
        throw new IOException("DNS resolve failed: " + host);
    }

    private Resp once(String method, String url, String body, int timeoutSec) {
        Resp r = new Resp();
        Socket sock = null;
        try {
            URL u = new URL(url);
            String scheme = u.getProtocol();
            String host = u.getHost();
            int port = u.getPort() > 0 ? u.getPort() : ("https".equals(scheme) ? 443 : 80);
            boolean ssl = "https".equals(scheme);

            InetAddress addr = pickIPv4(host);
            sock = new Socket();
            if (bindIp != null && bindIp.length() > 0) {
                try { sock.bind(new InetSocketAddress(java.net.InetAddress.getByName(bindIp), 0)); }
                catch (Exception e) { bindError = String.valueOf(e); }
            }
            sock.connect(new InetSocketAddress(addr, port), timeoutSec * 1000);
            sock.setSoTimeout(timeoutSec * 1000);

            if (ssl) {
                SSLSocketFactory f = (SSLSocketFactory) SSLSocketFactory.getDefault();
                SSLSocket s = (SSLSocket) f.createSocket(sock, host, port, true);
                s.startHandshake();
                java.security.cert.Certificate[] chain = s.getSession().getPeerCertificates();
                if (chain.length > 0 && chain[0] instanceof java.security.cert.X509Certificate) {
                    if (!hostnameMatches(host, (java.security.cert.X509Certificate) chain[0])) {
                        throw new IOException("certificate hostname mismatch: " + host);
                    }
                }
                sock = s;
            }

            String path = u.getFile();
            if (path == null || path.length() == 0) path = "/";

            StringBuilder sb = new StringBuilder();
            sb.append(method).append(' ').append(path).append(" HTTP/1.1\r\n");
            sb.append("Host: ").append(host).append("\r\n");
            sb.append("User-Agent: ").append(UA).append("\r\n");
            sb.append("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n");
            sb.append("Accept-Encoding: identity\r\n");
            sb.append("Connection: close\r\n");
            if (cookies.size() > 0) {
                sb.append("Cookie: ");
                boolean first = true;
                for (Map.Entry<String, String> e : cookies.entrySet()) {
                    if (!first) sb.append("; ");
                    sb.append(e.getKey()).append('=').append(e.getValue());
                    first = false;
                }
                sb.append("\r\n");
            }
            byte[] bodyBytes = null;
            if ("POST".equals(method)) {
                bodyBytes = (body == null ? "" : body).getBytes("UTF-8");
                sb.append("Content-Type: application/x-www-form-urlencoded\r\n");
                sb.append("Content-Length: ").append(bodyBytes.length).append("\r\n");
            }
            sb.append("\r\n");

            OutputStream os = sock.getOutputStream();
            os.write(sb.toString().getBytes("UTF-8"));
            if (bodyBytes != null) os.write(bodyBytes);
            os.flush();

            InputStream is = sock.getInputStream();
            String statusLine = readLine(is);
            if (statusLine == null) throw new IOException("empty response");
            String[] parts = statusLine.split(" ");
            if (parts.length >= 2) {
                try { r.status = Integer.parseInt(parts[1].trim()); } catch (Exception e) { }
            }

            int contentLength = -1;
            boolean chunked = false;
            List<String> setCookies = new ArrayList<String>();
            while (true) {
                String line = readLine(is);
                if (line == null) break;
                if (line.length() == 0) break;
                int c = line.indexOf(':');
                if (c < 0) continue;
                String k = line.substring(0, c).trim().toLowerCase();
                String v = line.substring(c + 1).trim();
                if (k.equals("content-length")) {
                    try { contentLength = Integer.parseInt(v); } catch (Exception e) { }
                } else if (k.equals("transfer-encoding") && v.toLowerCase().contains("chunked")) {
                    chunked = true;
                } else if (k.equals("location")) {
                    r.location = v;
                } else if (k.equals("set-cookie")) {
                    setCookies.add(v);
                }
            }
            for (String sc : setCookies) {
                int semi = sc.indexOf(';');
                String nv = semi >= 0 ? sc.substring(0, semi) : sc;
                int eq = nv.indexOf('=');
                if (eq > 0) {
                    String ck = nv.substring(0, eq).trim();
                    String cv = nv.substring(eq + 1).trim();
                    if (cv.length() > 0) cookies.put(ck, cv);
                    else cookies.remove(ck);
                }
            }

            byte[] raw;
            if (chunked) raw = readChunked(is);
            else if (contentLength >= 0) raw = readN(is, contentLength);
            else raw = readAll(is);
            r.body = new String(raw, "UTF-8");
        } catch (Exception ex) {
            r.error = ex.getClass().getSimpleName() + ": " + ex.getMessage();
        } finally {
            try { if (sock != null) sock.close(); } catch (Exception e) { }
        }
        return r;
    }

    private static String readLine(InputStream is) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        boolean any = false;
        int c;
        while ((c = is.read()) != -1) {
            any = true;
            if (c == '\n') return new String(bos.toByteArray(), "ISO-8859-1");
            if (c != '\r') bos.write(c);
        }
        if (!any && bos.size() == 0) return null;
        return new String(bos.toByteArray(), "ISO-8859-1");
    }

    private static byte[] readN(InputStream is, int n) throws IOException {
        byte[] buf = new byte[n];
        int off = 0;
        while (off < n) {
            int k = is.read(buf, off, n - off);
            if (k < 0) break;
            off += k;
        }
        if (off == n) return buf;
        byte[] out = new byte[off];
        System.arraycopy(buf, 0, out, 0, off);
        return out;
    }

    private static byte[] readAll(InputStream is) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        byte[] buf = new byte[4096];
        int k;
        while ((k = is.read(buf)) > 0) bos.write(buf, 0, k);
        return bos.toByteArray();
    }

    private static byte[] readChunked(InputStream is) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        while (true) {
            String sz = readLine(is);
            if (sz == null) break;
            int semi = sz.indexOf(';');
            if (semi >= 0) sz = sz.substring(0, semi);
            sz = sz.trim();
            if (sz.length() == 0) continue;
            int n;
            try { n = Integer.parseInt(sz, 16); } catch (Exception e) { break; }
            if (n == 0) { readLine(is); break; }
            bos.write(readN(is, n));
            readLine(is);
        }
        return bos.toByteArray();
    }
}
