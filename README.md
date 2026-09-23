# 校园网自动认证 (SICAU-AutoLogin)

四川农业大学校园网**自动认证**工具 —— **Windows 一个 EXE、安卓一个 APK**，连上校园网就自动把认证做完，不用再手动点浏览器。

> 同一个宿舍/教学楼的网，每次开机、每次断线重连都要重新登一遍，有时候登录页还会抽风要你重输密码 ——
> 这个工具就是为了省掉这一步。

---

## 它解决什么问题

校园网没认证的时候，你打开任何网页都会被网关劫持到登录页，要求填学号密码。
本工具复刻了这套「填表提交」的动作，然后：

- **开机就自动认证**（Windows 走「启动」文件夹快捷方式；安卓走开机广播）
- **断线自动补认证**（按你设的周期复查）
- **认证失败会告诉你原因**（密码错、账号停机、欠费、在线设备数超限……）
- Windows 端还能**把电脑变成 WiFi 热点**分享给手机

它只做「自动填表提交」这一件事。**密码只保存在你自己电脑/手机的本地配置里，不会上传到任何服务器。**

---

## 下载与安装

到本仓库的 **Releases** 页面下载：

| 平台 | 文件 | 怎么装 |
|---|---|---|
| Windows 10 / 11 | `SICAU-win-<版本>.exe` | 双击就能跑，免安装、无依赖（单文件） |
| Android 6.0+ | `SICAU-android-<版本>.apk` | 传到手机点开安装；提示「未知来源」时允许一下 |

> **Windows 版不会往桌面塞快捷方式**：程序放在哪个文件夹就在哪跑。
> 想放桌面就自己复制过去；用「安装到其他目录」也能搬家（会顺手把开机自启指过去）。

---

## 怎么用（Windows）

1. 双击 `SICAU-win-<版本>.exe`
2. 第一次会弹**向导**：上半部分自动检测你的网络环境，下半部分填学号和密码
3. 主界面顶部选**「有线」**还是**「无线」**（插网线点有线，用校园 WiFi 点无线）
4. 勾上**「开机自动认证」**，点**「保存并应用」**

之后就不用管了。窗口右上角那个**「认证成功后」**决定认证完怎么收场：

| 选项 | 干什么 |
|---|---|
| **自动退出程序**（默认） | 认证完直接结束进程，最安静，不留后台 |
| **留在托盘监测** | 收进右下角托盘，按「检查周期」定期复查，断线自动补认证 |

只有**认证失败**才会弹窗告诉你原因。

<details>
<summary>Windows 端还有什么（点开）</summary>

- **热点页签**：开/关热点、看已连设备、修热点（频段锁 2.4GHz 兼容性最好）
- **界面主题**：跟随系统 / 浅色 / 深色（仿 Win11 配色）
- **日志保留**：默认只留 24 小时的日志（0 = 不自动删）
- **不自动弹校园网登录页**：连上没有互联网的 WiFi 时 Windows 会自己探测并给你开登录页；
  勾上这个就把那个探测关掉（改的是系统设置，**需要管理员权限**，程序会问你要不要以管理员身份重开）
- 「关于」对话框里有作者信息和版本

</details>

---

## 怎么用（安卓）

1. 装好后打开「校园网自动认证」
2. 点**「i_sicau_wifi6」**或**「i_sicau」**按钮 —— 学校就这两个 WiFi 名，程序里已经做好了；
   点一下自动帮你连（系统会弹确认框，点「连接」即可）
3. 填学号、密码
4. 检查周期填 15 → 勾上「开机自动认证 + 后台定时检查」→ 点**「保存并应用」**
5. 国产手机（小米/华为/OPPO/vivo/荣耀）建议再勾上**「常驻通知栏」**，后台存活率明显更高

<details>
<summary>安卓上几个绕不过去的系统限制（点开看，免得以为是 bug）</summary>

**1）为什么点「连接 WiFi」一定要我点一下确认？**
安卓 10 起不允许应用偷偷改 WiFi，系统会弹确认框。这是谷歌的隐私限制，任何应用都绕不过去。

**2）为什么读不到 WiFi 名字？**
安卓从 8.1 起就把「WiFi 名字」当位置隐私来管：要位置权限、安卓 10 起还要求系统定位开关打开，
有的 ROM 就算权限齐全也不给。**但这不影响认证** —— 程序认网络看的是网卡 IP，不是名字。
想查为什么读不到：**点一下界面上那行 WiFi 状态文字**，会弹出诊断信息。

**3）连上后弹出来的登录页能关掉吗？**
那个弹窗是**系统**的强制门户检测弹的（不是本程序）。最简单的办法是把「开机自动认证」勾上，
让本程序抢先认证完，系统就不弹了。想彻底关：
- Windows：勾「不自动弹校园网登录页」
- 安卓：勾「减少『登录到网络』打扰」，它会告诉你怎么关通知；要彻底关得用电脑执行
  `adb shell settings put global captive_portal_mode 0`（APK 里有完整说明）

**4）扫描列表里为什么只有几个 WiFi？**
安卓 12 起系统只让应用看到「你连过的」网络。所以列表里大概率只有校园网 + 你连过的别人家网络。
扫不到就直接在输入框里打名字。

</details>

---

## 命令行（Windows，一般用不到）

```
SICAU-win-<版本>.exe                              打开界面
SICAU-win-<版本>.exe --auto                       静默认证一次（开机自启用的就是这个）
SICAU-win-<版本>.exe --auto --hotspot-on-boot     认证成功后顺手开热点
SICAU-win-<版本>.exe --install [目录]             安装到指定目录
SICAU-win-<版本>.exe --uninstall [--yes]          卸载
SICAU-win-<版本>.exe --hotspot status|on|off      热点开关
SICAU-win-<版本>.exe --hotspot repair             修复热点
```

