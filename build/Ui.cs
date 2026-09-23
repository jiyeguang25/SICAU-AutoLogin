using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sicau
{
    // ================================================================
    //  主题: 浅色 / 深色 两套 Win11 配色, 支持跟随系统
    // ================================================================
    public static class Theme
    {
        // 浅色
        public static Color Bg          = Color.FromArgb(0xF3, 0xF3, 0xF3);
        public static Color Card        = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color CardBorder  = Color.FromArgb(0xE8, 0xE8, 0xE8);
        public static Color InputBorder = Color.FromArgb(0xD8, 0xD8, 0xD8);
        public static Color FieldBg     = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color Accent      = Color.FromArgb(0x00, 0x67, 0xC0);
        public static Color AccentHover = Color.FromArgb(0x1A, 0x75, 0xC8);
        public static Color AccentDown  = Color.FromArgb(0x00, 0x53, 0x9B);
        public static Color AccentInk   = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color Ink         = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public static Color InkDim      = Color.FromArgb(0x61, 0x61, 0x61);
        public static Color Hover       = Color.FromArgb(0xF7, 0xF7, 0xF7);
        public static Color Down        = Color.FromArgb(0xEE, 0xEE, 0xEE);
        public static Color BtnBg       = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color Ok          = Color.FromArgb(0x0F, 0x7B, 0x0F);
        public static Color Err         = Color.FromArgb(0xC4, 0x2B, 0x1C);

        public static bool Dark = false;

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                Bg          = Color.FromArgb(0xF3, 0xF3, 0xF3);   // Win11 内容底色
                Card        = Color.FromArgb(0xFF, 0xFF, 0xFF);   // 卡片
                CardBorder  = Color.FromArgb(0xE5, 0xE5, 0xE5);   // 卡片描边(很淡)
                InputBorder = Color.FromArgb(0xD6, 0xD6, 0xD6);
                FieldBg     = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Accent      = Color.FromArgb(0x00, 0x67, 0xC0);
                AccentHover = Color.FromArgb(0x1A, 0x75, 0xC8);
                AccentDown  = Color.FromArgb(0x00, 0x53, 0x9B);
                AccentInk   = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Ink         = Color.FromArgb(0x1A, 0x1A, 0x1A);
                InkDim      = Color.FromArgb(0x5D, 0x5D, 0x5D);   // 次级文字
                Hover       = Color.FromArgb(0xF7, 0xF7, 0xF7);
                Down        = Color.FromArgb(0xED, 0xED, 0xED);
                BtnBg       = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Ok          = Color.FromArgb(0x0F, 0x7B, 0x0F);
                Err         = Color.FromArgb(0xC4, 0x2B, 0x1C);
            }
            else
            {
                Bg          = Color.FromArgb(0x20, 0x20, 0x20);   // Win11 深色内容底色
                Card        = Color.FromArgb(0x2B, 0x2B, 0x2B);   // 卡片
                CardBorder  = Color.FromArgb(0x38, 0x38, 0x38);   // 卡片描边
                InputBorder = Color.FromArgb(0x3D, 0x3D, 0x3D);
                FieldBg     = Color.FromArgb(0x33, 0x33, 0x33);   // 深色下控件要比卡片亮一点
                Accent      = Color.FromArgb(0x60, 0xCD, 0xFF);
                AccentHover = Color.FromArgb(0x7A, 0xD7, 0xFF);
                AccentDown  = Color.FromArgb(0x4A, 0xB8, 0xEB);
                AccentInk   = Color.FromArgb(0x00, 0x1A, 0x2A);
                Ink         = Color.FromArgb(0xFF, 0xFF, 0xFF);
                InkDim      = Color.FromArgb(0xC5, 0xC5, 0xC5);
                Hover       = Color.FromArgb(0x3A, 0x3A, 0x3A);
                Down        = Color.FromArgb(0x44, 0x44, 0x44);
                BtnBg       = Color.FromArgb(0x32, 0x32, 0x32);
                Ok          = Color.FromArgb(0x6C, 0xCB, 0x5F);
                Err         = Color.FromArgb(0xFF, 0x99, 0xA4);
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
        public static readonly Font Title  = Make("Microsoft YaHei UI", 16F, FontStyle.Bold);
        public static readonly Font Status = Make("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
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
                        return new Font(f, 9F);
            }
            catch { }
            return Make("Consolas", 9F, FontStyle.Regular);
        }
    }

    // ================================================================
    //  Win11 圆角窗口 + 深色标题栏
    // ================================================================
    public static class Dwm
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
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
            // COLORREF 是 0x00BBGGRR, 不是 RGBA
            try
            {
                int cap = dark ? 0x00202020 : 0x00FFFFFF;
                SetAttr(hwnd, 35, ref cap, 4);
                int txt = dark ? 0x00FFFFFF : 0x00000000;
                SetAttr(hwnd, 36, ref txt, 4);
            }
            catch { }
        }
    }

    // ================================================================
    //  圆角卡片
    // ================================================================
    public class CardPanel : Panel
    {
        public int Radius = 8;
        // 默认跟随主题, 免得哪个卡片忘了设置就画出一块白底
        public Color Fill = Theme.Card;
        public Color Line = Theme.CardBorder;
        public bool ShowLine = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Radius))
            {
                using (var b = new SolidBrush(Fill)) e.Graphics.FillPath(b, path);
                if (ShowLine) using (var p = new Pen(Line)) e.Graphics.DrawPath(p, path);
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
            Radius = 5;
            Fill = Theme.FieldBg;
            Line = Theme.InputBorder;
            Padding = new Padding(10, 6, 10, 6);
            Width = width;
            Height = 34;
            Margin = new Padding(0, 0, 0, 10);

            Inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = Fonts.UI,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Ink
            };
            Inner.GotFocus += (s, e) => { Line = Theme.Accent; Invalidate(); };
            Inner.LostFocus += (s, e) => { Line = Theme.InputBorder; Invalidate(); };
            Controls.Add(Inner);
        }

        public override string Text
        {
            get { return Inner == null ? "" : Inner.Text; }
            set { if (Inner != null) Inner.Text = value; }
        }
    }

    // ================================================================
    //  扁平下拉框
    // ================================================================
    public class FlatCombo : ComboBox
    {
        public FlatCombo(int width)
        {
            Width = width;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            Font = Fonts.UI;
            Margin = new Padding(0, 0, 0, 10);
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 26;    // 收起时的高度 = ItemHeight + 边框, 这样跟 34px 的输入框/按钮一样高
            DrawItem += OnDrawItem;
        }

        void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            // DrawItemState.ComboBoxEdit = 收起时画的那个框, 那时候不能画成选中色
            bool isEdit = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;
            bool sel = !isEdit && (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = sel ? (Theme.Dark ? Color.FromArgb(0x2D, 0x45, 0x5C) : Color.FromArgb(0xEA, 0xF3, 0xFB))
                           : (Theme.Dark ? Color.FromArgb(0x2D, 0x2D, 0x2D) : Color.White);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var b = new SolidBrush(Theme.Ink))
                e.Graphics.DrawString(Items[e.Index].ToString(), Font, b, e.Bounds.X + 6, e.Bounds.Y + 3);
        }
    }

    // ================================================================
    //  Win11 扁平圆角按钮
    // ================================================================
    public class Win11Button : Button
    {
        public bool Primary = false;
        public int Radius = 5;
        bool hover, down;

        public Win11Button()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Fonts.UI;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(86, 34);
            Padding = new Padding(16, 0, 16, 0);
            Margin = new Padding(0, 0, 8, 0);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, line, ink;
            if (Primary)
            {
                fill = down ? Theme.AccentDown : (hover ? Theme.AccentHover : Theme.Accent);
                line = fill;
                ink = Theme.AccentInk;
            }
            else
            {
                fill = down ? Theme.Down : (hover ? Theme.Hover : Theme.BtnBg);
                line = Theme.InputBorder;
                ink = Theme.Ink;
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Radius))
            {
                using (var b = new SolidBrush(fill)) e.Graphics.FillPath(b, path);
                using (var p = new Pen(line)) e.Graphics.DrawPath(p, path);
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

        static readonly Dictionary<int, Icon> cache = new Dictionary<int, Icon>();

        public static Icon Get(Color bg)
        {
            int k = bg.ToArgb();
            Icon ico;
            if (cache.TryGetValue(k, out ico)) return ico;
            ico = Build(bg, 32);
            cache[k] = ico;
            return ico;
        }

        static Icon Build(Color bg, int size)
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
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
