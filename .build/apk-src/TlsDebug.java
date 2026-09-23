package cn.edu.sicau.autologin;

import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.security.cert.X509Certificate;
import java.util.Collection;
import java.util.List;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.SSLSocketFactory;
import javax.net.ssl.SSLParameters;
import javax.net.ssl.SNIHostName;
import javax.net.ssl.SNIServerName;

public class TlsDebug {
    public static void main(String[] args) throws Exception {
        java.io.PrintStream out = new java.io.PrintStream(System.out, true, "UTF-8");
        String host = "portal.sicau.edu.cn";

        out.println("=== A: 默认 createSocket(sock, host, port, true) ===");
        show(out, host, false);

        out.println("=== B: 显式设置 SNI ===");
        show(out, host, true);
    }

    static void show(java.io.PrintStream out, String host, boolean explicitSni) throws Exception {
        Socket sock = new Socket();
        sock.connect(new InetSocketAddress(InetAddress.getByName("10.255.248.9"), 443), 8000);
        sock.setSoTimeout(8000);
        SSLSocketFactory f = (SSLSocketFactory) SSLSocketFactory.getDefault();
        SSLSocket s = (SSLSocket) f.createSocket(sock, host, 443, true);
        if (explicitSni) {
            SSLParameters p = s.getSSLParameters();
            List<SNIServerName> names = new java.util.ArrayList<SNIServerName>();
            names.add(new SNIHostName(host));
            p.setServerNames(names);
            s.setSSLParameters(p);
        }
        s.startHandshake();
        X509Certificate c = (X509Certificate) s.getSession().getPeerCertificates()[0];
        out.println("  subject  = " + c.getSubjectX500Principal().getName());
        out.println("  issuer   = " + c.getIssuerX500Principal().getName());
        out.println("  peerHost = " + s.getSession().getPeerHost());
        Collection<List<?>> san = c.getSubjectAlternativeNames();
        if (san == null) {
            out.println("  SAN      = (none)");
        } else {
            StringBuilder sb = new StringBuilder();
            for (List<?> e : san) sb.append(e.get(0)).append("=").append(e.get(1)).append(" ");
            out.println("  SAN      = " + sb);
        }
        boolean ok = javax.net.ssl.HttpsURLConnection.getDefaultHostnameVerifier().verify(host, s.getSession());
        out.println("  verify   = " + ok);
        s.close();
    }
}
