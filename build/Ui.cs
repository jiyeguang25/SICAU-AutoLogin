using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sicau
{
    // ================================================================
    //  主题: 以 DSH 的 Catppuccin 玻璃主题为蓝本
    //  浅色 = Latte, 深色 = Mocha; 卡片圆角 14, 输入框 8, 按钮 10
    //  颜色里带 alpha 的一律用 FromArgb(a, r, g, b), 让底下的渐变透上来
    // ================================================================
    public static class Theme
    {
        // ---- 形状 ----
        public const int RCard  = 14;
        public const int RField = 8;
        public const int RBtn   = 10;

        // ---- DPI: 布局里写的是 96 DPI 下的数, 用之前一律过 Px() ----
        //  这是「字被挤占」的根因: 以前把 72px 这种写死的宽度直接用在
        //  125% 缩放的屏幕上, 文字按 125% 变大而格子没变, 于是折行/被切。
        public static float Scale = 1f;

        [DllImport("user32.dll")]
        static extern int GetDpiForSystem();

        /// <summary>
        /// 开机时调一次: 把系统 DPI 记进 Scale。
        /// 布局里所有尺寸都是 96 DPI 的数字, 乘上 Scale 才是真实像素 ——
        /// 不乘就会出现「字按 125% 放大、格子还是原来那么窄」, 于是文字被挤占/折行/切掉。
        /// </summary>
        public static void InitScale()
        {
            float s = 1f;
            try
            {
                int dpi = GetDpiForSystem();
                if (dpi > 0) s = dpi / 96f;
            }
            catch { }
            if (s <= 0f || s > 4f) s = 1f;
            Scale = s;
        }

        public static int Px(int v) { return (int)Math.Round(v * Scale); }

        public static Padding Pad(int all)
        {
            int p = Px(all); return new Padding(p, p, p, p);
        }

        public static Padding Pad(int l, int t, int r, int b)
        {
            return new Padding(Px(l), Px(t), Px(r), Px(b));
        }

        // ---- 当前配色(由 Apply 填) ----
        public static Color Bg          = Color.FromArgb(0xF3, 0xF3, 0xF3);
        public static Color BgTop       = Color.FromArgb(0xF7, 0xF8, 0xFB);   // 窗口渐变的上端
        public static Color Card        = Color.FromArgb(0xFF, 0xFF, 0xFF);
        // TextBox / ComboBox 这类原生控件**不接受带 alpha 的背景色**
        // （set_BackColor 会抛「控件不支持透明的背景色」），所以卡片要给它们
        // 另一个不透明的同色版本：CardSolid = Card 压在自己的底色上算出来的实色。
        public static Color CardSolid   = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color CardBorder  = Color.FromArgb(0xE8, 0xE8, 0xE8);
        public static Color InputBorder = Color.FromArgb(0xC9, 0xCF, 0xDA);
        public static Color FieldBg     = Color.FromArgb(0xF4, 0xF6, 0xFA);   // 亮色下卡片是 #FEFFFF, 输入框再白就分不出边界了
        public static Color Accent      = Color.FromArgb(0x1E, 0x66, 0xF5);
        public static Color AccentHover = Color.FromArgb(0x3B, 0x7B, 0xF6);
        public static Color AccentDown  = Color.FromArgb(0x1A, 0x57, 0xD0);
        public static Color AccentInk   = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color AccentSoft  = Color.FromArgb(30, 0x1E, 0x66, 0xF5);
        public static Color Ink         = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public static Color InkDim      = Color.FromArgb(0x61, 0x61, 0x61);
        public static Color InkFaint    = Color.FromArgb(0x8A, 0x8A, 0x8A);
        public static Color Hover       = Color.FromArgb(0xF7, 0xF7, 0xF7);
        public static Color Down        = Color.FromArgb(0xEE, 0xEE, 0xEE);
        public static Color BtnBg       = Color.FromArgb(0xFF, 0xFF, 0xFF);
        // 禁用态底色：必须是**不透明实色**。以前禁用是用 Color.FromArgb(120, fill) 半透明叠在
        // 卡片上，GDI 画文字时看到的是混合过的底，ClearType 会把笔画糊出彩边。
        public static Color Disabled    = Color.FromArgb(0xEC, 0xEE, 0xF3);
        public static Color DisabledOn  = Color.FromArgb(0x83, 0xAB, 0xFA);
        public static Color Ok          = Color.FromArgb(0x0F, 0x7B, 0x0F);
        public static Color Err         = Color.FromArgb(0xC4, 0x2B, 0x1C);
        public static Color Warn        = Color.FromArgb(0xB8, 0x86, 0x00);
        public static Color Highlight   = Color.FromArgb(20, 0xFF, 0xFF, 0xFF);   // 卡片顶部高光
        public static Color Glow        = Color.FromArgb(26, 0x1E, 0x66, 0xF5);   // 窗口左上柔光

        public static bool Dark = false;

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                // ---- Catppuccin Latte ----
                Bg          = Color.FromArgb(0xEF, 0xF1, 0xF5);   // base
                BgTop       = Color.FromArgb(0xF7, 0xF8, 0xFB);
                Card        = Color.FromArgb(0xFC, 0xFF, 0xFF, 0xFF);
                CardSolid   = Color.FromArgb(0xFE, 0xFF, 0xFF);
                // 边框 alpha 2026-10-29 提高一档：26/42 折算只有 1.17:1 / 1.29:1，
                // 亮色下等于看不见。40/65 约 2.0:1，不是去凑 3:1（那会画成深灰很脏），
                // 只要「看得出这块能填」。
                CardBorder  = Color.FromArgb(40, 0x4C, 0x4F, 0x69);
                InputBorder = Color.FromArgb(65, 0x4C, 0x4F, 0x69);
                FieldBg     = Color.FromArgb(0xF4, 0xF6, 0xFA);   // 亮色下卡片是 #FEFFFF, 输入框再白就分不出边界了
                Accent      = Color.FromArgb(0x1E, 0x66, 0xF5);   // blue
                AccentHover = Color.FromArgb(0x3B, 0x7B, 0xF6);
                AccentDown  = Color.FromArgb(0x1A, 0x57, 0xD0);
                AccentInk   = Color.FromArgb(0xFF, 0xFF, 0xFF);
                AccentSoft  = Color.FromArgb(26, 0x1E, 0x66, 0xF5);
                Ink         = Color.FromArgb(0x4C, 0x4F, 0x69);   // text
                InkDim      = Color.FromArgb(0x62, 0x65, 0x7C);   // subtext (4.37→5.06:1)
                InkFaint    = Color.FromArgb(0x71, 0x74, 0x8A);   // overlay1 加深到 4.6:1
                Hover       = Color.FromArgb(0xE6, 0xE9, 0xEF);
                Down        = Color.FromArgb(0xDC, 0xE0, 0xE8);
                BtnBg       = Color.FromArgb(14, 0x4C, 0x4F, 0x69);
                Disabled    = Color.FromArgb(0xF1, 0xF3, 0xF7);
                DisabledOn  = Color.FromArgb(0x83, 0xAB, 0xFA);   // Primary 禁用: 淡强调底
                // 下面三色 2026-10-29 加深：Ok/Warn/InkFaint 压在 #FEFFFF 卡片上时
                // 对比度只有 3.34 / 2.61 / 3.20，低于 WCAG AA 的 4.5（状态字和淡说明字都读不清）。
                Ok          = Color.FromArgb(0x2F, 0x74, 0x20);
                Err         = Color.FromArgb(0xD2, 0x0F, 0x39);   // red
                Warn        = Color.FromArgb(0x8A, 0x5A, 0x0B);
                Highlight   = Color.FromArgb(140, 0xFF, 0xFF, 0xFF);
                Glow        = Color.FromArgb(22, 0x1E, 0x66, 0xF5);
            }
            else
            {
                // ---- Catppuccin Mocha ----
                Bg          = Color.FromArgb(0x1E, 0x1E, 0x2E);   // base
                BgTop       = Color.FromArgb(0x25, 0x26, 0x3A);
                Card        = Color.FromArgb(0xF2, 0x2A, 0x2B, 0x40);   // surface, 略透
                CardSolid   = Color.FromArgb(0x29, 0x2A, 0x3F);         // 同上压平后的实色
                CardBorder  = Color.FromArgb(30, 0xCD, 0xD6, 0xF4);
                InputBorder = Color.FromArgb(46, 0xCD, 0xD6, 0xF4);
                FieldBg     = Color.FromArgb(0x31, 0x32, 0x44);   // surface0
                Accent      = Color.FromArgb(0x89, 0xB4, 0xFA);   // blue
                AccentHover = Color.FromArgb(0x9E, 0xC2, 0xFB);
                AccentDown  = Color.FromArgb(0x74, 0xA0, 0xE8);
                AccentInk   = Color.FromArgb(0x11, 0x11, 0x1B);   // crust, 深底上压蓝要深字
                AccentSoft  = Color.FromArgb(40, 0x89, 0xB4, 0xFA);
                Ink         = Color.FromArgb(0xCD, 0xD6, 0xF4);   // text
                InkDim      = Color.FromArgb(0xA6, 0xAD, 0xC8);   // subtext0
                InkFaint    = Color.FromArgb(0x9A, 0xA0, 0xB8);   // overlay1 提亮到 4.5:1
                Hover       = Color.FromArgb(0x33, 0x35, 0x4B);
                Down        = Color.FromArgb(0x3B, 0x3D, 0x55);
                BtnBg       = Color.FromArgb(24, 0xCD, 0xD6, 0xF4);
                Disabled    = Color.FromArgb(0x2E, 0x30, 0x46);   // 深色禁用底(实色)
                DisabledOn  = Color.FromArgb(0x5E, 0x76, 0xA6);   // 深色 Primary 禁用
                Ok          = Color.FromArgb(0xA6, 0xE3, 0xA1);   // green
                Err         = Color.FromArgb(0xF3, 0x8B, 0xA8);   // red
                Warn        = Color.FromArgb(0xF9, 0xE2, 0xAF);   // yellow
                Highlight   = Color.FromArgb(22, 0xFF, 0xFF, 0xFF);
                Glow        = Color.FromArgb(30, 0x89, 0xB4, 0xFA);
            }
        }

        /// <summary>读注册表判断系统是深色还是浅色。</summary>
        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return ((int)v) == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>mode: auto / light / dark</summary>
        public static void ApplyMode(string mode)
        {
            bool dark;
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) dark = true;
            else if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) dark = false;
            else dark = SystemIsDark();
            Apply(dark);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > r.Width || d > r.Height) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public static class Fonts
    {
        public static readonly Font UI     = Make("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font UIBold = Make("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        public static readonly Font Small  = Make("Microsoft YaHei UI", 8.25F, FontStyle.Regular);
        public static readonly Font Title  = Make("Microsoft YaHei UI", 15F, FontStyle.Bold);
        public static readonly Font Status = Make("Microsoft YaHei UI", 9.75F, FontStyle.Bold);
        public static readonly Font Mono   = PickMono();

        static Font Make(string name, float size, FontStyle style)
        {
            try { return new Font(name, size, style); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style); }
        }

        static Font PickMono()
        {
            try
            {
                foreach (var f in FontFamily.Families)
                    if (string.Equals(f.Name, "Cascadia Mono", StringComparison.OrdinalIgnoreCase))
                        return new Font(f, 8.75F);
            }
            catch { }
            return Make("Consolas", 8.75F, FontStyle.Regular);
        }
    }

    // ================================================================
    //  Win11 圆角窗口 + 深色标题栏
    // ================================================================
    public static class Dwm
    {
        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        static extern int SetAttr(IntPtr hwnd, int attr, ref int value, int size);

        public static void RoundCorners(IntPtr hwnd)
        {
            try { int v = 2; SetAttr(hwnd, 33, ref v, 4); } catch { }   // DWMWCP_ROUND
        }

        public static void SetTitleBarDark(IntPtr hwnd, bool dark)
        {
            // 20 = Win10 20H1+/Win11; 19 = 更早的版本。两个都试, 谁认就用谁的
            try { int v = dark ? 1 : 0; SetAttr(hwnd, 20, ref v, 4); } catch { }
            try { int v2 = dark ? 1 : 0; SetAttr(hwnd, 19, ref v2, 4); } catch { }

            // 上面那个标志有些系统不认, 那就把标题栏颜色直接写死(Win11 22000+)
            // COLORREF 是 0x00BBGGRR, 不是 RGBA; 这里跟着主题底色走, 别用死黑
            try
            {
                int cap = dark ? 0x002E1E1E : 0x00F5F1EF;   // 0xBBGGRR
                SetAttr(hwnd, 35, ref cap, 4);
                int txt = dark ? 0x00F4D6CD : 0x00694F4C;
                SetAttr(hwnd, 36, ref txt, 4);
            }
            catch { }
        }
    }

    // ================================================================
    //  圆角卡片(可透明, 让窗口的渐变透上来)
    // ================================================================
    public class CardPanel : Panel
    {
        public int Radius = Theme.RCard;
        public Color Fill = Theme.Card;
        public Color Line = Theme.CardBorder;
        public bool ShowLine = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            // 卡片不给透明底: 圆角外露出来的部分直接是父级实色(窗口底), 既保住圆角,
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先把整块客户端填成不透明的 BackColor 再画。
            // 双缓冲的位图初始 alpha 全是 0, GDI 画文字时看到的是「透明底」,
            // ClearType 会画出黑边甚至糊成一团(2026-10-28 实测: 未选中的页签就是这样糊的)。
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width <= 0 || r.Height <= 0) return;

            using (var path = Theme.RoundRect(r, Radius))
            {
                using (var b = new SolidBrush(Fill)) e.Graphics.FillPath(b, path);
                if (ShowLine) using (var p = new Pen(Line)) e.Graphics.DrawPath(p, path);
            }

            // 顶部 1px 高光: DSH 玻璃卡片的 inset highlight, 让卡片有厚度
            if (Radius > 0 && Width > Radius * 3)
            {
                using (var p = new Pen(Theme.Highlight))
                    e.Graphics.DrawLine(p, Radius, 1, Width - Radius - 1, 1);
            }
        }
    }

    // ================================================================
    //  圆角输入框, 聚焦时边框变强调色
    // ================================================================
    public class InputBox : CardPanel
    {
        public TextBox Inner;

        public InputBox(int width)
        {
            Radius = Theme.RField;
            Fill = Theme.FieldBg;
            Line = Theme.InputBorder;
            Padding = Theme.Pad(11, 6, 11, 6);   // 上下留 6: 32 高的框里正好把 23px 的字摆中间
            Width = Theme.Px(width);
            Height = Theme.Px(32);
            Margin = Theme.Pad(0, 0, 0, 10);

            Inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Fonts.UI,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Ink
            };
            Inner.GotFocus += (s, e) => { Line = Theme.Accent; Invalidate(); };
            Inner.LostFocus += (s, e) => { Line = Theme.InputBorder; Invalidate(); };
            Controls.Add(Inner);
        }

        /// <summary>
        /// 把打字框在垂直方向摆正。
        /// 原来给 Inner 的是 Dock = Fill, 但单行 TextBox 的高度由字体算出来
        /// (实测 22px), Dock 只能把它拉宽、拉不高: 于是在 54px 高的框里,
        /// 字上面留 12px、下面留 20px, 看着就是"字贴着上沿"。
        /// 改成自己算 (Height - PreferredHeight) / 2。
        /// </summary>
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Inner == null) return;
            int h = Inner.PreferredHeight;
            int x = Padding.Left;
            int w = Width - Padding.Horizontal;
            if (w < Theme.Px(8)) w = Theme.Px(8);
            int y = (Height - h) / 2;
            if (y < 0) y = 0;
            Inner.Bounds = new Rectangle(x, y, w, h);
        }

        public override string Text
        {
            get { return Inner == null ? "" : Inner.Text; }
            set { if (Inner != null) Inner.Text = value; }
        }

        /// <summary>内容改变时回调, 给"输入框里的字变了要顺手刷新状态"用。</summary>
        public event EventHandler TextChangedInner
        {
            add { if (Inner != null) Inner.TextChanged += value; }
            remove { if (Inner != null) Inner.TextChanged -= value; }
        }
    }

    // ================================================================
    //  下拉框: 自己画的圆角框 + 弹出列表
    //  (系统 ComboBox 在深色主题下是白底方块, 跟卡片格格不入, 所以重写了)
    //  Editable = true 时可以直接打字(扫到的 WiFi 只是候选)
    // ================================================================
    public class DropDown : Control
    {
        public class ItemList
        {
            readonly List<string> items;
            public ItemList(List<string> items) { this.items = items; }

            public int Count { get { return items.Count; } }
            public string this[int i] { get { return items[i]; } set { items[i] = value; } }

            public void Add(string s) { items.Add(s); }
            public void Add(object o) { items.Add(o == null ? "" : o.ToString()); }
            public void AddRange(object[] os)
            {
                if (os == null) return;
                foreach (var o in os) Add(o);
            }
            public void Clear() { items.Clear(); }
            public int IndexOf(string s) { return items.IndexOf(s); }
        }

        readonly List<string> items = new List<string>();
        readonly ItemList itemList;
        TextBox editor;
        bool hover, open, focused;
        int arrowW;
        PopupList pop;   // 展开在窗体里的候选列表(见 PopupList)

        public int MaxDropHeight = 240;

        public DropDown(int width)
        {
            itemList = new ItemList(items);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.CardSolid;
            Font = Fonts.UI;
            Width = Theme.Px(width);
            Height = Theme.Px(32);
            Margin = Theme.Pad(0, 0, 0, 10);
            TabStop = true;
            arrowW = Theme.Px(26);
        }

        public ItemList Items { get { return itemList; } }

        /// <summary>true = 可以直接打字(比如 WiFi 名称), false = 只能从列表里选。</summary>
        public bool Editable
        {
            get { return editor != null; }
            set
            {
                if (value && editor == null)
                {
                    editor = new TextBox
                    {
                        BorderStyle = BorderStyle.None,
                        Font = Fonts.UI,
                        BackColor = Theme.FieldBg,
                        ForeColor = Theme.Ink
                    };
                    editor.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
                    editor.LostFocus += delegate { focused = false; Invalidate(); };
                    editor.GotFocus += delegate { focused = true; Invalidate(); };
                    Controls.Add(editor);
                    Padding = Theme.Pad(11, 8, arrowW + 6, 8);
                }
                else if (!value && editor != null)
                {
                    string txt = editor.Text;
                    Controls.Remove(editor);
                    editor = null;
                    Padding = Padding.Empty;
                    Text = txt;
                }
                PerformLayout();
                Invalidate();
            }
        }

        int sel = -1;
        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                int v = (value < -1 || value >= items.Count) ? -1 : value;
                if (sel == v) return;
                sel = v;
                if (editor == null) Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler SelectedIndexChanged;

        public override string Text
        {
            get
            {
                if (editor != null) return editor.Text;
                return (sel >= 0 && sel < items.Count) ? items[sel] : "";
            }
            set
            {
                if (editor != null) { editor.Text = value == null ? "" : value; return; }
                int i = items.IndexOf(value == null ? "" : value);
                if (i >= 0) SelectedIndex = i;
                else Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先把整块客户端填成不透明的 BackColor 再画。
            // 双缓冲的位图初始 alpha 全是 0, GDI 画文字时看到的是「透明底」,
            // ClearType 会画出黑边甚至糊成一团(2026-10-28 实测: 未选中的页签就是这样糊的)。
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Theme.RField))
            {
                using (var b = new SolidBrush(Theme.FieldBg)) e.Graphics.FillPath(b, path);
                Color line = (open || focused) ? Theme.Accent
                           : (hover ? Theme.InkFaint : Theme.InputBorder);
                using (var p = new Pen(line)) e.Graphics.DrawPath(p, path);
            }

            if (editor == null)
            {
                string txt = (sel >= 0 && sel < items.Count) ? items[sel] : "";
                var tr = new Rectangle(Theme.Px(11), 0, Width - arrowW - Theme.Px(14), Height);
                TextRenderer.DrawText(e.Graphics, txt, Font, tr, Theme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            // 右端的小箭头
            int cx = Width - arrowW / 2 - Theme.Px(6);
            int cy = Height / 2;
            int w = Theme.Px(7), h = Theme.Px(4);
            using (var p = new Pen(hover || open ? Theme.Ink : Theme.InkDim, Math.Max(1.4f, Theme.Scale * 1.4f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                e.Graphics.DrawLines(p, new Point[] {
                    new Point(cx - w, cy - h / 2),
                    new Point(cx,     cy + h / 2),
                    new Point(cx + w, cy - h / 2)
                });
            }
        }

        /// <summary>主题换了以后调用: 刷新内嵌输入框的颜色并重绘。</summary>
        public void Restyle()
        {
            if (editor != null) { editor.BackColor = Theme.FieldBg; editor.ForeColor = Theme.Ink; }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            // 这里**不能**开弹层。Control.WmMouseDown 处理完 OnMouseDown 之后会把
            // Capture 设回本控件(鼠标按下期间框架要抓鼠标), 而弹层在 Open() 里刚把
            // Capture 抢走 —— 于是弹层立刻收到「捕获丢失」→ OnMouseCaptureChanged
            // 判定成「点到框外」→ 当场关掉。用户看到的就是「点了完全没反应」。
            // 改成等 MouseUp（键已松开、Capture 记账结束）再开。
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (open) DropPopup();          // 再点一下同一个下拉 = 收起
            else OpenPopup();
        }

        protected override void OnGotFocus(EventArgs e) { focused = true; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { focused = false; Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down && items.Count > 0)
            {
                SelectedIndex = Math.Min(items.Count - 1, sel + 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && items.Count > 0)
            {
                SelectedIndex = sel <= 0 ? 0 : sel - 1;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Down && e.Alt) || e.KeyCode == Keys.F4)
            {
                OpenPopup();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 打开候选列表。
        /// 旧实现开的是独立无边框窗体: 它要 Show + Activate 才响应鼠标, 关它只能
        /// 靠 Deactivate, 但点 DropDown 自身并不会让弹层失活 —— 于是旧列表留在
        /// 屏幕上、再点就叠起来, 用起来就是「下拉卡住」。现在换成本窗体里的
        /// 子控件 PopupList: 打开时抓鼠标, 点外面/滚轮/回车/Esc 都能确定地关。
        /// </summary>
        void OpenPopup()
        {
            if (items.Count == 0) return;
            DropPopup();
            var host = FindForm();
            if (host == null) return;

            int rowH = Theme.Px(26);
            int rows = Math.Min(items.Count, 8);
            var list = new PopupList();
            list.Items = items.ToArray();
            list.Sel = sel;
            list.RowH = rowH;
            list.PageRows = rows;

            var here = host.PointToClient(PointToScreen(new Point(0, 0)));
            int h = rows * rowH + Theme.Px(8);
            int top = here.Y + Height + Theme.Px(2);
            int limit = host.ClientSize.Height - Theme.Px(4);
            if (top + h > limit)
            {
                int above = here.Y - h - Theme.Px(2);   // 翻到上面
                if (above >= Theme.Px(4)) top = above;
                else
                {
                    // 上下都放不下(窗口很小): 贴着底边, 行数减到放得下的最大值
                    int fit = (limit - (here.Y + Height) - Theme.Px(6)) / rowH;
                    if (fit < 3) fit = 3;
                    if (fit > items.Count) fit = items.Count;
                    list.PageRows = fit;
                    h = fit * rowH + Theme.Px(8);
                    top = Math.Max(Theme.Px(4), limit - h);
                }
            }
            list.Bounds = new Rectangle(here.X, top, Width, h);

            list.Committed += delegate(int i) { SelectedIndex = i; DropPopup(); };
            list.Dismissed += delegate { DropPopup(); };
            host.Controls.Add(list);
            list.BringToFront();
            pop = list;
            open = true;
            Invalidate();
            list.Open();
        }

        void DropPopup()
        {
            open = false;
            var p = pop;
            pop = null;
            if (p != null)
            {
                if (p.Parent != null) p.Parent.Controls.Remove(p);
                p.Dispose();
            }
            Invalidate();
        }
    }

    // ================================================================
    //  下拉弹出的候选列表: 本窗体里的一个子控件, 不是独立窗体
    //  独立窗体(Form)做弹层的三个坑, 表现都是「卡住」:
    //   1) 它要 Show + Activate 才吃鼠标, 而 Activate 会把本窗口的激活抢走;
    //   2) 关它只能靠 Deactivate, 可点 DropDown 自己不会让它失活, 旧弹层就
    //      赖在屏幕上, 再点就是两个叠着;
    //   3) 它不在本窗体的控件树里, 窗口移动/缩放/切主题时不会跟着走。
    //  现在用鼠标捕获(Capture)接管: 打开后所有鼠标消息都进这里, 「点在框外」
    //  就是关, 不依赖任何失去激活的时序。
    // ================================================================
    class PopupList : Control
    {
        public string[] Items = new string[0];
        public int Sel = -1;
        public int RowH = 26;
        public int PageRows = 8;

        public event Action<int> Committed;   // 选了第 i 项
        public event Action Dismissed;        // 没选, 关掉

        int hover = -1;
        int top;                              // 第一个可见行的下标
        bool closing;
        DateTime openedAt;                    // 刚打开的一小段时间内不把"捕获丢失"当点外框

        public PopupList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FieldBg;
            Font = Fonts.UI;
            TabStop = true;
        }

        /// <summary>显示: 拿焦点 + 抓鼠标。
        /// 拿焦点才有键盘(↑↓/回车/Esc), 抓鼠标才知道"点到了框外"。</summary>
        public void Open()
        {
            if (Sel >= PageRows) top = Sel - PageRows + 1;
            if (top < 0) top = 0;
            Focus();
            openedAt = DateTime.Now;
            Invalidate();
            // 抓鼠标要推到这条鼠标消息处理完之后再做（BeginInvoke 排队到下一次消息循环）。
            // 同步抢 Capture 会被框架随后的记账抢回去, 弹层立刻自我关闭。
            BeginInvoke((Action)delegate
            {
                if (IsDisposed || closing) return;
                Capture = true;
            });
        }

        void Finish(bool commit)
        {
            if (closing) return;
            closing = true;
            Capture = false;
            if (commit) { var c = Committed; if (c != null && Sel >= 0) c(Sel); }
            else { var d = Dismissed; if (d != null) d(); }
        }

        int IndexAt(Point p)
        {
            int pad = Theme.Px(4);
            if (p.Y < pad) return -1;
            int i = top + (p.Y - pad) / RowH;
            return (i >= 0 && i < Items.Length) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexAt(e.Location);
            if (i >= 0) { Sel = i; Finish(true); }
            else Finish(false);                  // 点在框外 = 关
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Items.Length <= PageRows) return;
            int t = top - Math.Sign(e.Delta);
            if (t < 0) t = 0;
            if (t > Items.Length - PageRows) t = Items.Length - PageRows;
            top = t;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // 抓着的鼠标没了 = 用户点到别处去了(或窗口失活)。
            // 刚打开的这一两百毫秒内不算: 那时 Capture 还在排队设置, 中间的丢失是记账噪声,
            // 把它当"点到框外"就会一点就没了。
            if (!Capture && !closing && (DateTime.Now - openedAt).TotalMilliseconds > 250) Finish(false);
        }

        protected override bool IsInputKey(Keys v) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Finish(false); return; }
            if (e.KeyCode == Keys.Enter) { e.Handled = true; Finish(true); return; }
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
            e.Handled = true;
            int n = Items.Length;
            if (n == 0) return;
            bool down = e.KeyCode == Keys.Down;
            int i = Sel < 0 ? (down ? 0 : n - 1) : Sel + (down ? 1 : -1);
            if (i < 0) i = 0;
            if (i >= n) i = n - 1;
            Sel = i;
            if (Sel < top) top = Sel;
            if (Sel >= top + PageRows) top = Sel - PageRows + 1;
            hover = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(Theme.FieldBg))
            using (var pen = new Pen(Theme.InputBorder))
            {
                g.FillRectangle(fill, rc);
                g.DrawRectangle(pen, rc);
            }

            int pad = Theme.Px(4);
            int pool = Items.Length - top;
            int shown = Math.Min(pool, (Height - pad * 2) / RowH);
            for (int k = 0; k < shown; k++)
            {
                int idx = top + k;
                var row = new Rectangle(Theme.Px(1), pad + k * RowH, Width - Theme.Px(2), RowH);
                bool isSel = idx == Sel;
                bool isHover = idx == hover;
                if (isSel || isHover)
                {
                    using (var b = new SolidBrush(isSel ? Theme.AccentSoft : Theme.Hover))
                        g.FillRectangle(b, row);
                }
                if (isSel)
                {
                    using (var b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, new Rectangle(row.X, row.Y, Theme.Px(2), row.Height));
                }
                var tr = new Rectangle(row.X + Theme.Px(9), row.Y, row.Width - Theme.Px(18), row.Height);
                TextRenderer.DrawText(g, Items[idx], Font, tr,
                    isSel ? Theme.Accent : Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            // 还有更多项时, 右侧画一条很淡的滚动提示
            if (Items.Length > PageRows)
            {
                int trackX = Width - Theme.Px(4);
                int trackH = Height - pad * 2;
                int barH = Math.Max(Theme.Px(16), trackH * PageRows / Items.Length);
                int barY = pad + (trackH - barH) * top / Math.Max(1, Items.Length - PageRows);
                using (var b = new SolidBrush(Color.FromArgb(60, Theme.InkFaint)))
                    g.FillRectangle(b, new Rectangle(trackX, barY, Theme.Px(2), barH));
            }
        }
    }

    // ================================================================
    //  数字输入: 圆角框 + 右端加减, 也可以直接打字
    //  (系统 NumericUpDown 是灰底小方框 + 系统箭头, 深色下很跳)
    // ================================================================
    public class Stepper : Control
    {
        public int Minimum = 0;
        public int Maximum = 9999;

        int val;
        TextBox inner;
        int zoneW;
        int hoverZone;   // 0=无 1=减 2=加
        bool syncing;

        public Stepper(int width)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.CardSolid;
            Font = Fonts.UI;
            Width = Theme.Px(width);
            Height = Theme.Px(32);
            Margin = Theme.Pad(0, 0, 10, 10);
            zoneW = Theme.Px(20);

            inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Fonts.UI,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Ink,
                TextAlign = HorizontalAlignment.Right
            };
            inner.TextChanged += delegate
            {
                if (syncing) return;
                int v;
                if (int.TryParse(inner.Text.Trim(), out v)) { v = Clamp(v); if (v != val) { val = v; var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); } }
            };
            inner.LostFocus += delegate { syncing = true; inner.Text = val.ToString(); syncing = false; Invalidate(); };
            inner.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; } };
            Controls.Add(inner);
        }

        int Clamp(int v) { return v < Minimum ? Minimum : (v > Maximum ? Maximum : v); }

        public int Value
        {
            get { return val; }
            set
            {
                int v = Clamp(value);
                if (v == val) { if (inner != null) { syncing = true; inner.Text = v.ToString(); syncing = false; } return; }
                val = v;
                if (inner != null) { syncing = true; inner.Text = v.ToString(); syncing = false; }
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public event EventHandler ValueChanged;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (inner != null)
            {
                int w = Width - Theme.Px(11) - zoneW * 2 - Theme.Px(8);
                inner.SetBounds(Theme.Px(11), (Height - inner.PreferredHeight) / 2 + 2, Math.Max(Theme.Px(20), w), inner.PreferredHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先把整块客户端填成不透明的 BackColor 再画。
            // 双缓冲的位图初始 alpha 全是 0, GDI 画文字时看到的是「透明底」,
            // ClearType 会画出黑边甚至糊成一团(2026-10-28 实测: 未选中的页签就是这样糊的)。
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Theme.RField))
            {
                using (var b = new SolidBrush(Theme.FieldBg)) e.Graphics.FillPath(b, path);
                using (var p = new Pen(Theme.InputBorder)) e.Graphics.DrawPath(p, path);
            }

            // 右端两个按钮的分隔线
            int x0 = Width - zoneW * 2;
            using (var p = new Pen(Theme.CardBorder))
                e.Graphics.DrawLine(p, x0, Theme.Px(8), x0, Height - Theme.Px(8));
            using (var p = new Pen(Theme.CardBorder))
                e.Graphics.DrawLine(p, x0 + zoneW, Theme.Px(8), x0 + zoneW, Height - Theme.Px(8));

            if (hoverZone == 1)
            {
                using (var b = new SolidBrush(Theme.AccentSoft))
                using (var path = Theme.RoundRect(new Rectangle(x0 + 1, 2, zoneW - 2, Height - 5), Theme.RField - 3))
                    e.Graphics.FillPath(b, path);
            }
            else if (hoverZone == 2)
            {
                using (var b = new SolidBrush(Theme.AccentSoft))
                using (var path = Theme.RoundRect(new Rectangle(x0 + zoneW + 1, 2, zoneW - 1, Height - 5), Theme.RField - 3))
                    e.Graphics.FillPath(b, path);
            }

            float pen = Math.Max(1.4f, Theme.Scale * 1.4f);
            Color ink = Enabled ? Theme.InkDim : Theme.InkFaint;
            int cy = Height / 2, hw = Theme.Px(4);
            using (var p = new Pen(hoverZone == 1 ? Theme.Accent : ink, pen))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                int c1 = x0 + zoneW / 2;
                e.Graphics.DrawLine(p, c1 - hw, cy, c1 + hw, cy);
            }
            using (var p = new Pen(hoverZone == 2 ? Theme.Accent : ink, pen))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                int c2 = x0 + zoneW + zoneW / 2;
                e.Graphics.DrawLine(p, c2 - hw, cy, c2 + hw, cy);
                e.Graphics.DrawLine(p, c2, cy - hw, c2, cy + hw);
            }
        }

        int ZoneAt(Point p)
        {
            int x0 = Width - zoneW * 2;
            if (p.X < x0) return 0;
            return p.X < x0 + zoneW ? 1 : 2;
        }

        /// <summary>主题换了以后调用。</summary>
        public void Restyle()
        {
            if (inner != null) { inner.BackColor = Theme.FieldBg; inner.ForeColor = Theme.Ink; }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = ZoneAt(e.Location);
            if (z != hoverZone) { hoverZone = z; Cursor = z == 0 ? Cursors.Default : Cursors.Hand; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverZone != 0) { hoverZone = 0; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int z = ZoneAt(e.Location);
            if (z == 1) Value = val - 1;
            else if (z == 2) Value = val + 1;
            else { if (inner != null) inner.Focus(); }
        }
    }

    // ================================================================
    //  圆角按钮: Primary(实心强调) / 普通(描边) / Segment(页签)
    // ================================================================
    public class Win11Button : Button
    {
        public bool Primary = false;
        public bool Segment = false;
        public bool Selected = false;
        /// <summary>危险动作（如「卸载」）：平时与普通按钮一样，只有鼠标悬停时字和描边变 Theme.Err。</summary>
        public bool DangerOnHover = false;
        bool hover, down;

        public Win11Button()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.CardSolid;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Fonts.UI;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // 8 个按钮要挤进一排, 单个按钮的内边距和最小宽度都得收一点:
            // 原来 Px(16) 左右内边距 + Px(84) 最小宽, 8 个排下来 1214 设备像素, 窗口被迫变宽。
            MinimumSize = new Size(Theme.Px(72), Theme.Px(32));
            Padding = Theme.Pad(13, 0, 13, 0);
            Margin = Theme.Pad(0, 0, 8, 0);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先把整块客户端填成不透明的 BackColor 再画。
            // 双缓冲的位图初始 alpha 全是 0, GDI 画文字时看到的是「透明底」,
            // ClearType 会画出黑边甚至糊成一团(2026-10-28 实测: 未选中的页签就是这样糊的)。
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, line, ink;
            if (Segment)
            {
                // 页签: 选中 = 淡强调底 + 强调字; 未选 = 透明底 + 一圈很淡的边
                // (完全不给边的话, 未选中的页签看起来就是三个字, 用户不知道能点)
                fill = Selected ? Theme.AccentSoft : (hover ? Theme.BtnBg : Color.Transparent);
                line = Selected ? Color.FromArgb(90, Theme.Accent)
                     : (hover ? Theme.InputBorder : Color.FromArgb(60, Theme.InputBorder));
                ink = Selected ? Theme.Accent : (hover ? Theme.Ink : Theme.InkDim);
            }
            else if (Primary)
            {
                fill = down ? Theme.AccentDown : (hover ? Theme.AccentHover : Theme.Accent);
                line = fill;
                ink = Theme.AccentInk;
            }
            else
            {
                fill = down ? Theme.Down : (hover ? Theme.Hover : Theme.BtnBg);
                line = hover ? Theme.InputBorder : Theme.CardBorder;
                ink = Theme.Ink;
                if (DangerOnHover && hover) { line = Theme.Err; ink = Theme.Err; }
            }

            if (!Enabled)
            {
                // 实色，不用半透明。半透明填色会让 GDI 在混合底上画 ClearType 糊字（§22.7 老坑）。
                fill = Primary ? Theme.DisabledOn : Theme.Disabled;
                line = Primary ? Theme.DisabledOn : Theme.CardBorder;
                ink = Theme.InkFaint;
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (fill.A > 0 || line.A > 0)
            {
                using (var path = Theme.RoundRect(r, Segment ? Theme.Px(8) : Theme.RBtn))
                {
                    if (fill.A > 0) using (var b = new SolidBrush(fill)) e.Graphics.FillPath(b, path);
                    if (line.A > 0) using (var p = new Pen(line)) e.Graphics.DrawPath(p, path);
                }
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, r, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ================================================================
    //  程序图标 / 托盘图标
    //  圆角方块底 + 白色 WiFi 弧, 换底色就是不同状态
    //  绿色 = 程序图标(跟 EXE 图标一致)
    //  蓝色 = 托盘·已连上校园网
    //  红色 = 托盘·未连上
    // ================================================================
    public static class AppIcon
    {
        public static readonly Color Green = Color.FromArgb(0x16, 0xA3, 0x4A);
        public static readonly Color Blue  = Color.FromArgb(0x00, 0x78, 0xD4);
        public static readonly Color Red   = Color.FromArgb(0xD1, 0x34, 0x38);

        // 缓存的是**像素字节**, 不是 Icon 对象。
        // 以前缓存 Icon 并让窗口和托盘共用同一个实例: HICON 只有一份, 谁先被销毁谁就把图标
        // 抽走, 于是任务栏/托盘里会出现"图标不见了"。每次用都从字节新建一个自己拥有的 Icon。
        static readonly Dictionary<int, byte[]> cache = new Dictionary<int, byte[]>();

        public static Icon Get(Color bg)
        {
            int k = bg.ToArgb();
            byte[] bytes;
            if (!cache.TryGetValue(k, out bytes))
            {
                bytes = BuildBytes(bg, 32);
                cache[k] = bytes;
            }
            return new Icon(new System.IO.MemoryStream(bytes));
        }

        static byte[] BuildBytes(Color bg, int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                int rad = Math.Max(2, (int)(size * 0.18));
                int d = rad * 2;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(0, 0, d, d, 180, 90);
                    path.AddArc(size - d - 1, 0, d, d, 270, 90);
                    path.AddArc(size - d - 1, size - d - 1, d, d, 0, 90);
                    path.AddArc(0, size - d - 1, d, d, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                }

                float cx = size / 2f;
                float cy = size * 0.76f;
                using (var pen = new Pen(Color.White, Math.Max(1f, size * 0.095f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    foreach (float rr in new float[] { 0.17f, 0.31f, 0.45f })
                    {
                        float dd = size * rr * 2f;
                        g.DrawArc(pen, cx - dd / 2f, cy - dd / 2f, dd, dd, 205, 130);
                    }
                }
                float dr = size * 0.085f;
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, cx - dr, cy - dr, dr * 2, dr * 2);
            }

            using (var ms = new System.IO.MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                byte[] png = ms.ToArray();
                return PngToIco(png, size);
            }
        }

        /// <summary>把 32x32 PNG 包成最小可用的 .ico(Vista+ 支持 PNG 压缩的图标项)。</summary>
        static byte[] PngToIco(byte[] png, int size)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write((ushort)0);      // reserved
                w.Write((ushort)1);      // type = icon
                w.Write((ushort)1);      // count
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);        // palette
                w.Write((byte)0);        // reserved
                w.Write((ushort)1);      // planes
                w.Write((ushort)32);     // bpp
                w.Write((uint)png.Length);
                w.Write((uint)22);       // offset
                w.Write(png);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
