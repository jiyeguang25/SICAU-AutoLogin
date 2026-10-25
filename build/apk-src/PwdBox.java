package cn.edu.sicau.autologin;

/**
 * 密码密文的「信封」格式：<code>[版本 1 字节][IV 长度 1 字节][IV][密文]</code>。
 *
 * <p>为什么单独拆出来：这部分是纯字节拼装，不碰任何 {@code android.*} 类，
 * 所以桌面测试台（jvmtest）能直接对往返、截断、版本不认这几种情况做单元测试。
 * 真正的加解密（Android Keystore）在 {@link Secret} 里。</p>
 */
public final class PwdBox {

    /** 版本 1：AES/GCM/NoPadding，IV 由 Cipher 生成（12 字节），带 128 位认证标签。 */
    public static final int VER_AES_GCM = 1;
    /** 版本 2：RSA/ECB/PKCS1Padding，老机器（API 21/22，没有 Keystore AES）用，没有 IV。 */
    public static final int VER_RSA = 2;

    private static final int MAX_IV = 64;

    private PwdBox() { }

    /** 拼信封；iv 传 null 或空数组表示该算法不用 IV（RSA）。 */
    public static byte[] pack(int ver, byte[] iv, byte[] body) {
        if (body == null) body = new byte[0];
        if (iv == null) iv = new byte[0];
        byte[] out = new byte[2 + iv.length + body.length];
        out[0] = (byte) ver;
        out[1] = (byte) iv.length;
        System.arraycopy(iv, 0, out, 2, iv.length);
        System.arraycopy(body, 0, out, 2 + iv.length, body.length);
        return out;
    }

    /** 校验信封结构，返回版本号；结构不对返回 -1（调用方当成"解不开"处理，不要抛异常）。 */
    public static int verify(byte[] blob) {
        if (blob == null || blob.length < 2) return -1;
        int ver = blob[0] & 0xFF;
        int ivLen = blob[1] & 0xFF;
        if (ivLen > MAX_IV || blob.length < 2 + ivLen) return -1;
        if (ver == VER_AES_GCM && ivLen == 0) return -1;
        if (ver == VER_RSA && ivLen != 0) return -1;
        if (ver != VER_AES_GCM && ver != VER_RSA) return -1;
        return ver;
    }

    /** 取 IV（RSA 信封返回空数组）。调用前先 {@link #verify}。 */
    public static byte[] ivOf(byte[] blob) {
        int n = blob[1] & 0xFF;
        byte[] iv = new byte[n];
        System.arraycopy(blob, 2, iv, 0, n);
        return iv;
    }

    /** 取密文本体。调用前先 {@link #verify}。 */
    public static byte[] bodyOf(byte[] blob) {
        int off = 2 + (blob[1] & 0xFF);
        byte[] body = new byte[blob.length - off];
        System.arraycopy(blob, off, body, 0, body.length);
        return body;
    }
}
