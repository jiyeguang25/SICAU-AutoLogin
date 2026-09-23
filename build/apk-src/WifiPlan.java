package cn.edu.sicau.autologin;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * WiFi 目标名字的解析与判断。
 *
 * 特意做成「不依赖任何 android.* 类」的纯 Java, 这样能直接在电脑上的 JVM 里跑单元测试
 * (桌面测试台见 build\jvmtest\WifiPlanTest.java), 不用真机。
 *
 * 为什么支持多个名字: 校园网可能同时存在 i_sicau_wifi6 和 i_sicau_wifi 之类的名字,
 * 用户不该为了换一个名字再改一次设置。
 */
public final class WifiPlan {

    private WifiPlan() { }

    /**
     * 把用户输入/扫描结果拆成候选 WiFi 名字列表。
     *
     * 支持的分隔符: 换行、英文逗号、中文逗号、分号、竖线、斜杠、制表符。
     * 会自动去掉首尾空格、去掉成对的英文双引号(系统有时候给的名字带引号)、
     * 去掉空项, 并按出现顺序去重(保留第一次出现的顺序, 便于"哪个优先试哪个")。
     */
    public static List<String> parseCandidates(String raw) {
        List<String> out = new ArrayList<String>();
        if (raw == null) return out;

        String s = raw.trim();
        if (s.length() == 0) return out;

        String[] parts = s.split("[\n\r,，;；|/\\t]+");
        for (int i = 0; i < parts.length; i++) {
            String p = clean(parts[i]);
            if (p.length() == 0) continue;
            if (!out.contains(p)) out.add(p);
        }
        return out;
    }

    /**
     * 目标名字里是否含有通配符。用 * 或 ? 表示:
     *   i_sicau_*   匹配 i_sicau_wifi6 / i_sicau_wifi / i_sicau_test
     *   i_sicau_?   只匹配 i_sicau_ 后面正好一个字符
     * 这个很有用: 校园网的 SSID 会随楼栋变(i_sicau_wifi6 / i_sicau_wifi5 ...),
     * 写通配符就不用每次改配置。
     */
    public static boolean isPattern(String name) {
        return name != null && (name.indexOf('*') >= 0 || name.indexOf('?') >= 0);
    }

