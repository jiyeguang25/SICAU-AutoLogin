import cn.edu.sicau.autologin.WifiPlan;

import java.util.Arrays;
import java.util.List;

/**
 * WifiPlan 的桌面单元测试 —— 不需要安卓设备。
 *
 * 编译运行:
 *   javac -encoding UTF-8 -d out apk-src\WifiPlan.java jvmtest\WifiPlanTest.java
 *   java -Dfile.encoding=UTF-8 -cp out WifiPlanTest
 */
public class WifiPlanTest {

    static int pass = 0, fail = 0;

    static void eq(String what, Object got, Object want) {
        boolean ok = (got == null && want == null) || (got != null && got.equals(want));
        if (ok) { pass++; System.out.println("  [OK]   " + what + " = " + got); }
        else { fail++; System.out.println("  [FAIL] " + what + " = " + got + "   期望 " + want); }
    }

    static void eqList(String what, List<String> got, String... want) {
        eq(what, got.toString(), Arrays.asList(want).toString());
    }

    /** 把 pickerEntries 的结果转成字符串列表再比对。 */
    static List<String> texts(java.util.List<WifiPlan.Labeled> l) {
        List<String> out = new java.util.ArrayList<String>();
        for (int i = 0; i < l.size(); i++) out.add(l.get(i).text());
        return out;
    }

    static void eqLabeled(String what, java.util.List<WifiPlan.Labeled> got, String... want) {
        eq(what, texts(got).toString(), Arrays.asList(want).toString());
    }