---

## 文件与配置都在哪

| 内容 | Windows | 安卓 |
|---|---|---|
| 配置 | `%APPDATA%\SICAU-AutoLogin\config.conf` | 应用私有目录（SharedPreferences） |
| 日志 | `%APPDATA%\SICAU-AutoLogin\autologin.log` | 界面下方日志区 + 应用私有目录 |
| 开机自启 | 「启动」文件夹里的快捷方式 | 系统开机广播 |

> 配置文件里的密码是**明文**保存的。在自己机器上问题不大，但别把配置文件截图发出去，也别整个文件夹外发。
> 发现账号异常请及时改校园网密码。

---

## 自己编译

需要的东西：

- **Windows 版**：.NET Framework 4.x 自带的 `csc.exe`（Windows 上本来就有，**不用装任何东西**）
- **安卓版**：JDK 8+ 和 Android SDK 的 build-tools 35.0.0（放在 `.build\sdk` 下，构建脚本会自己找）

```powershell
# Windows：编译并更新项目根目录那份 EXE，顺便校正开机自启快捷方式
powershell -NoProfile -ExecutionPolicy Bypass -File .build\build-win.ps1

# 安卓：打包签名，产物落到 android\SICAU-android-<版本>.apk
powershell -NoProfile -ExecutionPolicy Bypass -File .build\build-apk.ps1
```

- **发版改版本号**：Windows 改 `build-win.ps1` 顶部的 `$VER` 和 `Core.cs` 里的 `AppVersion`；
  安卓改 `apk-src\AndroidManifest.xml` 的 `versionCode` + `versionName`。构建脚本会自动改名并删掉旧版本的包。
- **安卓签名**：用 `.build\sicau.keystore`。**你自己发布一定要换成自己的 keystore**，否则升级安装会签名冲突。
- **头像资源**：`powershell -File .build\生成头像资源.ps1 -Source <你的方形头像>`，
  会同时生成 Windows 用的圆形 PNG（编进 EXE）和安卓各密度用的 JPEG。

### 不用手机也能验证安卓逻辑

认证协议那块（`SicauAuth.java` / `Http.java` / `AppLog.java`）**不依赖任何 `android.*` API**，
可以在电脑上的 JVM 里跑真实认证；WiFi 名字解析/匹配那部分有 69 项单元测试：

```powershell
$J = '.build\jvmtest'
javac -encoding UTF-8 -d "$J\out" .build\apk-src\WifiPlan.java "$J\WifiPlanTest.java"
java -Dfile.encoding=UTF-8 -cp "$J\out" WifiPlanTest
```

---

## 认证原理（想了解的看这里）

深澜(Srun) + 华为 AC 的 Web 认证：

1. 未认证时网关把 HTTP 请求 302 到 `portal.sicau.edu.cn/portal.do?wlanuserip=..&wlanacname=..&nasip=..`
2. 抓这个页面，解析里面的隐藏表单（共 34 个字段）
3. 把 `userId` / `passwd` **覆盖**进表单 —— 不能追加，追加会变成两个同名参数，门户只取第一个（空值）
4. POST 到 `https://portal.sicau.edu.cn/webauth.do;<JSESSIONID>?<同一串参数>`
   —— **会话号在 URL 里，不在 cookie**，必须带上
5. 同账号只能一台设备在线：第一次提交是把别的设备踢下线，本机可能要提交第二次

没有 JS 加密、没有 token，所以只要复刻这一步就够了。

<details>
<summary>踩过的坑（开发时记的，点开）</summary>

- **校园网 IPv6 不通**：DNS 会先返回 IPv6 地址但那条路是死的（curl -6 超时、-4 只要 49ms），
  而 .NET 的 HttpWebRequest 不会回退，所以程序自己解析 IPv4、用 Host 头带真实域名。
- **证书校验**：服务器是 `*.sicau.edu.cn` 通配符证书，JDK 默认校验器会误判，两端都改成自己解析 SAN 列表。
- **安卓 12 起 `WifiInfo.getIpAddress()` 废弃固定返回 0**：旧代码拿不到 IP → 请求不绑 WiFi → 走移动流量
  → 误判「已联网」或认证失败。改成走 `ConnectivityManager` + `LinkProperties` 拿地址。
- **安卓 10 起不能偷偷改 WiFi**：老 API 直接失效，只能走系统弹窗（`WifiNetworkSpecifier`）。
- **Windows 开机自启不能用 `schtasks`**：Windows Defender 会把「未签名程序 + 登录任务」判成
  `Behavior:Win32/Persistence.A!ml` 然后**把 EXE 和计划任务一起删掉**。改用「启动」文件夹快捷方式。
- **热点频段别每次都重设**：`HotspotBand != "auto"` 不是「变了」，会导致每次保存都重启热点、踢设备。
  改成保存时现读系统值逐项比对，只有真的不同才写。

</details>

---

## 免责声明

- 本工具只是**代替你手动点一次登录页**，不修改任何校园网服务端设置、不做任何破解。
- 请遵守学校的网络使用规定，仅在**你自己的账号**上使用。
- 本项目是个人学习作品，**与四川农业大学官方无关**，不代表学校。
- 用之前请自己评估风险，出了问题（账号、网络、设备）作者不承担责任。

## 许可证

[MIT](LICENSE) —— 随便用、随便改，保留版权声明即可。

---

作者 **极夜光** · 邮箱 <yeguang225@outlook.com>