    /**
     * 通配符转**正则**。* -> .*   ? -> .   其余字符按字面量处理(会转义)。
     *
     * 注意: 这个给程序自己比对用(matches)。要交给安卓系统的 setSsidPattern 时**不能**用它,
     * 系统那边吃的是 glob 不是正则 —— 那个用 toGlob()。
     * (public 是因为单元测试要直接验它。)
     */
    public static String toRegex(String pattern) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < pattern.length(); i++) {
            char c = pattern.charAt(i);
            if (c == '*') sb.append(".*");
            else if (c == '?') sb.append('.');
            else if ("\\.[]{}()+-^$|".indexOf(c) >= 0) sb.append('\\').append(c);
            else sb.append(c);
        }
        return sb.toString();
    }

    /**
     * 通配符转安卓系统的 glob 模式, 给 WifiNetworkSpecifier.Builder.setSsidPattern 用。
     *
     * 系统那边的语法(PatternMatcher.PATTERN_ADVANCED_GLOB)是:
     *   *  匹配任意多个字符
     *   .  匹配任意一个字符(注意: 系统这边"一个字符"是点号, 不是问号)
     *   \  转义
     * 所以这里把用户的 ? 换成 . , 再把系统认作特殊字符的东西转义掉, 其余原样。
     */
    public static String toGlob(String pattern) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < pattern.length(); i++) {
            char c = pattern.charAt(i);
            if (c == '*') sb.append('*');
            else if (c == '?') sb.append('.');
            else if (c == '\\' || c == '.') sb.append('\\').append(c);
            else sb.append(c);
        }
        return sb.toString();
    }

    /** 候选(可能是通配符)匹配到实际 SSID 时返回真。大小写不敏感。 */
    public static boolean matches(String candidate, String ssid) {
        if (candidate == null || ssid == null) return false;
        String c = candidate.trim();
        String t = clean(ssid);
        if (c.length() == 0 || t.length() == 0) return false;
        try {
            if (isPattern(c)) {
                return t.toLowerCase().matches("(?i)" + toRegex(c));
            }
            return c.equalsIgnoreCase(t);
        } catch (Throwable e) {
            return false;
        }
    }

    /** 目标列表里任意一个匹配到 ssid 就算命中。 */
    public static boolean matchesAny(List<String> candidates, String ssid) {
        if (candidates == null) return false;
        for (int i = 0; i < candidates.size(); i++) {
            if (matches(candidates.get(i), ssid)) return true;
        }
        return false;
    }

    /** 去掉首尾空格和成对的双引号。 */
    public static String clean(String s) {
        if (s == null) return "";
        String t = s.trim();
        if (t.length() >= 2 && t.startsWith("\"") && t.endsWith("\"")) {
            t = t.substring(1, t.length() - 1).trim();
        }
        return t;
    }

    /** 给界面显示用: 列表拼成一行, 最长 maxLen 个字符, 超出用 … 收尾。 */
    public static String describe(List<String> candidates, int maxLen) {
        if (candidates == null || candidates.isEmpty()) return "（未设置）";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < candidates.size(); i++) {
            if (i > 0) sb.append(", ");
            sb.append(candidates.get(i));
        }
        String s = sb.toString();
        if (maxLen > 0 && s.length() > maxLen) s = s.substring(0, maxLen - 1) + "…";
        return s;
    }

    /** 只读视图, 方便外部遍历。 */
    public static List<String> unmodifiable(List<String> l) {
        return Collections.unmodifiableList(l);
    }

    // ================================================================
    //  校园 WiFi 预设
    // ================================================================

    /**
     * 学校就这两个 WiFi 名（在用户电脑上 `netsh wlan show profiles` 查到的，写死在这里当预设）。
     * 界面上会直接把这两个做成按钮，点一下就填上并连 —— 不用打字，也不用等扫描。
     * 扫描列表里如果扫到别的, 照样能选; 手动输入也一直留着。
     */
    public static final String[] CAMPUS_PRESETS = { "i_sicau_wifi6", "i_sicau" };

    public static List<String> campusPresets() {
        List<String> l = new ArrayList<String>();
        for (int i = 0; i < CAMPUS_PRESETS.length; i++) l.add(CAMPUS_PRESETS[i]);
        return l;
    }

    /**
     * 拼出「选择 WiFi」列表的显示顺序（纯逻辑, 有单元测试）：
     *   1. 当前连着的那个（放最前面一眼能看到）
     *   2. 校园网预设那两个（固定名字, 不用等扫描）
     *   3. 扫描到的周围 WiFi
     *   4. 用户已经填在输入框里的（可能是手打的、扫描看不见的）
     *
     * 每项返回一行文字；同名只出现一次（按优先级取第一个）。
     * scanLabel 与 scanNames 一一对应（每个扫描项的说明文字, 比如信号强弱）。
     */
    public static List<Labeled> pickerEntries(String currentSsid, List<String> typed,
                                              List<String> scanNames, List<String> scanLabels) {
        List<Labeled> out = new ArrayList<Labeled>();
        List<String> seen = new ArrayList<String>();

        // 1) 当前连着的
        String cur = clean(currentSsid);
        if (!isEmptyName(cur) && !contains(seen, cur)) {
            out.add(new Labeled(cur, "当前已连接"));
            seen.add(cur);
        }
        // 2) 校园网预设
        for (int i = 0; i < CAMPUS_PRESETS.length; i++) {
            String p = CAMPUS_PRESETS[i];
            if (contains(seen, p)) continue;
            out.add(new Labeled(p, "校园网"));
            seen.add(p);
        }
        // 3) 扫描到的
        if (scanNames != null) {
            for (int i = 0; i < scanNames.size(); i++) {
                String n = clean(scanNames.get(i));
                if (n.length() == 0 || contains(seen, n)) continue;
                String lab = (scanLabels != null && i < scanLabels.size()) ? scanLabels.get(i) : "";
                out.add(new Labeled(n, lab));
                seen.add(n);
            }
        }
        // 4) 用户自己填的
        if (typed != null) {
            for (int i = 0; i < typed.size(); i++) {
                String n = clean(typed.get(i));
                if (n.length() == 0 || contains(seen, n)) continue;
                out.add(new Labeled(n, "当前填写"));
                seen.add(n);
            }
        }
        return out;
    }

    /** 列表里的一行：名字 + 后面括号里的说明。 */
    public static final class Labeled {
        public final String name;
        public final String note;
        public Labeled(String name, String note) { this.name = name; this.note = note; }
        /** 界面上显示的文字。 */
        public String text() {
            if (note == null || note.length() == 0) return name;
            return name + "　（" + note + "）";
        }
        public String toString() { return text(); }
    }

    static boolean contains(List<String> l, String s) {
        for (int i = 0; i < l.size(); i++) if (l.get(i).equalsIgnoreCase(s)) return true;
        return false;
    }

    static boolean isEmptyName(String s) {
        String t = clean(s);
        if (t.length() == 0) return true;
        String low = t.toLowerCase();
        return low.equals("unknown") || low.equals("0x") || low.equals("<unknown ssid>");
    }

    // ================================================================
    //  扫描结果整理（纯逻辑, 不碰 android.*, 所以能在这台电脑上测）
    // ================================================================

    /** 从元素里取一个字符串(用来当去重的键)。 */
    public interface Key<T> { String of(T item); }

    /** 从元素里取一个整数(用来比大小, 比如信号强度); 越大越优先。 */
    public interface Score<T> { int of(T item); }

    /**
     * 按 key 去重, 同一个 key 只留 score 最大的那个; 然后按 score 从大到小排序。
     *
     * 用在扫描结果上: 同一个 WiFi 名可能有多个接入点(2.4G/5G、不同楼层),
     * 界面上一行就够了 —— 留信号最强的那条。
     *
     * ⚠️ 依赖 java.util 的**稳定排序**: 分数相同的保持原顺序, 所以"当前连着的"那条
     * 在前面的位置不会被后来的同分项顶掉。测试里专门验了这一点。
     */
    public static <T> List<T> mergeKeepStrongest(List<T> items, Key<T> key, Score<T> score) {
        List<T> out = new ArrayList<T>();
        if (items == null) return out;

        List<Integer> outScore = new ArrayList<Integer>();
        List<String> outKey = new ArrayList<String>();

        for (int i = 0; i < items.size(); i++) {
            T it = items.get(i);
            if (it == null) continue;

            String k = clean(key.of(it));
            if (k.length() == 0) continue;                  // 隐藏网络的 SSID 是空的, 用不上
            int sc = score.of(it);

            int at = -1;
            for (int j = 0; j < outKey.size(); j++) {
                if (outKey.get(j).equalsIgnoreCase(k)) { at = j; break; }
            }
            if (at < 0) {
                out.add(it); outKey.add(k); outScore.add(sc);
            } else if (sc > outScore.get(at)) {
                out.set(at, it); outScore.set(at, sc);
            }
        }

        // 插入排序: 按分数从大到小, 同分保持原顺序(稳定)
        for (int i = 1; i < out.size(); i++) {
            T it = out.get(i);
            int sc = outScore.get(i);
            String k = outKey.get(i);
            int j = i - 1;
            while (j >= 0 && outScore.get(j) < sc) {
                out.set(j + 1, out.get(j));
                outScore.set(j + 1, outScore.get(j));
                outKey.set(j + 1, outKey.get(j));
                j--;
            }
            out.set(j + 1, it);
            outScore.set(j + 1, sc);
            outKey.set(j + 1, k);
        }
        return out;
    }
}
