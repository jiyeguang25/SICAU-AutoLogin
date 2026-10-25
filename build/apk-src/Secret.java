package cn.edu.sicau.autologin;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.security.KeyPairGenerator;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * 密码加密：用 Android Keystore 里的密钥，**不用硬编码密钥**。
 *
 * <ul>
 *   <li>正常路径（API 23+）：Keystore 里的 AES/GCM 256 位密钥，密钥本身不可导出、不进备份</li>
 *   <li>老机器兜底（API 21/22，Keystore 还不支持 AES）：用同一个 Keystore 里的 RSA 密钥加密</li>
 *   <li>两条路都失败时返回空串 —— 宁可不存密码，也**绝不退回明文**</li>
 * </ul>
 *
 * <p>密文格式见 {@link PwdBox}；整体 Base64 后带 <code>keystore:</code> 前缀存进 SharedPreferences。
 * 读到没有前缀的值就是旧版本存的明文，{@link Prefs} 会立刻加密回写（平滑迁移）。</p>
 */
final class Secret {

    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String AES_ALIAS = "sicau_pwd_aes_v1";
    private static final String RSA_ALIAS = "sicau_pwd_rsa_v1";
    private static final String PREFIX = "keystore:";
    private static final int GCM_TAG_BITS = 128;

    private Secret() { }

    /** 加密；空密码返回空串。失败返回空串（不落明文）。 */
    static String encrypt(Context ctx, String plain) {
        if (plain == null || plain.length() == 0) return "";
        byte[] blob = null;
        try { blob = aesEncrypt(plain); }
        catch (Throwable ignored) { }
        if (blob == null) {
            try { blob = rsaEncrypt(ctx, plain); } catch (Throwable ignored) { }
        }
        if (blob == null || blob.length == 0) return "";
        return PREFIX + Base64.encodeToString(blob, Base64.NO_WRAP);
    }

    /** 解密；没有前缀的值按旧明文原样返回（迁移用）。解不开返回空串，不抛异常。 */
    static String decrypt(String stored) {
        if (stored == null || stored.length() == 0) return "";
        if (!stored.startsWith(PREFIX)) return stored;
        try {
            byte[] blob = Base64.decode(stored.substring(PREFIX.length()), Base64.NO_WRAP);
            int ver = PwdBox.verify(blob);
            if (ver == PwdBox.VER_AES_GCM) return aesDecrypt(blob);
            if (ver == PwdBox.VER_RSA) return rsaDecrypt(blob);
        } catch (Throwable ignored) { }
        return "";   // 换了手机 / 清了 Keystore：当没存, 让用户重输一次
    }

    /** 这个值是不是"还没加密"的旧明文（空串不算）。 */
    static boolean isLegacyPlain(String stored) {
        return stored != null && stored.length() > 0 && !stored.startsWith(PREFIX);
    }

    // ------------------------------------------------------------ AES/GCM (API 23+)
    private static byte[] aesEncrypt(String plain) throws Exception {
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
        c.init(Cipher.ENCRYPT_MODE, aesKey());
        byte[] iv = c.getIV();
        byte[] ct = c.doFinal(plain.getBytes("UTF-8"));
        return PwdBox.pack(PwdBox.VER_AES_GCM, iv, ct);
    }

    private static String aesDecrypt(byte[] blob) throws Exception {
        Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
        c.init(Cipher.DECRYPT_MODE, aesKey(), new GCMParameterSpec(GCM_TAG_BITS, PwdBox.ivOf(blob)));
        return new String(c.doFinal(PwdBox.bodyOf(blob)), "UTF-8");
    }

    private static SecretKey aesKey() throws Exception {
        KeyStore ks = KeyStore.getInstance(KEYSTORE);
        ks.load(null);
        if (!ks.containsAlias(AES_ALIAS)) {
            KeyGenerator kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE);
            kg.init(new KeyGenParameterSpec.Builder(AES_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build());
            kg.generateKey();
        }
        return (SecretKey) ks.getKey(AES_ALIAS, null);
    }

    // ------------------------------------------------------------ RSA 兜底 (API 21/22)
    private static byte[] rsaEncrypt(Context ctx, String plain) throws Exception {
        Cipher c = Cipher.getInstance("RSA/ECB/PKCS1Padding");
        c.init(Cipher.ENCRYPT_MODE, rsaKey(ctx));
        return PwdBox.pack(PwdBox.VER_RSA, null, c.doFinal(plain.getBytes("UTF-8")));
    }

    private static String rsaDecrypt(byte[] blob) throws Exception {
        Cipher c = Cipher.getInstance("RSA/ECB/PKCS1Padding");
        c.init(Cipher.DECRYPT_MODE, rsaKey(null));
        return new String(c.doFinal(PwdBox.bodyOf(blob)), "UTF-8");
    }

    /** ctx 只在需要新建密钥时用得到；已有密钥时传 null 也能取。 */
    private static java.security.Key rsaKey(Context ctx) throws Exception {
        KeyStore ks = KeyStore.getInstance(KEYSTORE);
        ks.load(null);
        if (!ks.containsAlias(RSA_ALIAS)) {
            if (ctx == null) throw new IllegalStateException("no rsa key");
            KeyPairGenerator kpg = KeyPairGenerator.getInstance("RSA", KEYSTORE);
            // 用 API 18 就有的 KeyPairGeneratorSpec：比 KeyGenParameterSpec 老, 但 21/22 上就它能用
            android.security.KeyPairGeneratorSpec.Builder b =
                    new android.security.KeyPairGeneratorSpec.Builder(ctx)
                            .setAlias(RSA_ALIAS)
                            .setSubject(new javax.security.auth.x500.X500Principal("CN=SICAU-AutoLogin"))
                            .setSerialNumber(java.math.BigInteger.ONE)
                            .setStartDate(new java.util.Date(0L))
                            .setEndDate(new java.util.Date(4102444800000L));   // 2100-01-01
            kpg.initialize(b.build());
            kpg.generateKeyPair();
        }
        return ks.getKey(RSA_ALIAS, null);
    }
}
