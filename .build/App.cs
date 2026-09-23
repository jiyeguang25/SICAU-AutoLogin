using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sicau
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        /// <summary>命令行模式下把输出接到父进程控制台(被重定向时直接用标准输出)。</summary>
        static void ConsoleInit()
        {
            bool redirected = true;
            try { redirected = Console.IsOutputRedirected; } catch { }
            if (!redirected) { try { AttachConsole(-1); } catch { } }
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Core.InitPaths();
            Core.RemoveLegacyTask();   // 老版本用计划任务自启, 被 Defender 判成恶意持久化, 见到就清掉
            Theme.ApplyMode(Core.LoadCfg().Theme);   // 先定主题, 后面所有界面按它来画

            bool auto = false, hotspotOnBoot = false, yes = false, doInstall = false, doUninstall = false;
            string hotspotAction = null;
            string installDir = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--auto") auto = true;
                else if (a == "--hotspot-on-boot") hotspotOnBoot = true;
                else if (a == "--yes" || a == "-y") yes = true;
                else if (a == "--install")
                {
                    doInstall = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) { installDir = args[i + 1]; i++; }
                }
                else if (a == "--uninstall") doUninstall = true;
                else if (a == "--hotspot")
                {
                    var hs = new StringBuilder();
                    for (int j = i + 1; j < args.Length; j++)
                    {
                        if (hs.Length > 0) hs.Append(' ');
                        string v = args[j];
                        if (v.IndexOf(' ') >= 0) v = "\"" + v + "\"";
                        hs.Append(v);
                    }
                    hotspotAction = hs.ToString();
                    i = args.Length;
                }
                else if (a.StartsWith("--hotspot=")) hotspotAction = a.Substring("--hotspot=".Length);
            }

            // ---- 卸载 ----
            if (doUninstall)
            {
                if (!yes)
                {
                    var ask = MessageBox.Show(
                        "确定要卸载「校园网自动认证」吗？\n\n" +
                        "将删除：\n" +
                        "  · 开机自启动任务\n" +
                        "  · 开始菜单 / 桌面快捷方式\n" +
                        "  · 程序文件\n\n" +
                        "配置和运行日志会保留。",
                        "卸载 校园网自动认证", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (ask != DialogResult.Yes) return;
                }
                string msg = Core.Uninstall(false, true);
                if (!yes) MessageBox.Show("卸载完成。\n\n" + msg, "校园网自动认证", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ---- 安装 ----
            if (doInstall)
            {
                if (string.IsNullOrEmpty(installDir)) installDir = Core.DefaultInstallDir();
                var cfgIns = Core.LoadCfg();
                bool wantTask = !string.IsNullOrWhiteSpace(cfgIns.UserId) && !string.IsNullOrEmpty(cfgIns.Password);
                var ir = Core.InstallTo(installDir, true, wantTask);
                ConsoleInit();
                Console.Out.WriteLine(ir.Ok ? ("已安装到: " + ir.Target) : ("安装失败: " + ir.Message));
                Console.Out.Flush();
                return;
            }

            // ---- 热点命令行 ----
            if (hotspotAction != null)
            {
                string hsOut = Core.RunHotspot(hotspotAction);
                ConsoleInit();
                Console.Out.WriteLine(hsOut);
                Console.Out.Flush();
                return;
            }

            if (auto)
            {
                // 开机这一刻是在跟浏览器的认证页弹窗抢时间: 把自己的优先级提上去
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }

                var cfg = Core.LoadCfg();
                Core.Log("--------------------------------------------------");
                bool trayMode = MainForm.WantTray(cfg.BootMode);
                Core.Log("开机自动认证: 静默认证, 成功后"
                         + (trayMode ? "留在托盘继续监测" : "直接退出") + ", 失败才弹窗");

                // 计划任务已经不再延迟启动了(原来登录后要等 20 秒), 所以这里自己等网卡就绪:
                // 一般是开机后 2~6 秒就好, 比固定等 20 秒快得多, 也不会在没网时就白认证一次。
                var bootWatch = System.Diagnostics.Stopwatch.StartNew();
                Core.WaitForNetwork(45);

                Core.AuthOutcome outcome = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try { outcome = Core.DoAuth(cfg); }
                    catch (Exception ex) { Core.Log("异常: " + ex.Message, "ERROR"); }
                    if (outcome != null && outcome.Ok) break;
                    if (attempt < 3 && Core.LooksLikeNotReady(outcome))
                    {
                        Core.Log("第 " + attempt + " 次没成, 可能是网络还没就绪, 5 秒后再试一次");
                        System.Threading.Thread.Sleep(5000);
                        continue;
                    }
                    break;
                }
                bool ok = (outcome != null && outcome.Ok);
                Core.Log("开机认证共耗时 " + bootWatch.ElapsedMilliseconds + " ms");
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }

                if (hotspotOnBoot && ok)
                {
                    Core.Log("登录后自动开启移动热点");
                    try
                    {
                        string r = Core.RunHotspot("on");
                        if (!string.IsNullOrEmpty(r)) Core.Log(r.Replace("\r\n", " | ").Replace("\n", " | "));
                    }
                    catch (Exception ex) { Core.Log("热点启动失败: " + ex.Message, "WARN"); }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 成功: 安安静静地收束, 不打扰用户
                if (ok)
                {
                    if (!trayMode)
                    {
                        Core.Log("认证成功, 本次不驻留, 直接退出");
                        return;
                    }
                    Core.Log("认证成功, 收束到托盘继续监测");
                    Application.Run(new MainForm { StartHidden = true });
                    return;
                }

                // 没成功才弹窗, 让用户看到到底卡在哪
                Core.Log("认证未成功, 弹出提示窗口");
                bool stay = false;
                try
                {
                    using (var f = new BootResultForm(outcome, cfg))
                    {
                        f.ShowDialog();
                        stay = f.StayInTray;
                    }
                }
                catch (Exception ex) { Core.Log("提示窗口异常: " + ex, "ERROR"); }

                if (stay)
                {
                    Core.Log("用户选择打开主界面");
                    Application.Run(new MainForm());
                }
                else Core.Log("用户选择退出");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 首次运行: 弹一个向导, 自动检测环境并引导填账号
            var cfg0 = Core.LoadCfg();
            if (string.IsNullOrWhiteSpace(cfg0.UserId))
            {
                using (var wiz = new FirstRunForm())
                {
                    wiz.ShowDialog();
                }
            }

            Application.Run(new MainForm());
        }
    }

    /// <summary>
    /// 开机模式二用的确认窗口: 认证完成后把结果告诉用户,
    /// 由用户决定是直接退出还是留在托盘继续后台跑。
    /// </summary>
    public class BootResultForm : Form
    {
        public bool StayInTray = false;

        public BootResultForm(Core.AuthOutcome o, Cfg c)
        {
            bool ok = (o != null && o.Ok);
            string head, detail;

            if (o == null)
            {
                head = "认证异常";
                detail = "认证过程中出了错，可以点「留在托盘」打开主界面看日志。";
            }
            else if (o.Ok && o.AlreadyOnline)
            {
                head = "已联网，无需认证";
                detail = "当前网络本来就是通的，没有做任何操作。";
            }
            else if (o.Ok)
            {
                head = "认证成功";
                detail = "校园网已连接，可以正常上网了。";
            }
            else
            {
                head = "认证失败";
                if (!string.IsNullOrEmpty(o.Reason)) detail = o.Reason;
                else if (!string.IsNullOrEmpty(o.Message)) detail = o.Message;
                else detail = "原因未知，可以点「留在托盘」打开主界面看日志。";
            }

            Color dot = ok ? Theme.Ok : Theme.Err;

            Text = "开机自动认证";
            Icon = AppIcon.Get(AppIcon.Green);
            AutoScaleMode = AutoScaleMode.None;
            Font = Fonts.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(26, 22, 26, 20),
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- 标题 ----
            root.Controls.Add(new Label
            {
                Text = "校园网自动认证",
                Font = Fonts.Title,
                ForeColor = Theme.Ink,
                BackColor = Theme.Bg,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            }, 0, 0);

            // ---- 结果卡片 ----
            var card = new CardPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(22, 18, 22, 18),
                Margin = new Padding(0, 0, 0, 14)
            };
            var body = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(new Label
            {
                Text = "●  " + head,
                Font = Fonts.Status,
                ForeColor = dot,
                BackColor = Theme.Card,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 0);
            body.Controls.Add(new Label
            {
                Text = detail,
                ForeColor = Theme.Ink,
                BackColor = Theme.Card,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Margin = new Padding(0, 0, 0, 8)
            }, 0, 1);
            body.Controls.Add(new Label
            {
                Tag = "dim",
                Text = "学号 " + (string.IsNullOrWhiteSpace(c.UserId) ? "(未设置)" : c.UserId)
                     + "      " + DateTime.Now.ToString("HH:mm:ss"),
                ForeColor = Theme.InkDim,
                BackColor = Theme.Card,
                AutoSize = true,
                Margin = new Padding(0)
            }, 0, 2);
            card.Controls.Add(body);
            root.Controls.Add(card, 0, 1);

            // ---- 按钮 ----
            // 这个窗口只在"开机认证没成功"时才出现, 所以主按钮是"打开主界面去处理"
            var btnTray = new Win11Button { Text = "打开主界面", Primary = true, MinimumSize = new Size(120, 36) };
            var btnExit = new Win11Button { Text = "退出程序", MinimumSize = new Size(110, 36) };
            btnExit.Click += (s, e) => { StayInTray = false; DialogResult = DialogResult.OK; Close(); };
            btnTray.Click += (s, e) => { StayInTray = true; DialogResult = DialogResult.OK; Close(); };

            var btns = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Theme.Bg,
                Margin = new Padding(0)
            };
            btns.Controls.Add(btnExit);
            btns.Controls.Add(btnTray);
            root.Controls.Add(btns, 0, 3);

            Controls.Add(root);
            AcceptButton = btnExit;
            CancelButton = btnTray;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Dwm.RoundCorners(Handle);
            Dwm.SetTitleBarDark(Handle, Theme.Dark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 有的系统要等窗口真正显示出来才认这个属性, 所以显示后再补一次
            Dwm.SetTitleBarDark(Handle, Theme.Dark);
        }

    }

    public class MainForm : Form
    {
        InputBox txtUser, txtPass;
        InputBox txtHotspotSsid, txtHotspotPass;
        TextBox txtLog;
        string curHotspotSsid = "", curHotspotPass = "";
        CheckBox chkAutostart, chkAutoHotspot, chkQuietPortal, chkShow;
        NumericUpDown numInterval, numLogKeep;
        Label lblStatus, lblHint, lblLogKeepHint;
        Win11Button btnSave, btnAuth, btnHotOn, btnHotOff, btnHotStatus, btnOpen, btnClear, btnProbe;
        Win11Button btnInstall, btnUninstall, btnAbout, btnRepair;
        FlatCombo cboBand, cboTheme, cboBoot;
        int modeIdx = 0;   // 连接方式: 0=有线, 1=无线。由「有线 / 无线」页签决定, 没有"自动"
        Win11Button btnTabWired, btnTabWifi, btnTabHot;
        TableLayoutPanel gridHost, grid;   // gridHost = 页签条 + 设置表;  grid = 三个页签共用的那张表
        int tabIdx = 1;
        bool uiReady = false;
        FlatCombo cboSsid;
        NotifyIcon tray;
        bool trayTipShown = false;
        bool reallyExit = false;
        /// <summary>true = 启动时不显示窗口, 只留托盘图标(选"留在托盘"时用)。</summary>
        public bool StartHidden = false;
        Color lastStatusColor = SystemColors.ControlText;
        bool busy = false;

        static readonly Font FontUI = new Font("Microsoft YaHei UI", 9F);
        static readonly Font FontTitle = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
        static readonly Font FontMono = new Font("Consolas", 8.5F);

        public MainForm()
        {
            Icon = AppIcon.Get(AppIcon.Green);   // 窗口/任务栏图标, 跟 EXE 图标保持一致
            BuildUi();
            BuildTray();
            Core.OnLog += AppendLog;
            LoadToUi();
            AppendLog("SICAU-AutoLogin 启动");
            AppendLog("配置目录: " + Core.Dir);
            SetStatus("就绪", Theme.InkDim);

            // 配置里如果记着"不要自动弹登录页", 启动时尽量把它落到位
            // (没管理员权限时只是写条日志, 不弹窗打扰)
            try { ApplyQuietPortal(Core.LoadCfg().QuiethPortal, true); } catch { }

            // 启动时按主题刷一遍控件色。
            // (不能指望 cboTheme 的 SelectedIndexChanged: 值没变时它不会触发)
            Restyle(this, Theme.Bg);
            uiReady = true;
        }

        // ================================================================
        //  界面布局
        //  设计基准 96 DPI; 运行时按 DeviceDpi/96 等比放大整块布局,
        //  这样高分屏下字体和控件一起放大, 不会出现文字被裁切。
        // ================================================================
        const int BaseW = 700;
        const int BaseH = 620;
        bool dpiApplied = false;
        Label tipLabel;
        Control[] bottomRow;

        // ================================================================
        //  界面: 全部用 TableLayoutPanel + AutoSize 自动排版。
        //  标签宽度由文字自己撑开, 不管系统 DPI / 字体怎么变都不会被裁切。
        // ================================================================

        // ================================================================
        //  Win11 风格界面
        //  浅灰底 + 白色圆角卡片 + 扁平圆角按钮/输入框
        //  排版用 TableLayoutPanel + AutoSize, 标签宽度由文字自己撑开
        // ================================================================
        TableLayoutPanel root;
        FlowLayoutPanel pnlButtons, pnlBottom, pnlChecks;
        CardPanel cardSettings, cardChecks, cardLog;
        Panel header;   // 标题栏(左: 名称 / 右: 界面主题 + 认证成功后)
        TableLayoutPanel titleBox;      // 标题栏左边那块(名称 + 副标题)
        FlowLayoutPanel headerSettings; // 标题栏右边那两个下拉框
        Win11Button btnScan;
        int gridRows = 0;

        void BuildUi()
        {
            Text = "四川农业大学 校园网自动认证";
            AutoScaleMode = AutoScaleMode.None;
            Font = Fonts.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 560);      // 起始就给小一点, AdaptSize 只会往大了调
            MinimumSize = new Size(560, 420);

            root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(14, 8, 14, 8),
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 标题栏里要用这两个下拉框, 所以先建好(它们都是 MainForm 的字段, 建在哪不影响别的)
            cboTheme = new FlatCombo(100);
            cboTheme.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            cboTheme.SelectedIndex = 0;
            cboTheme.SelectedIndexChanged += (s, e) => ApplyTheme(SelectedTheme(), false);

            cboBoot = new FlatCombo(116);
            cboBoot.Items.AddRange(new object[] { "自动退出程序", "留在托盘监测" });
            cboBoot.SelectedIndex = 0;
            cboBoot.SelectedIndexChanged += (s, e) => UpdateIntervalEnabled();

            // ---------------- 标题栏: 左边名称, 右边两个全局设置 ----------------
            //  「界面主题」「认证成功后」是全局项, 放标题这一行里 —— 哪个页签都看得见, 又不占设置卡片的地方。
            //  (标题栏用 Panel + 手动靠右, 不用 TableLayoutPanel: 嵌套的 AutoSize 表格
            //   和 Dock=Fill 的百分比列凑一起会让 WinForms 排版递归到栈溢出)
            header = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Bg,
                Margin = new Padding(2, 2, 0, 14)
            };

            titleBox = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Bg,
                Margin = new Padding(0)
            };
            titleBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleBox.Controls.Add(new Label
            {
                Text = "校园网自动认证",
                Font = Fonts.Title,
                ForeColor = Theme.Ink,
                BackColor = Theme.Bg,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            }, 0, 0);
            titleBox.Controls.Add(new Label
            {
                Text = "portal.sicau.edu.cn · 开机自动登录",
                ForeColor = Theme.InkDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                Tag = "dim",
                Margin = new Padding(1, 0, 0, 0)
            }, 0, 1);
            titleBox.Location = new Point(0, 0);
            header.Controls.Add(titleBox);

            headerSettings = Flow(pnl =>
            {
                pnl.BackColor = Theme.Bg;
                pnl.Location = new Point(360, 7);
                pnl.Controls.Add(HeaderField("界面主题", cboTheme));
                pnl.Controls.Add(HeaderField("认证成功后", cboBoot));
            });
            header.Controls.Add(headerSettings);
            // 窗口变宽时, 这两个设置跟着贴到右边去
            //  (挂在 SizeChanged 上而不是 Layout 上: 改子控件位置不会再触发 SizeChanged,
            //   挂 Layout 会递归, 直接栈溢出崩溃)
            header.SizeChanged += (s, e) =>
            {
                int x = header.ClientSize.Width - headerSettings.Width;
                headerSettings.Left = Math.Max(titleBox.Right + 12, x);
            };
            root.Controls.Add(header, 0, 0);

            // ---------------- 设置卡片 ----------------
            cardSettings = new CardPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 10, 14, 4),
                Margin = new Padding(0, 0, 0, 6)
            };
            // 分页容器: 第一行是页签条, 第二行是设置表(三个页签共用, 靠隐藏行切换)
            gridHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            gridHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 2; i++) gridHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            txtUser = new InputBox(150);
            txtPass = new InputBox(150);
            txtPass.Inner.UseSystemPasswordChar = true;

            // 热点名称/密码: 打开界面时自动读出当前值填上
            txtHotspotSsid = new InputBox(150);
            txtHotspotPass = new InputBox(150);

            chkShow = new CheckBox
            {
                Text = "显示密码",
                AutoSize = true,
                Font = Fonts.UI,
                ForeColor = Theme.Ink,
                BackColor = Theme.Card,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(14, 0, 0, 10)
            };
            chkShow.CheckedChanged += (s, e) => txtPass.Inner.UseSystemPasswordChar = !chkShow.Checked;
            var pnlPass = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Controls.Add(txtPass);
                pnl.Controls.Add(chkShow);
            });

            numInterval = new NumericUpDown
            {
                Width = 76,
                Minimum = 0,
                Maximum = 1440,
                Value = 5,
                Font = Fonts.UI,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 10, 10)
            };
            lblHint = new Label
            {
                Tag = "dim",
                Text = "分钟（0 = 不复检）",
                ForeColor = Theme.InkDim,
                BackColor = Theme.Card,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 10)
            };

            // 日志保留: 超过这个小时的日志行自动删掉(0 = 不删), 免得日志越堆越大
            numLogKeep = new NumericUpDown
            {
                Width = 76,
                Minimum = 0,
                Maximum = 720,
                Value = 24,
                Font = Fonts.UI,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 10, 10)
            };
            lblLogKeepHint = new Label
            {
                Tag = "dim",
                Text = "小时（0 = 不自动删）",
                ForeColor = Theme.InkDim,
                BackColor = Theme.Card,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 10)
            };
            var pnlInterval = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Controls.Add(numInterval);
                pnl.Controls.Add(lblHint);
            });

            // 连接方式不放下拉框: 上面的「有线 / 无线」页签就是连接方式(见 ShowTab / SelectedMode)
            cboSsid = new FlatCombo(150);
            cboSsid.DropDownStyle = ComboBoxStyle.DropDown;
            btnScan = new Win11Button
            {
                Text = "扫描",
                MinimumSize = new Size(66, 34),
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 0, 10)
            };
            btnScan.Click += (s, e) => ScanWifiAsync();
            var pnlSsid = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Controls.Add(cboSsid);
                pnl.Controls.Add(btnScan);
            });

            cboBand = new FlatCombo(150);
            cboBand.Items.AddRange(new object[] { "自动", "仅 2.4GHz", "仅 5GHz" });
            cboBand.SelectedIndex = 0;


            // ---- 页签条: 有线 / 无线 / 热点 ----
            var tabStrip = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Margin = new Padding(0, 0, 0, 4);
                btnTabWired = MkTab("有线", 0);
                btnTabWifi = MkTab("无线", 1);
                btnTabHot = MkTab("热点", 2);
                pnl.Controls.Add(btnTabWired);
                pnl.Controls.Add(btnTabWifi);
                pnl.Controls.Add(btnTabHot);
            });
            gridHost.Controls.Add(tabStrip, 0, 0);

            // ---- 设置表: 三个页签共用同一张表, 靠隐藏行切换 ----
            //  以前是每页一张小表, 各自算列宽: 「WiFi 名称」那一行的标签和输入框
            //  比上面「学号/密码/检查周期」整排右错 4px, 行高也差 10px, 看着就是歪的。
            //  合并成一张表之后, 三个页签的标签列、控件列都是同一套宽度。
            grid = MakeGrid(6);
            // 两个标签列固定宽度: 否则哪一页可见的标签宽一点(比如「WiFi 名称」比「检查周期」宽 4px),
            // 整列的输入框就跟着左右跳, 三个页签对不齐
            //  两个标签列定死宽度, 两个控件列按当前字号算出来的实际宽度定死:
            //  这样三个页签完全对齐, 而且横跨三列的「热点操作」按钮不会把第一列撑宽(原来会多 78px)
            grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 72);
            grid.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 72);
            grid.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute,
                Math.Max(txtUser.Width, Math.Max(txtHotspotSsid.Width, Math.Max(cboBand.Width, pnlInterval.PreferredSize.Width))) + 26);
            grid.ColumnStyles[3] = new ColumnStyle(SizeType.Absolute,
                Math.Max(txtHotspotPass.Width, pnlPass.PreferredSize.Width) + 26);
            PutRow(grid, 0, "学号", txtUser, "密码", pnlPass);
            var pnlLogKeep = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Controls.Add(numLogKeep);
                pnl.Controls.Add(lblLogKeepHint);
            });
            PutRow(grid, 1, "检查周期", pnlInterval, "日志保留", pnlLogKeep);
            PutRow(grid, 2, "WiFi 名称", pnlSsid, null, null);                         // 无线页
            PutRow(grid, 3, "热点名称", txtHotspotSsid, "热点密码", txtHotspotPass);   // 热点页
            PutRow(grid, 4, "热点频段", cboBand, null, null);                          // 热点页
            var pnlHotBtns = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                pnl.Controls.Add(btnHotOn = MkB("开热点", false, (s, e) => DoHotspot("on")));
                pnl.Controls.Add(btnHotOff = MkB("关热点", false, (s, e) => DoHotspot("off")));
                pnl.Controls.Add(btnHotStatus = MkB("热点状态", false, (s, e) => DoHotspot("status")));
                pnl.Controls.Add(btnRepair = MkB("修复热点", false, (s, e) => DoRepairHotspot()));
            });
            // 这一行横跨右边三列: 否则那一格被 4 个按钮撑到 400 宽, 整页(连窗口)会跟着变宽
            pnlHotBtns.Margin = new Padding(0, 0, 0, 6);
            grid.Controls.Add(MakeLabel("热点操作"), 0, 5);
            grid.SetColumnSpan(pnlHotBtns, 3);
            grid.Controls.Add(pnlHotBtns, 1, 5);
            gridHost.Controls.Add(grid, 0, 1);

            cardSettings.Controls.Add(gridHost);
            root.Controls.Add(cardSettings, 0, 1);

            // ---------------- 复选项卡片 ----------------
            cardChecks = new CardPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 6, 14, 6),
                Margin = new Padding(0, 0, 0, 6)
            };
            pnlChecks = Flow(pnl =>
            {
                pnl.BackColor = Theme.Card;
                chkAutostart = new CheckBox
                {
                    Text = "开机自动认证",
                    AutoSize = true,
                    Checked = true,
                    Font = Fonts.UI,
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Card,
                    Margin = new Padding(0, 0, 28, 0)
                };
                chkAutoHotspot = new CheckBox
                {
                    Text = "登录后自动开热点",
                    AutoSize = true,
                    Font = Fonts.UI,
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Card,
                    Margin = new Padding(0, 0, 28, 0)
                };
                chkQuietPortal = new CheckBox
                {
                    Text = "不自动弹校园网登录页",
                    AutoSize = true,
                    Font = Fonts.UI,
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Card,
                    Margin = new Padding(0)
                };
                pnl.Controls.Add(chkAutostart);
                pnl.Controls.Add(chkAutoHotspot);
                pnl.Controls.Add(chkQuietPortal);
            });
            cardChecks.Controls.Add(pnlChecks);
            root.Controls.Add(cardChecks, 0, 2);

            // ---------------- 功能按钮 ----------------
            pnlButtons = Flow(pnl =>
            {
                pnl.BackColor = Theme.Bg;
                pnl.Margin = new Padding(2, 0, 0, 6);
                pnl.Controls.Add(btnSave = MkB("保存并应用", true, (s, e) => DoSave()));
                pnl.Controls.Add(btnAuth = MkB("立即认证", false, (s, e) => DoAuthAsync(false)));
                pnl.Controls.Add(btnProbe = MkB("检测状态", false, (s, e) => DoProbeAsync()));
                // 开/关/状态/修复热点 都放在「热点」页签里了, 这里不再重复
            });
            root.Controls.Add(pnlButtons, 0, 3);

            // ---------------- 状态 ----------------
            lblStatus = new Label
            {
                Text = "●  就绪",
                Font = Fonts.Status,
                ForeColor = Theme.InkDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                Margin = new Padding(4, 2, 0, 6)
            };
            root.Controls.Add(lblStatus, 0, 4);

            // ---------------- 日志卡片 ----------------
            cardLog = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0, 0, 0, 6)
            };
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = Fonts.Mono,
                BackColor = Theme.Card,
                ForeColor = Theme.Ink,
                BorderStyle = BorderStyle.None
            };
            cardLog.Controls.Add(txtLog);
            root.Controls.Add(cardLog, 0, 5);

            // ---------------- 底部 ----------------
            pnlBottom = Flow(pnl =>
            {
                pnl.BackColor = Theme.Bg;
                pnl.Margin = new Padding(2, 0, 0, 0);
                pnl.Controls.Add(btnOpen = MkB("打开配置目录", false, (s, e) => { try { Process.Start("explorer.exe", "\"" + Core.Dir + "\""); } catch { } }));
                pnl.Controls.Add(btnClear = MkB("清空日志", false, (s, e) => txtLog.Clear()));
                pnl.Controls.Add(btnInstall = MkB("安装到其他目录", false, (s, e) => DoInstallTo()));
                pnl.Controls.Add(btnUninstall = MkB("卸载", false, (s, e) => DoUninstall()));
                pnl.Controls.Add(btnAbout = MkB("关于", false, (s, e) => ShowAbout()));
            });
            root.Controls.Add(pnlBottom, 0, 6);

            tipLabel = new Label
            {
                Tag = "dim",
                Text = "配置和日志保存在 " + Core.Dir,
                ForeColor = Theme.InkDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                Margin = new Padding(4, 4, 0, 0)
            };
            root.Controls.Add(tipLabel, 0, 7);

            Controls.Add(root);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Dwm.RoundCorners(Handle);      // Win11 圆角窗口(旧系统自动忽略)
            Dwm.SetTitleBarDark(Handle, Theme.Dark);
            RefreshTrayStateAsync();       // 探一次网络, 决定托盘图标蓝还是红
            StartTrayTimer();              // 留在托盘时按周期自动复检
        }

        /// <summary>标题栏右边用的「小标签 + 下拉框」, 标签跟下拉框垂直居中。</summary>
        Control HeaderField(string label, Control box)
        {
            box.Margin = new Padding(0);
            return Flow(pnl =>
            {
                pnl.BackColor = Theme.Bg;
                pnl.Margin = new Padding(12, 0, 0, 0);
                pnl.Controls.Add(new Label
                {
                    Text = label,
                    ForeColor = Theme.InkDim,
                    BackColor = Theme.Bg,
                    AutoSize = true,
                    Tag = "dim",
                    Margin = new Padding(0, 7, 6, 0)
                });
                pnl.Controls.Add(box);
            });
        }

        Win11Button MkB(string text, bool primary, EventHandler onClick)
        {
            var b = new Win11Button { Text = text, Primary = primary };
            b.Click += onClick;
            return b;
        }

        string SelectedTheme()
        {
            if (cboTheme.SelectedIndex == 1) return "light";
            if (cboTheme.SelectedIndex == 2) return "dark";
            return "auto";
        }

        string SelectedBootMode()
        {
            // 0 = 认证成功后自动退出, 1 = 认证成功后留在托盘继续监测
            return cboBoot.SelectedIndex == 1 ? "tray" : "exit";
        }

        static int BootIndex(string mode)
        {
            return WantTray(mode) ? 1 : 0;
        }

        /// <summary>
        /// 认证成功后要不要留在托盘后台继续跑。
        /// 兼容老配置: confirm(原来的"弹窗确认") 归到留在托盘, silent 归到自动退出。
        /// </summary>
        public static bool WantTray(string bootMode)
        {
            return string.Equals(bootMode, "tray", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bootMode, "confirm", StringComparison.OrdinalIgnoreCase);
        }

        static int ThemeIndex(string mode)
        {
            if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        /// <summary>切换主题并立即重绘整个界面。</summary>
        void ApplyTheme(string mode, bool save)
        {
            Color oldOk = Theme.Ok, oldErr = Theme.Err, oldDim = Theme.InkDim;
            Theme.ApplyMode(mode);

            if (lastStatusColor == oldOk) lastStatusColor = Theme.Ok;
            else if (lastStatusColor == oldErr) lastStatusColor = Theme.Err;
            else if (lastStatusColor == oldDim) lastStatusColor = Theme.InkDim;

            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Restyle(this, Theme.Bg);
            if (lblStatus != null) lblStatus.ForeColor = lastStatusColor;

            try { if (IsHandleCreated) Dwm.SetTitleBarDark(Handle, Theme.Dark); } catch { }
            Invalidate(true);

            if (save)
            {
                var c = Core.LoadCfg();
                c.Theme = mode;
                try { Core.SaveCfg(c); } catch { }
            }
        }

        /// <summary>按当前主题重新给整棵控件树刷色。</summary>
        void Restyle(Control parent, Color surface)
        {
            foreach (Control c in parent.Controls)
            {
                Color childSurface = surface;
                var ib = c as InputBox;
                var cp = c as CardPanel;

                if (ib != null)
                {
                    ib.BackColor = surface;
                    ib.Fill = Theme.FieldBg;
                    ib.Line = Theme.InputBorder;
                    if (ib.Inner != null) { ib.Inner.BackColor = Theme.FieldBg; ib.Inner.ForeColor = Theme.Ink; }
                }
                else if (cp != null)
                {
                    cp.BackColor = surface;
                    cp.Fill = Theme.Card;
                    cp.Line = Theme.CardBorder;
                    childSurface = Theme.Card;
                }
                else if (c is TextBox)
                {
                    c.BackColor = Theme.Card;
                    c.ForeColor = Theme.Ink;
                }
                else if (c is ComboBox || c is NumericUpDown || c is ListBox)
                {
                    c.BackColor = Theme.FieldBg;
                    c.ForeColor = Theme.Ink;
                }
                else
                {
                    c.BackColor = surface;
                    if (c is Label || c is CheckBox)
                        c.ForeColor = ("dim".Equals(c.Tag)) ? Theme.InkDim : Theme.Ink;
                }

                if (c.HasChildren) Restyle(c, childSurface);
                c.Invalidate();
            }
        }
        // ================================================================
        //  设置区分页: 有线 / 无线 / 热点
        //  高度按"当前页"算, 所以切到短的那页窗口会跟着缩
        // ================================================================
        TableLayoutPanel MakeGrid(int rows)
        {
            var g = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = rows,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            for (int i = 0; i < 4; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < rows; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return g;
        }

        /// <summary>往指定页里放一行, 左右各一组「标签 + 控件」, 任一侧传 null 就留空。</summary>
        void PutRow(TableLayoutPanel g, int row, string labelL, Control cL, string labelR, Control cR)
        {
            if (labelL != null)
            {
                g.Controls.Add(MakeLabel(labelL), 0, row);
                if (cL != null) { cL.Margin = new Padding(0, 0, 26, 6); g.Controls.Add(cL, 1, row); }
            }
            if (labelR != null)
            {
                g.Controls.Add(MakeLabel(labelR), 2, row);
                if (cR != null) { cR.Margin = new Padding(0, 0, 0, 6); g.Controls.Add(cR, 3, row); }
            }
        }

        Win11Button MkTab(string text, int idx)
        {
            var b = new Win11Button { Text = text, MinimumSize = new Size(88, 32), Padding = new Padding(8, 0, 8, 0) };
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += (s, e) => ShowTab(idx);
            return b;
        }

        /// <summary>显示/隐藏表格里的一整行: 行里的控件和行高一起改, 藏起来的行不留空位。</summary>
        static void ShowRow(TableLayoutPanel g, int row, bool show)
        {
            if (g == null) return;
            foreach (Control c in g.Controls)
                if (g.GetRow(c) == row) c.Visible = show;
            if (row < g.RowStyles.Count)
                g.RowStyles[row] = show ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, 0);
            g.PerformLayout();
        }

        /// <summary>切换页签。0=有线 1=无线 2=热点。</summary>
        void ShowTab(int idx)
        {
            tabIdx = idx;
            if (btnTabWired != null) { btnTabWired.Primary = (idx == 0); btnTabWired.Invalidate(); }
            if (btnTabWifi != null) { btnTabWifi.Primary = (idx == 1); btnTabWifi.Invalidate(); }
            if (btnTabHot != null) { btnTabHot.Primary = (idx == 2); btnTabHot.Invalidate(); }

            ShowRow(grid, 2, idx == 1);                 // WiFi 名称: 只有无线页
            ShowRow(grid, 3, idx == 2);                 // 热点名称/密码、频段、操作: 只有热点页
            ShowRow(grid, 4, idx == 2);
            ShowRow(grid, 5, idx == 2);

            // 页签本身就决定了连接方式(没有"自动"): 0=有线, 1=无线; 热点页不改这个值
            if (idx <= 1) modeIdx = idx;

            if (uiReady) AdaptSize();
        }

        /// <summary>配置里的连接方式 -> 页签索引(0=有线, 1=无线)。老配置的 auto 由 Core.ConcreteMode 收敛。</summary>
        static int GuessTab(string mode)
        {
            return Core.ConcreteMode(mode) == "wired" ? 0 : 1;
        }

        Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Fonts.UI,
                ForeColor = Theme.Ink,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 12, 6)
            };
        }

        FlowLayoutPanel Flow(Action<FlowLayoutPanel> fill)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            fill(p);
            return p;
        }

        /// <summary>扫描附近的 WiFi, 填进下拉框。</summary>
        void ScanWifiAsync()
        {
            if (btnScan == null || !btnScan.Enabled) return;
            btnScan.Enabled = false;
            btnScan.Text = "扫描中";
            Task.Run(() =>
            {
                var list = Core.ScanWifiNetworks();
                string cur = Core.CurrentWifiSsid();
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        string keep = cboSsid.Text;
                        cboSsid.Items.Clear();
                        foreach (var s in list) cboSsid.Items.Add(s);
                        if (keep.Length > 0) cboSsid.Text = keep;
                        else if (cur.Length > 0) cboSsid.Text = cur;

                        btnScan.Enabled = true;
                        btnScan.Text = "扫描";
                        if (list.Count == 0) AppendLog("没扫描到 WiFi（可能没有无线网卡，或无线服务未开启）");
                        else AppendLog("扫描到 " + list.Count + " 个 WiFi" + (cur.Length > 0 ? "，当前连接: " + cur : ""));
                    }));
                }
                catch { }
            });
        }

        /// <summary>按内容自适应初始窗口大小, 既不裁切也不超出屏幕。</summary>
        void AdaptSize()
        {
            try
            {
                // 标题栏不能用 header.PreferredSize —— 那两个下拉框是贴右边的, 标题栏有多宽它就报多宽,
                // 于是「需要多宽」永远等于「现在多宽」, 窗口再也瘦不回去。按内容算才对。
                int headerNeed = titleBox == null ? 0
                    : titleBox.PreferredSize.Width + headerSettings.Margin.Horizontal + headerSettings.PreferredSize.Width;

                int needW = Math.Max(pnlButtons.PreferredSize.Width,
                            Math.Max(pnlBottom.PreferredSize.Width,
                            Math.Max(headerNeed,
                            Math.Max(cardSettings.PreferredSize.Width, cardChecks.PreferredSize.Width)))) + 40;

                // 日志区固定预留 200, 其余各块按实际高度累加
                int fixedH = header.PreferredSize.Height + cardSettings.PreferredSize.Height
                           + cardChecks.PreferredSize.Height + pnlButtons.PreferredSize.Height
                           + lblStatus.PreferredSize.Height + pnlBottom.PreferredSize.Height
                           + tipLabel.PreferredSize.Height + root.Padding.Vertical + 88;   // 日志区最小高度

                // 宽高都按内容算, 不再只增不减
                ClientSize = new Size(Math.Max(ClientSize.Width, needW), fixedH);

                MinimumSize = new Size(Math.Max(560, needW - 80), Math.Min(fixedH, 560));

                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int w = Math.Min(ClientSize.Width, wa.Width - 24);
                int h = Math.Min(ClientSize.Height, wa.Height - 24);
                if (w != ClientSize.Width || h != ClientSize.Height) ClientSize = new Size(w, h);
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AdaptSize();
            try { Dwm.SetTitleBarDark(Handle, Theme.Dark); } catch { }
            ScanWifiAsync();
            LoadHotspotInfoAsync();
        }

        // ------------------------------------------------ 托盘
        // ------------------------------------------------ 托盘图标状态
        /// <summary>托盘图标底色: 蓝色 = 已连上校园网, 红色 = 未连上。</summary>
        void SetTrayState(bool online)
        {
            if (tray == null) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetTrayState(online))); } catch { }
                return;
            }
            try
            {
                tray.Icon = AppIcon.Get(online ? AppIcon.Blue : AppIcon.Red);
                tray.Text = online ? "校园网自动认证 · 已连上" : "校园网自动认证 · 未连上";
            }
            catch { }
        }

        /// <summary>后台探一次网络, 回主线程更新托盘图标。</summary>
        void RefreshTrayStateAsync()
        {
            Task.Run(() =>
            {
                bool on = false;
                try { on = Core.TestOnline(4); } catch { }
                SetTrayState(on);
            });
        }

        // ------------------------------------------------ 周期复检
        System.Windows.Forms.Timer trayTimer;

        /// <summary>
        /// 检查周期只在"留在托盘后台跑"的时候才有意义。
        /// 静默退出是开机认证一次就走, 没有周期这回事。所以这里是 0 就不启动。
        /// </summary>
        void StartTrayTimer()
        {
            try
            {
                if (trayTimer != null) { trayTimer.Stop(); trayTimer.Dispose(); trayTimer = null; }
                var c = Core.LoadCfg();
                int mins = c.IntervalMinutes;
                if (mins <= 0) return;

                trayTimer = new System.Windows.Forms.Timer();
                trayTimer.Interval = mins * 60000;
                trayTimer.Tick += (s, e) =>
                {
                    if (busy) return;
                    AppendLog("按周期(" + mins + " 分钟)自动复检");
                    DoAuthAsync(true);
                };
                trayTimer.Start();
                AppendLog("已开启周期复检: 每 " + mins + " 分钟一次");
            }
            catch { }
        }

        // ------------------------------------------------ 检查周期可用性
        /// <summary>静默退出没有周期检查, 把这一项直接禁掉。</summary>
        void UpdateIntervalEnabled()
        {
            bool trayMode = (SelectedBootMode() == "tray");
            if (numInterval != null)
            {
                numInterval.Enabled = trayMode;
                if (!trayMode) numInterval.Value = 0;
            }
            if (lblHint != null)
                lblHint.Text = trayMode
                    ? "分钟（0 = 只认证一次，不复检）"
                    : "分钟（认证成功即退出，无需它）";
        }

        void BuildTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = AppIcon.Get(AppIcon.Red);   // 还没探测, 先按"未连上"显示红色
                tray.Text = "校园网自动认证 · 未连上";
                tray.Visible = true;

                var menu = new ContextMenuStrip();
                menu.Items.Add("显示窗口", null, (s, e) => RestoreWindow());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("立即认证", null, (s, e) => { RestoreWindow(); DoAuthAsync(false); });
                menu.Items.Add("检测状态", null, (s, e) => { RestoreWindow(); DoProbeAsync(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("开热点", null, (s, e) => DoHotspot("on"));
                menu.Items.Add("关热点", null, (s, e) => DoHotspot("off"));
                menu.Items.Add("热点状态", null, (s, e) => { RestoreWindow(); DoHotspot("status"); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出", null, (s, e) => ExitApp());
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (s, e) => RestoreWindow();
            }
            catch { }
        }

        void RestoreWindow()
        {
            try
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
            }
            catch { }
        }

        void ExitApp()
        {
            reallyExit = true;
            try { if (tray != null) tray.Visible = false; } catch { }
            Application.Exit();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (StartHidden)
            {
                StartHidden = false;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);   // 建好句柄但不显示, 托盘图标才有得用
                // 开机认证成功才会走到这里: 起周期复检, 再告诉用户一声
                AppendLog("认证成功, 已收束到托盘");
                RefreshTrayStateAsync();
                StartTrayTimer();
                try
                {
                    if (tray != null)
                        tray.ShowBalloonTip(2500, "校园网自动认证",
                            "开机认证成功，已收束到托盘后台运行。\n右键托盘图标可操作，选「退出」才真正关闭。",
                            ToolTipIcon.Info);
                }
                catch { }
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 点右上角 X 干什么, 跟着「认证成功后」那个下拉走:
            //   留在托盘监测   -> 最小化到托盘, 后台继续跑(老行为)
            //   自动退出程序   -> 直接退出 —— 这个模式下检查周期本来就是灰的, 没有后台可言
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                if (WantTray(SelectedBootMode()))
                {
                    e.Cancel = true;
                    Hide();
                    if (!trayTipShown && tray != null)
                    {
                        trayTipShown = true;
                        try
                        {
                            tray.ShowBalloonTip(2500, "校园网自动认证",
                                "已最小化到托盘，后台自动认证继续运行。\n右键托盘图标可操作，选「退出」才真正关闭。",
                                ToolTipIcon.Info);
                        }
                        catch { }
                    }
                    return;
                }
                reallyExit = true;
                try { if (tray != null) tray.Visible = false; } catch { }
            }
            base.OnFormClosing(e);
        }

        Button MkBtn(string text, int x, int y, int w)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 32),
                FlatStyle = FlatStyle.System,
                Font = FontUI
            };
        }

        void LoadToUi()
        {
            var c = Core.LoadCfg();
            txtUser.Text = c.UserId;
            txtPass.Text = c.Password;
            numInterval.Value = Math.Max(0, Math.Min(1440, c.IntervalMinutes));
            numLogKeep.Value = Math.Max(0, Math.Min(720, c.LogKeepHours));
            chkAutoHotspot.Checked = c.AutoHotspot;
            chkAutostart.Checked = Core.AutostartEnabled();

            // 「不自动弹校园网登录页」= 想让探测关掉。
            // 以系统实际状态为准(注册表里可能已经被别的地方改过), 配置只是记个意愿。
            chkQuietPortal.Checked = !Core.ProbeEnabled();

            ShowTab(GuessTab(c.Mode));   // 有线/无线/热点 页签(顺带定下连接方式)
            cboBand.SelectedIndex = BandIndex(c.HotspotBand);
            cboTheme.SelectedIndex = ThemeIndex(c.Theme);
            cboBoot.SelectedIndex = BootIndex(c.BootMode);
            UpdateIntervalEnabled();
            txtHotspotSsid.Text = curHotspotSsid;
            txtHotspotPass.Text = curHotspotPass;
            cboSsid.Text = c.WifiSsid == null ? "" : c.WifiSsid;
        }

        /// <summary>界面频段值(auto/2.4/5) 和系统值(Auto/TwoPointFourGigahertz/FiveGigahertz) 是否不同。</summary>
        static bool BandDiffers(string cfgBand, string sysBand)
        {
            string want = string.IsNullOrEmpty(cfgBand) ? "auto" : cfgBand.Trim().ToLowerInvariant();
            string have = (sysBand ?? "").Trim().ToLowerInvariant();
            if (want == "2.4" || want == "2.4g") return have != "twopointfourgigahertz";
            if (want == "5" || want == "5g") return have != "fivegigahertz";
            return (have.Length > 0 && have != "auto");   // 界面选的是"自动"
        }

        /// <summary>系统频段值 -> 下拉索引(0=自动,1=2.4G,2=5G); 认不出返回 -1。</summary>
        static int BandIndexOfSystem(string sysBand)
        {
            string s = (sysBand ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0 || s == "auto") return 0;
            if (s == "twopointfourgigahertz") return 1;
            if (s == "fivegigahertz") return 2;
            return -1;
        }

        static int BandIndex(string band)
        {
            if (band == "2.4" || band == "2.4g") return 1;
            if (band == "5" || band == "5g") return 2;
            return 0;
        }

        string SelectedMode()
        {
            // 连接方式就是页签: 0=有线, 1=无线; 永远不会返回"auto"
            return modeIdx == 1 ? "wifi" : "wired";
        }

        string SelectedBand()
        {
            if (cboBand.SelectedIndex == 1) return "2.4";
            if (cboBand.SelectedIndex == 2) return "5";
            return "auto";
        }

        void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
            lastStatusColor = color;
            lblStatus.Text = "●  " + text;
            lblStatus.ForeColor = color;
        }

        void AppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(line))); return; }
            txtLog.AppendText(line + Environment.NewLine);
        }

        void SetBusy(bool b)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetBusy(b))); return; }
            busy = b;
            btnSave.Enabled = !b;
            btnAuth.Enabled = !b;
            btnProbe.Enabled = !b;
            btnHotOn.Enabled = !b;
            btnHotOff.Enabled = !b;
            btnHotStatus.Enabled = !b;
            btnInstall.Enabled = !b;
            btnUninstall.Enabled = !b;
            btnRepair.Enabled = !b;
            Cursor = b ? Cursors.WaitCursor : Cursors.Default;
        }

        void DoSave()
        {
            var c = Core.LoadCfg();
            c.UserId = txtUser.Text.Trim();
            c.Password = txtPass.Text;
            c.IntervalMinutes = (int)numInterval.Value;
            c.LogKeepHours = (int)numLogKeep.Value;
            c.AutoHotspot = chkAutoHotspot.Checked;
            c.QuiethPortal = chkQuietPortal.Checked;
            c.Mode = SelectedMode();
            c.WifiSsid = cboSsid.Text.Trim();
            c.HotspotBand = SelectedBand();
            c.Theme = SelectedTheme();
            c.BootMode = SelectedBootMode();
            // 认证成功后就退出 = 只认证这一次, 没有周期这回事
            if (!WantTray(c.BootMode)) c.IntervalMinutes = 0;
            if (c.Mode == "wifi" && c.WifiSsid.Length == 0)
            {
                MessageBox.Show("选择了「仅无线」时，请填写要连接的 WiFi 名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (c.UserId.Length == 0) { MessageBox.Show("请填写学号", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (c.Password.Length == 0) { MessageBox.Show("请填写密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try { Core.SaveCfg(c); }
            catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            AppendLog("配置已保存: " + Core.ConfPath);
            if (c.LogKeepHours > 0)
            {
                int dropped = Core.PruneLog(c.LogKeepHours);
                AppendLog(dropped > 0
                    ? ("已清理日志: 删掉 " + dropped + " 行超过 " + c.LogKeepHours + " 小时的记录")
                    : ("日志已按「保留 " + c.LogKeepHours + " 小时」检查过，没有要删的"));
            }

            if (chkAutostart.Checked)
            {
                string msg;
                bool ok = Core.EnableAutostart(Core.ExePath(), c.AutoHotspot, out msg);
                AppendLog(ok ? ("开机自启动已启用（启动文件夹快捷方式）: " + msg) : ("自启设置失败: " + msg));
                SetStatus(ok ? "已保存，开机自动认证已启用" : "已保存，但自启设置失败", ok ? Theme.Ok : Theme.Err);
            }
            else
            {
                Core.DisableAutostart();
                AppendLog("开机自启动已关闭");
                SetStatus("已保存，未启用开机自启动", Theme.InkDim);
            }

            StartTrayTimer();   // 周期变了就重设定时器

            // 「不自动弹校园网登录页」: 这个开关改的是 Windows 的强制门户探测(在 HKLM 下),
            // 要有管理员权限。这里写不成不报错卡住, 而是告诉用户怎么以管理员身份再点一次。
            ApplyQuietPortal(chkQuietPortal.Checked, false);

            // 热点设置: 改热点会重启热点并把已连设备踢下线, 所以不能凭缓存值猜,
            // 而是在保存时现读系统当前配置, 逐项比对, 只有真的不同才写。
            // (之前写的 (c.HotspotBand != "auto") 不是"变了", 而是"只要不是自动就每次重设")
            string ns = txtHotspotSsid.Text.Trim();
            string np = txtHotspotPass.Text;
            bool nameProvided = (ns.Length > 0 && np.Length > 0);
            if (nameProvided && np.Length < 8)
            {
                MessageBox.Show("热点密码至少 8 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameProvided = false;
            }
            string wantBand = c.HotspotBand;
            Task.Run(() =>
            {
                string sysSsid, sysPass, sysBand, sysState;
                int sysClients;
                if (!Core.GetHotspotInfo(out sysSsid, out sysPass, out sysBand, out sysState, out sysClients))
                {
                    AppendLog("读不到系统热点配置, 本次不改动热点(避免误重置)");
                    return;
                }
                AppendLog("系统当前热点: " + sysSsid + "   频段 " + sysBand);

                bool wantName = nameProvided && (ns != sysSsid || np != sysPass);
                bool wantBand2 = BandDiffers(wantBand, sysBand);

                if (!wantName && !wantBand2)
                {
                    AppendLog("热点设置与系统一致, 未改动");
                    return;
                }
                if (wantName)
                {
                    string m2 = "";
                    bool ok2 = Core.SetHotspotConfig(ns, np, out m2);
                    if (ok2)
                    {
                        curHotspotSsid = ns;
                        curHotspotPass = np;
                        AppendLog("热点名称/密码已更新");
                    }
                    else
                    {
                        AppendLog("热点名称/密码更新失败: " + m2.Replace("\r\n", " | ").Replace("\n", " | "));
                    }
                }
                if (wantBand2)
                {
                    string o3 = Core.RunHotspot("band -Band " + wantBand);
                    foreach (var ln in o3.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        AppendLog(ln);
                }
            });
        }

        // ------------------------------------------------ 修复热点
        /// <summary>后台读出当前热点的名称/密码, 填进输入框。</summary>
        void LoadHotspotInfoAsync()
        {
            Task.Run(() =>
            {
                string ssid, pass, band, state;
                int clients;
                bool ok = Core.GetHotspotInfo(out ssid, out pass, out band, out state, out clients);
                if (!ok)
                {
                    AppendLog("读不到热点配置（可能没有无线网卡，或无线服务未开启）");
                    return;
                }
                curHotspotSsid = ssid;
                curHotspotPass = pass;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        txtHotspotSsid.Text = ssid;
                        txtHotspotPass.Text = pass;
                        // 频段下拉也按系统实际值回填: 否则界面显示的和系统不一致,
                        // 一保存就会把系统频段"纠正"回去, 用户会以为被重置了
                        int bi = BandIndexOfSystem(band);
                        if (bi >= 0) cboBand.SelectedIndex = bi;
                    }));
                }
                catch { }
            });
        }

        /// <summary>
        /// 应用「不自动弹校园网登录页」这个开关。
        ///
        /// 它改的是 Windows 的强制门户探测(注册表 HKLM 下的 EnableActiveProbing):
        ///   勾上 → 关掉探测, 系统不再自动给你开登录页;
        ///   取消 → 恢复系统默认。
        /// **没管理员权限是改不了的**, 这时不硬来, 而是问要不要以管理员身份重开一次。
        /// </summary>
        void ApplyQuietPortal(bool wantQuiet, bool startup)
        {
            bool want = !wantQuiet;                 // 想把探测关掉 = 不希望探测开着
            bool now = Core.ProbeEnabled();
            if (now == want)
            {
                if (!startup) AppendLog(Core.ProbeInfo());
                return;                              // 已经是想要的状态, 不动
            }

            string msg;
            bool ok = Core.SetProbe(want, out msg);
            if (ok)
            {
                AppendLog("强制门户探测: " + msg + "（断开重连一次 WiFi 或重启后完全生效）");
                SetStatus(wantQuiet ? "已关掉自动弹登录页" : "已恢复系统默认探测", Theme.Ok);
                return;
            }

            AppendLog("强制门户探测开关没改成: " + msg);
            if (!startup)
            {
                var r = MessageBox.Show(
                    "这个开关要改 Windows 的系统设置，需要管理员权限。\n\n" +
                    "要以管理员身份重新打开本程序吗？\n" +
                    "（重新打开后再点一次「保存并应用」就生效）",
                    "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r == DialogResult.Yes) RelaunchAsAdmin();
            }
            else
            {
                SetStatus("开关需要管理员权限，点「保存并应用」可以重开一次", Theme.Err);
            }
        }

        /// <summary>以管理员身份重新启动本程序（会弹 UAC）。</summary>
        void RelaunchAsAdmin()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Core.ExePath())
                {
                    UseShellExecute = true,
                    Verb = "runas",                  // 关键: 触发 UAC 提权
                    WorkingDirectory = Core.CurrentExeDir()
                };
                System.Diagnostics.Process.Start(psi);
                ExitApp();                            // 新进程起来了, 这个就退出
            }
            catch (Exception ex)
            {
                // 用户在 UAC 上点了"否", 会走到这里
                AppendLog("没有提权: " + ex.Message);
                MessageBox.Show("没有以管理员身份打开，这个开关就没法改。\n" +
                                "想改的话：右键程序图标 →「以管理员身份运行」，再点「保存并应用」。",
                                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void DoRepairHotspot()
        {            if (busy) return;
            SetBusy(true);
            SetStatus("正在修复热点…", Theme.InkDim);
            string band = SelectedBand();
            Task.Run(() =>
            {
                try
                {
                    string r = Core.RunHotspot("repair -Band " + band);
                    foreach (var ln in r.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        AppendLog(ln);
                    SetStatus("热点已重置", Theme.Ok);
                }
                catch (Exception ex)
                {
                    AppendLog("修复热点失败: " + ex.Message);
                    SetStatus("修复热点失败", Theme.Err);
                }
                finally { SetBusy(false); }
            });
        }

        void DoProbeAsync()
        {
            if (busy) return;
            SetBusy(true);
            SetStatus("正在检测…", Theme.InkDim);
            Task.Run(() =>
            {
                try
                {
                    bool on = Core.TestOnline();
                    AppendLog(on ? "检测结果: 已联网" : "检测结果: 未联网(需要认证)");
                    SetStatus(on ? "已联网" : "未联网，需要认证", on ? Theme.Ok : Theme.Err);
                    SetTrayState(on);
                }
                catch (Exception ex) { AppendLog("检测失败: " + ex.Message); SetStatus("检测失败", Theme.Err); }
                finally { SetBusy(false); }
            });
        }

        void DoAuthAsync(bool force)
        {
            if (busy) return;
            var c = Core.LoadCfg();
            c.UserId = txtUser.Text.Trim();
            c.Password = txtPass.Text;
            if (c.UserId.Length == 0 || c.Password.Length == 0)
            {
                MessageBox.Show("请先填写学号与密码，并点击「保存并应用」", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetBusy(true);
            SetStatus("正在认证…", Theme.InkDim);
            Task.Run(() =>
            {
                try
                {
                    AppendLog("--------------------------------------------------");
                    var o = Core.DoAuth(c, force);
                    if (o.Ok && o.AlreadyOnline) SetStatus("已联网，无需认证", Theme.Ok);
                    else if (o.Ok) SetStatus("认证成功", Theme.Ok);
                    else SetStatus(o.Message, Theme.Err);
                    SetTrayState(o.Ok);

                    // 选了「认证成功后自动退出程序」的: 手动点「立即认证」成功也一样收尾退出,
                    // 不留后台(这个模式下检查周期本来就是灰的)。留 0.9 秒让用户看见结果。
                    if (o.Ok && !WantTray(SelectedBootMode()))
                    {
                        AppendLog("按「认证成功后自动退出程序」收尾: 稍后自动关闭");
                        Task.Delay(900).ContinueWith(t2 =>
                        {
                            try { BeginInvoke(new Action(ExitApp)); } catch { }
                        });
                    }
                }
                catch (Exception ex) { AppendLog("认证异常: " + ex.Message); SetStatus("认证异常", Theme.Err); }
                finally { SetBusy(false); }
            });
        }

        void DoHotspot(string action)
        {
            if (busy) return;
            SetBusy(true);
            SetStatus("正在操作热点…", Theme.InkDim);
            Task.Run(() =>
            {
                try
                {
                    string r = Core.RunHotspot(action);
                    foreach (var ln in r.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        AppendLog(ln);
                    if (action == "on") SetStatus("热点已开启", Theme.Ok);
                    else if (action == "off") SetStatus("热点已关闭", Theme.InkDim);
                    else SetStatus("热点状态已读取", Theme.InkDim);
                }
                catch (Exception ex) { AppendLog("热点操作失败: " + ex.Message); SetStatus("热点操作失败", Theme.Err); }
                finally { SetBusy(false); }
            });
        }

        // ------------------------------------------------ 安装到其他目录
        void DoInstallTo()
        {
            string oldInstalled = Core.GetInstalledDir();
            string curDir = Core.CurrentExeDir();

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择安装目录（程序会被复制到这里，并创建开始菜单/桌面快捷方式）";
                dlg.ShowNewFolderButton = true;
                try
                {
                    string def = Core.DefaultInstallDir();
                    dlg.SelectedPath = Directory.Exists(def) ? def : Path.GetDirectoryName(def);
                }
                catch { }

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string target = dlg.SelectedPath;
                if (string.Equals(target.TrimEnd('\\'), curDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("这个目录就是程序当前所在的目录，不用再安装一次。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (MessageBox.Show(
                        "将把程序安装到：\n" + target + "\n\n" +
                        "会同时创建开始菜单和桌面快捷方式，\n" +
                        "之后可以在「设置 → 应用」里卸载。\n\n" +
                        "是否继续？",
                        "安装确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                bool wantTask = chkAutostart.Checked || Core.AutostartEnabled();
                var r = Core.InstallTo(target, true, wantTask);
                if (!r.Ok)
                {
                    MessageBox.Show("安装失败：" + r.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                AppendLog("已安装到: " + r.Target);
                if (wantTask) AppendLog("开机自启动任务已指向新位置");
                SetStatus("安装完成", Theme.Ok);

                // 清理旧的安装目录
                if (!string.IsNullOrEmpty(oldInstalled)
                    && !string.Equals(oldInstalled.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (MessageBox.Show(
                            "检测到旧安装目录：\n" + oldInstalled + "\n\n是否删除？",
                            "清理旧目录", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        try { Directory.Delete(oldInstalled, true); AppendLog("已删除旧目录: " + oldInstalled); }
                        catch { AppendLog("旧目录正被占用，稍后可手动删除: " + oldInstalled); }
                    }
                }

                if (MessageBox.Show(
                        "安装完成。\n\n是否立即从新位置启动，并删除当前位置的程序文件？",
                        "安装完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try { Process.Start(r.Target); } catch { }
                    Core.ScheduleSelfDelete(Core.CurrentExe(), null);
                    Application.Exit();
                }
            }
        }

        // ------------------------------------------------ 卸载
        void DoUninstall()
        {
            bool installed = Core.IsInstalled();

            if (MessageBox.Show(
                    "确定要卸载「校园网自动认证」吗？\n\n" +
                    "将删除：\n" +
                    "  · 开机自启动任务\n" +
                    "  · 开始菜单 / 桌面快捷方式\n" +
                    (installed ? "  · 程序文件\n" : "") +
                    "\n是否继续？",
                    "卸载 校园网自动认证", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            var keep = MessageBox.Show(
                "是否同时删除配置和运行日志？\n\n" +
                "是 = 一起删除（下次装回来要重新输学号密码）\n" +
                "否 = 保留在 " + Core.Dir,
                "配置如何处理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            string msg = Core.Uninstall(keep == DialogResult.Yes, true);
            AppendLog(msg);
            MessageBox.Show("卸载完成。\n\n" + msg, "校园网自动认证", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
        }

        // ------------------------------------------------ 关于
        /// <summary>
        /// 关于对话框。左边放作者头像, 右边是版本/环境/原理。
        /// 头像是编译时嵌进 EXE 的资源(.build\avatar.png, 由 生成头像资源.ps1 生成),
        /// 不依赖任何外部文件 —— 程序只有一个 EXE 这个约定不能破。
        /// </summary>
        void ShowAbout()
        {
            string where = Core.IsInstalled() ? ("已安装到: " + Core.CurrentExeDir()) : ("便携运行: " + Core.CurrentExeDir());
            string text =
                "校园网自动认证  v" + Core.AppVersion + "\n" +
                "四川农业大学 portal.sicau.edu.cn\n\n" +
                "作者: " + Core.AuthorName + "\n" +
                "邮箱: " + Core.AuthorMail + "\n\n" +
                where + "\n" +
                "配置目录: " + Core.Dir + "\n" +
                "开机自启: " + (Core.AutostartEnabled() ? "已启用" : "未启用") + "\n\n" +
                "原理: 未认证时网关会 302 跳到 portal.do?wlanuserip=...&wlanacname=...&nasip=...，\n" +
                "页面里的隐藏表单明文 POST 到 /webauth.do 即完成认证。\n" +
                "因校园网 IPv6 不通，程序强制走 IPv4。";

            using (var f = new Form())
            {
                f.Text = "关于 校园网自动认证";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.BackColor = Theme.Card;
                f.Font = Fonts.UI;
                f.AutoScaleMode = AutoScaleMode.Dpi;

                // 先用文字量出内容尺寸, 别让路径那种长内容把标签挤成一条竖条
                Size bodySize = TextRenderer.MeasureText(text, Fonts.UI, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                var pic = new PictureBox
                {
                    Size = new Size(96, 96),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Theme.Card,
                    Image = LoadAvatarImage(),
                    Location = new Point(22, 26)
                };
                // 头像下面写名字, 像名片一样
                var name = new Label
                {
                    Text = Core.AuthorName,
                    AutoSize = true,
                    Font = Fonts.UIBold,
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Card,
                    Location = new Point(22, 22 + 96 + 10)
                };

                var body = new Label
                {
                    Text = text,
                    AutoSize = true,
                    MaximumSize = new Size(bodySize.Width + 8, 0),
                    Font = Fonts.UI,
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Card,
                    Location = new Point(142, 24)
                };

                var ok = new Win11Button
                {
                    Text = "好",
                    Primary = true,
                    MinimumSize = new Size(90, 32),
                    DialogResult = DialogResult.OK
                };
                ok.Click += (s, e) => f.Close();

                f.Controls.Add(pic);
                f.Controls.Add(name);
                f.Controls.Add(body);
                f.Controls.Add(ok);
                f.AcceptButton = ok;

                // 尺寸按内容算: 左边头像列固定 142, 右边文字按量出来的宽度
                int wantW = 142 + bodySize.Width + 30;
                int wantH = Math.Max(24 + bodySize.Height, 22 + 96 + 10 + name.PreferredHeight) + 64;
                f.ClientSize = new Size(Math.Max(wantW, 360), Math.Max(wantH, 240));
                ok.Location = new Point(f.ClientSize.Width - ok.Width - 20, f.ClientSize.Height - ok.Height - 18);

                f.ShowDialog(this);
            }
        }

        /// <summary>把嵌在 EXE 里的 avatar.png 读成 Image; 读不到就返回 null(对话框照样能开)。</summary>
        Image LoadAvatarImage()
        {
            try
            {
                byte[] b = Core.LoadEmbeddedBytes(Core.AvatarResource);
                if (b == null || b.Length == 0) return null;
                var ms = new MemoryStream(b);
                return Image.FromStream(ms);     // ms 不能 Dispose: GDI+ 要用它
            }
            catch { return null; }
        }
    }

    /// <summary>首次运行向导: 自动检测环境 + 引导填账号。排版同样用 AutoSize, 不会挤。</summary>
    public class FirstRunForm : Form
    {
        InputBox txtU, txtP;
        Label lblDetect;
        CardPanel cardInfo, cardDetect;
        TableLayoutPanel root;
        FlowLayoutPanel pnlBtns;

        public FirstRunForm()
        {
            Text = "首次使用设置";
            Icon = AppIcon.Get(AppIcon.Green);
            AutoScaleMode = AutoScaleMode.None;
            Font = Fonts.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(26, 22, 26, 20),
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- 标题 ----
            var head = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 16) };
            var t = new Label { Text = "欢迎使用 校园网自动认证", Font = Fonts.Title, ForeColor = Theme.Ink, BackColor = Theme.Bg, AutoSize = true, Location = new Point(0, 0) };
            var s = new Label
            {
                Text = "填好学号和密码，点「开始使用」即可。\n之后每次开机都会自动认证，不用再手动登录。",
                ForeColor = Theme.InkDim,
                BackColor = Theme.Bg,
                AutoSize = true,
                Location = new Point(3, 40)
            };
            head.Controls.Add(t);
            head.Controls.Add(s);
            root.Controls.Add(head, 0, 0);

            // ---- 账号卡片 ----
            cardInfo = new CardPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 10, 14, 4),
                Margin = new Padding(0, 0, 0, 6)
            };
            var g = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Card,
                Margin = new Padding(0)
            };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 2; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            txtU = new InputBox(300);
            txtP = new InputBox(300);
            txtP.Inner.UseSystemPasswordChar = true;

            var l1 = new Label { Text = "学号", AutoSize = true, Anchor = AnchorStyles.Left, Font = Fonts.UI, ForeColor = Theme.Ink, BackColor = Theme.Card, Margin = new Padding(0, 0, 16, 10) };
            var l2 = new Label { Text = "密码", AutoSize = true, Anchor = AnchorStyles.Left, Font = Fonts.UI, ForeColor = Theme.Ink, BackColor = Theme.Card, Margin = new Padding(0, 0, 16, 10) };
            g.Controls.Add(l1, 0, 0);
            g.Controls.Add(txtU, 1, 0);
            g.Controls.Add(l2, 0, 1);
            g.Controls.Add(txtP, 1, 1);
            cardInfo.Controls.Add(g);
            root.Controls.Add(cardInfo, 0, 1);

            // ---- 环境检测卡片 ----
            cardDetect = new CardPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 14, 20, 14),
                Margin = new Padding(0, 0, 0, 16)
            };
            var dt = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Card, Margin = new Padding(0) };
            dt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var dtTitle = new Label { Text = "环境检测", Font = Fonts.UIBold, ForeColor = Theme.Ink, BackColor = Theme.Card, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            lblDetect = new Label { Text = "正在检测…", ForeColor = Theme.InkDim, BackColor = Theme.Card, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0) };
            dt.Controls.Add(dtTitle, 0, 0);
            dt.Controls.Add(lblDetect, 0, 1);
            cardDetect.Controls.Add(dt);
            root.Controls.Add(cardDetect, 0, 2);

            // ---- 按钮 ----
            var btnOk = new Win11Button { Text = "开始使用", Primary = true, MinimumSize = new Size(120, 36) };
            var btnLater = new Win11Button { Text = "稍后手动设置", MinimumSize = new Size(130, 36) };
            btnOk.Click += (a, b) => OkClick();
            btnLater.Click += (a, b) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlBtns = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Theme.Bg,
                Margin = new Padding(0)
            };
            pnlBtns.Controls.Add(btnOk);
            pnlBtns.Controls.Add(btnLater);
            root.Controls.Add(pnlBtns, 0, 3);

            Controls.Add(root);
            Shown += (a, b) =>
            {
                Dwm.RoundCorners(Handle);
                Dwm.SetTitleBarDark(Handle, Theme.Dark);
                Task.Run(() => Detect());
            };
        }

        void Detect()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                bool hasWifi = Core.HasWifiAdapter();
                string wired = Core.InterfaceIPv4(false);
                string wifi = Core.InterfaceIPv4(true);
                string curWifi = Core.CurrentWifiSsid();
                bool online = Core.TestOnline();

                sb.AppendLine("无线网卡 ： " + (hasWifi ? "已检测到，可用热点功能" : "未检测到，热点功能不可用（认证不受影响）"));
                // InterfaceIPv4 找不到网卡时返回的是 null, 直接 .Length 会空引用 -> 向导里显示"检测失败"
                if (curWifi.Length > 0)
                    sb.AppendLine("无线连接 ： " + curWifi + (string.IsNullOrEmpty(wifi) ? "" : "   (" + wifi + ")"));
                else
                    sb.AppendLine("无线连接 ： " + (string.IsNullOrEmpty(wifi) ? "未连接" : wifi));
                sb.AppendLine("有线连接 ： " + (string.IsNullOrEmpty(wired) ? "未连接" : wired));
                sb.AppendLine("网络状态 ： " + (online ? "已联通（当前无需认证）" : "未认证 / 不通（属于正常）"));
            }
            catch (Exception ex) { sb.AppendLine("检测失败: " + ex.Message); }

            string text = sb.ToString().TrimEnd();
            try { BeginInvoke(new Action(() => { lblDetect.Text = text; })); } catch { }
        }

        void OkClick()
        {
            string u = txtU.Text.Trim();
            string p = txtP.Text;
            if (u.Length == 0) { MessageBox.Show("请填写学号", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtU.Inner.Focus(); return; }
            if (p.Length == 0) { MessageBox.Show("请填写密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); txtP.Inner.Focus(); return; }

            var c = Core.LoadCfg();
            c.UserId = u;
            c.Password = p;
            try { Core.SaveCfg(c); }
            catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            string msg;
            Core.EnableAutostart(Core.ExePath(), c.AutoHotspot, out msg);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