    public static void main(String[] args) {
        System.out.println("=== 1) 单个名字 ===");
        eqList("parseCandidates(\"i_sicau_wifi6\")",
                WifiPlan.parseCandidates("i_sicau_wifi6"), "i_sicau_wifi6");

        System.out.println("=== 2) 多个名字: 英文逗号 / 中文逗号 / 换行 / 分号 / 竖线 / 斜杠 ===");
        eqList("英文逗号", WifiPlan.parseCandidates("A,B"), "A", "B");
        eqList("中文逗号", WifiPlan.parseCandidates("A，B"), "A", "B");
        eqList("换行", WifiPlan.parseCandidates("A\nB\r\nC"), "A", "B", "C");
        eqList("分号+竖线+斜杠", WifiPlan.parseCandidates("A;B|C/D"), "A", "B", "C", "D");

        System.out.println("=== 3) 空格 / 空项 / 去重(保序) ===");
        eqList("前后空格", WifiPlan.parseCandidates("  A  ,  B  "), "A", "B");
        eqList("空项丢掉", WifiPlan.parseCandidates("A,,  ,B,"), "A", "B");
        eqList("重复保序去重", WifiPlan.parseCandidates("B,A,B"), "B", "A");
        eqList("空串", WifiPlan.parseCandidates("   "));
        eqList("null", WifiPlan.parseCandidates(null));

        System.out.println("=== 4) 系统给的名字带引号 ===");
        eqList("带引号", WifiPlan.parseCandidates("\"i_sicau_wifi6\""), "i_sicau_wifi6");
        eq("clean 引号", WifiPlan.clean("\"abc\""), "abc");
        eq("clean 只去成对的", WifiPlan.clean("\"abc"), "\"abc");

        System.out.println("=== 5) 精确匹配(大小写不敏感) ===");
        eq("完全相同", WifiPlan.matches("i_sicau_wifi6", "i_sicau_wifi6"), true);
        eq("大小写不同", WifiPlan.matches("I_SICAU_WiFi6", "i_sicau_wifi6"), true);
        eq("不一样", WifiPlan.matches("i_sicau_wifi6", "i_sicau_wifi5"), false);
        eq("拿 ssid 带引号也能比", WifiPlan.matches("i_sicau_wifi6", "\"i_sicau_wifi6\""), true);

        System.out.println("=== 6) 通配符 ===");
        eq("星号匹配 wifi6", WifiPlan.matches("i_sicau_*", "i_sicau_wifi6"), true);
        eq("星号匹配 wifi", WifiPlan.matches("i_sicau_*", "i_sicau_wifi"), true);
        eq("星号不匹配别的", WifiPlan.matches("i_sicau_*", "campus-wifi"), false);
        eq("问号只吃一个字符", WifiPlan.matches("i_sicau_wifi?", "i_sicau_wifi6"), true);
        eq("问号不吃两个", WifiPlan.matches("i_sicau_wifi?", "i_sicau_wifi66"), false);
        eq("中间星号", WifiPlan.matches("i_*_wifi6", "i_sicau_wifi6"), true);

        System.out.println("=== 7) 正则元字符要当普通字符 ===");
        eq("加号是字面量", WifiPlan.matches("A+B", "A+B"), true);
        eq("加号不是元字符", WifiPlan.matches("A+B", "AAB"), false);
        eq("圆括号是字面量", WifiPlan.matches("WiFi(5G)", "WiFi(5G)"), true);
        eq("点号是字面量", WifiPlan.matches("a.b", "a.b"), true);
        eq("点号不匹配任意字符", WifiPlan.matches("a.b", "axb"), false);

        System.out.println("=== 8) matchesAny ===");
        List<String> targets = WifiPlan.parseCandidates("i_sicau_wifi6, i_sicau_wifi");
        eq("命中第一个", WifiPlan.matchesAny(targets, "i_sicau_wifi6"), true);
        eq("命中第二个", WifiPlan.matchesAny(targets, "i_sicau_wifi"), true);
        eq("都没命中", WifiPlan.matchesAny(targets, "CMCC-5G"), false);
        eq("空列表", WifiPlan.matchesAny(WifiPlan.parseCandidates(""), "x"), false);
        eq("空 ssid", WifiPlan.matchesAny(targets, ""), false);
        eq("null ssid", WifiPlan.matchesAny(targets, null), false);

        System.out.println("=== 9) isPattern / describe ===");
        eq("有星号", WifiPlan.isPattern("a*b"), true);
        eq("有问号", WifiPlan.isPattern("a?b"), true);
        eq("没有", WifiPlan.isPattern("ab"), false);
        eq("describe 空", WifiPlan.describe(WifiPlan.parseCandidates(""), 40), "（未设置）");
        eq("describe 拼接", WifiPlan.describe(targets, 40), "i_sicau_wifi6, i_sicau_wifi");
        eq("describe 截断", WifiPlan.describe(targets, 10), "i_sicau_w…");

        System.out.println("=== 10) toRegex: 交给系统 setSsidPattern 的正则 ===");
        eq("星号", WifiPlan.toRegex("i_sicau_*"), "i_sicau_.*");
        eq("问号", WifiPlan.toRegex("i_sicau_wifi?"), "i_sicau_wifi.");
        eq("元字符转义", WifiPlan.toRegex("WiFi(5G)+"), "WiFi\\(5G\\)\\+");
        eq("转义后仍能整串匹配", "WiFi(5G)+".matches(WifiPlan.toRegex("WiFi(5G)+")), true);
        eq("点号被转义", "a.b".matches(WifiPlan.toRegex("a.b")), true);
        eq("点号不再通配", "axb".matches(WifiPlan.toRegex("a.b")), false);

        System.out.println("=== 11) toGlob: 交给系统 setSsidPattern 的 glob ===");
        eq("星号原样", WifiPlan.toGlob("i_sicau_*"), "i_sicau_*");
        eq("问号变点号", WifiPlan.toGlob("i_sicau_wifi?"), "i_sicau_wifi.");
        eq("点号被转义", WifiPlan.toGlob("a.b"), "a\\.b");
        eq("反斜杠被转义", WifiPlan.toGlob("a\\b"), "a\\\\b");
        eq("普通名字不变", WifiPlan.toGlob("i_sicau_wifi6"), "i_sicau_wifi6");

        System.out.println("=== 12) mergeKeepStrongest: 扫描列表去重 + 排序 ===");
        // 用假的扫描项: "名字:信号" 这样拼出来的字符串当 key
        WifiPlan.Key<String> key = new WifiPlan.Key<String>() {
            public String of(String s) { return s.split(":")[0]; }
        };
        WifiPlan.Score<String> score = new WifiPlan.Score<String>() {
            public int of(String s) { return Integer.parseInt(s.split(":")[1]); }
        };

        eqList("同名留最强 + 按信号排序",
                WifiPlan.mergeKeepStrongest(Arrays.asList(
                        "A:-70", "B:-40", "A:-55", "C:-90", "B:-30"), key, score),
                "B:-30", "A:-55", "C:-90");

        eqList("同分保持原顺序(不把前面那条顶掉)",
                WifiPlan.mergeKeepStrongest(Arrays.asList(
                        "i_sicau_wifi6:-50", "CMCC:-50", "i_sicau_wifi6:-80"), key, score),
                "i_sicau_wifi6:-50", "CMCC:-50");

        eqList("名字大小写不同算同一个", 
                WifiPlan.mergeKeepStrongest(Arrays.asList(
                        "WiFi:-60", "wifi:-40"), key, score),
                "wifi:-40");

        eqList("空名字(隐藏网络)丢掉",
                WifiPlan.mergeKeepStrongest(Arrays.asList(
                        ":-30", "A:-60"), key, score),
                "A:-60");

        eqList("带空格的名字先 trim 再比",
                WifiPlan.mergeKeepStrongest(Arrays.asList(
                        " A :-70", "A:-50"), key, score),
                "A:-50");

        eqList("null 列表", WifiPlan.mergeKeepStrongest(null, key, score));
        eqList("空列表", WifiPlan.mergeKeepStrongest(Arrays.<String>asList(), key, score));

        System.out.println("=== 13) 校园网预设 ===");
        eqList("预设就是这两个", WifiPlan.campusPresets(), "i_sicau_wifi6", "i_sicau");
        eq("预设能匹配上自己", WifiPlan.matchesAny(WifiPlan.campusPresets(), "i_sicau_wifi6"), true);
        eq("预设能匹配上另一个", WifiPlan.matchesAny(WifiPlan.campusPresets(), "i_sicau"), true);
        eq("别家的不匹配", WifiPlan.matchesAny(WifiPlan.campusPresets(), "CMCC-8899"), false);

        System.out.println("=== 14) pickerEntries: 选择列表的顺序 ===");
        // 顺序应当是: 当前连着 → 校园网预设 → 扫描到的 → 自己填的
        eqLabeled("基本顺序",
                WifiPlan.pickerEntries("CMCC-8899",
                        Arrays.asList("我手打的名字"),
                        Arrays.asList("CMCC-8899", "i_sicau_wifi6", "邻居家WiFi"),
                        Arrays.asList("信号很好", "信号一般", "信号较弱")),
                "CMCC-8899　（当前已连接）",
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）",
                "邻居家WiFi　（信号较弱）",
                "我手打的名字　（当前填写）");

        eqLabeled("当前连着的就是预设时不重复",
                WifiPlan.pickerEntries("i_sicau_wifi6", null,
                        Arrays.asList("i_sicau_wifi6", "i_sicau"), null),
                "i_sicau_wifi6　（当前已连接）",
                "i_sicau　（校园网）");

        eqLabeled("读不到名字(空/null)时当前项不出现",
                WifiPlan.pickerEntries("", null, null, null),
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）");

        eqLabeled("<unknown ssid> 也算读不到",
                WifiPlan.pickerEntries("<unknown ssid>", null, null, null),
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）");

        eqLabeled("扫描里重复的名字只出现一次",
                WifiPlan.pickerEntries(null, null,
                        Arrays.asList("i_sicau", "i_sicau", "A"), Arrays.asList("x", "y", "z")),
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）",
                "A　（z）");

        eqLabeled("扫描名字和说明个数不齐也不炸",
                WifiPlan.pickerEntries(null, null, Arrays.asList("A", "B"), Arrays.asList("只有一条")),
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）",
                "A　（只有一条）",
                "B");

        eqLabeled("全是空的时候只剩两个预设",
                WifiPlan.pickerEntries(null, Arrays.<String>asList("  "), Arrays.<String>asList(""), null),
                "i_sicau_wifi6　（校园网）",
                "i_sicau　（校园网）");

        System.out.println();
        System.out.println("通过 " + pass + " 项, 失败 " + fail + " 项");
        if (fail > 0) System.exit(1);
    }
}
