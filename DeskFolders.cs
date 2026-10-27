using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

static class W
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
        IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO fi, uint cb, uint flags);
    [DllImport("shell32.dll", EntryPoint = "#727")] public static extern int SHGetImageList(int list, ref Guid iid, out IntPtr ppv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(int ev, uint flags, string item1, IntPtr item2);
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbm);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo;
        public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetLongPathName(string path, StringBuilder buf, int size);
    [DllImport("comctl32.dll")] public static extern IntPtr ImageList_GetIcon(IntPtr list, int i, int flags);
}

class Item
{
    public string Path, Label;
    public Bitmap Icon;
}

class FolderData
{
    public string Id, Name = "Folder", Lnk = "", Wallpaper = "", Store = "";
    public int Rows = 3, Cols = 3, Rgb = 0x32343A, Opacity = 88;
    public List<Item> Items = new List<Item>();
}

class DimForm : Form
{
    public DimForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.01;
        TopMost = true;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; }
    }
}

class GhostForm : Form
{
    public GhostForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(1, 1, 1);
        TransparencyKey = BackColor;
        Opacity = 0.85;
        TopMost = true;
        BackgroundImageLayout = ImageLayout.Zoom;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x20; return cp; }
    }
}

class FolderForm : Form
{
    Item pressItem;
    Point pressAt;
    bool dragOut;
    readonly GhostForm ghost = new GhostForm();
    readonly Timer dragTimer = new Timer();
    int hoverSlot = -1, dropSlot = -1, hoverDot = -1;
    readonly List<Item> sel = new List<Item>();
    List<Item> bandBase = new List<Item>();
    bool banding;
    Point bandA, bandB;
    DateTime hoverSince, dotSince;

    public FolderData D;

    const double AnimMs = 240;
    const int MaxRows = 6, MaxCols = 8;

    readonly float S;
    readonly int tile, sidePad, topExtra, botExtra, cellW, cellH, iconSz, panelPad, topPad, dotsH;
    readonly Point origin;
    Bitmap face, wp;

    double t;
    int dir;
    readonly Stopwatch sw = new Stopwatch();
    readonly Timer anim = new Timer();
    readonly DimForm dim = new DimForm();
    bool menuOpen, dialogOpen;
    int page;
    Rectangle curP;

    public FolderForm(FolderData d, Point from)
    {
        D = d;
        origin = from;
        Text = D.Name;
        Icon = Program.AppIcon;
        wp = Program.LoadWallpaper(D);
        RebuildFace(false);
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
        tile = I(56); sidePad = I(34); topExtra = I(56); botExtra = I(10);
        cellW = I(108); cellH = I(104); iconSz = I(48); panelPad = I(22); topPad = I(40); dotsH = I(26);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Rectangle a = TileRect();
        Bounds = Rectangle.FromLTRB(a.Left - sidePad, a.Top - topExtra, a.Right + sidePad, a.Bottom + botExtra);
        AllowDrop = true;
        anim.Interval = 15;
        anim.Tick += delegate { AnimTick(); };
        dragTimer.Interval = 50;
        dragTimer.Tick += delegate { DragUpdate(); };
        dim.Click += delegate { CloseFolder(); };
        Shown += delegate { OpenFolder(); };
    }

    int I(float v) { return (int)Math.Round(v * S); }
    static int Lerp(int a, int b, double e) { return (int)Math.Round(a + (b - a) * e); }

    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80; return cp; }
    }

    int PerPage { get { return D.Rows * D.Cols; } }
    int Pages { get { return Math.Max(1, (D.Items.Count + PerPage - 1) / PerPage); } }
    Rectangle TileRect() { return new Rectangle(origin.X - tile / 2, origin.Y - tile / 2, tile, tile); }

    Rectangle OpenRect()
    {
        Rectangle wa = Screen.FromPoint(origin).WorkingArea;
        int w = D.Cols * cellW + 2 * panelPad, h = topPad + D.Rows * cellH + dotsH;
        return new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
    }

    Rectangle SettingsRect(Size open) { return new Rectangle(open.Width - I(34), I(8), I(26), I(26)); }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void DrawIcon(Graphics g, Bitmap bmp, Rectangle dest, float alpha)
    {
        if (bmp == null) return;
        using (ImageAttributes ia = new ImageAttributes())
        {
            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = alpha;
            ia.SetColorMatrix(cm);
            g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
        }
    }

    static Color White(float alpha, int max) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(max * alpha))), 255, 255, 255); }

    void DrawText(Graphics g, string text, float px, FontStyle style, RectangleF r, float alpha, bool shadow, int lines)
    {
        using (Font f = new Font("Segoe UI", px * S, style, GraphicsUnit.Pixel))
        using (StringFormat sf = new StringFormat())
        {
            sf.Alignment = StringAlignment.Center;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = lines == 1 ? StringFormatFlags.NoWrap : StringFormatFlags.LineLimit;
            if (shadow)
                using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(170 * alpha), 0, 0, 0)))
                {
                    g.DrawString(text, f, sb, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
                    g.DrawString(text, f, sb, new RectangleF(r.X - 1, r.Y + 1, r.Width, r.Height), sf);
                    g.DrawString(text, f, sb, new RectangleF(r.X, r.Y + 2, r.Width, r.Height), sf);
                }
            using (SolidBrush b = new SolidBrush(White(alpha, 255))) g.DrawString(text, f, b, r, sf);
        }
    }

    void DrawOpen(Graphics g, float alpha, Size open)
    {
        List<Item> items = Order();
        List<Item> moving = dragOut ? Moving() : new List<Item>();
        int start = page * PerPage;
        for (int i = 0; i < PerPage && start + i < items.Count; i++)
        {
            Item it = items[start + i];
            int x = panelPad + (i % D.Cols) * cellW, y = topPad + (i / D.Cols) * cellH;
            bool held = moving.Contains(it);
            if (!held && sel.Contains(it))
                using (GraphicsPath path = Round(new RectangleF(x + I(6), y + I(1), cellW - I(12), cellH - I(4)), I(12)))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(70 * alpha), 120, 170, 255))) g.FillPath(b, path);
                    using (Pen pen = new Pen(Color.FromArgb((int)(200 * alpha), 150, 195, 255), 1.5f)) g.DrawPath(pen, path);
                }
            bool shell = held && dropSlot >= 0;
            if (shell)
                using (GraphicsPath path = Round(new RectangleF(x + I(8), y + I(1), cellW - I(16), cellH - I(6)), I(14)))
                {
                    using (SolidBrush b = new SolidBrush(White(alpha, 38))) g.FillPath(b, path);
                    using (Pen pen = new Pen(White(alpha, 190), 1.5f))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        g.DrawPath(pen, path);
                    }
                }
            DrawIcon(g, it.Icon, new Rectangle(x + (cellW - iconSz) / 2, y + I(6), iconSz, iconSz), shell ? alpha * 0.4f : held ? alpha * 0.25f : alpha);
            DrawText(g, it.Label, 12, FontStyle.Regular,
                new RectangleF(x + 3, y + I(8) + iconSz, cellW - 6, cellH - iconSz - I(12)), held ? alpha * 0.5f : alpha, false, 2);
        }
        if (banding)
        {
            Rectangle br = BandRect();
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(45 * alpha), 120, 170, 255))) g.FillRectangle(b, br);
            using (Pen pen = new Pen(Color.FromArgb((int)(220 * alpha), 150, 195, 255), 1f)) g.DrawRectangle(pen, br);
        }
        if (D.Items.Count == 0)
            DrawText(g, "Drag apps here", 13, FontStyle.Regular,
                new RectangleF(0, topPad + (D.Rows * cellH) / 2 - I(10), open.Width, I(24)), alpha * 0.7f, false, 1);

        if (Pages > 1)
            for (int p = 0; p < Pages; p++)
            {
                float cx = open.Width / 2f + (p - (Pages - 1) / 2f) * I(16), cy = open.Height - dotsH / 2f, r = I(3.5f);
                using (SolidBrush b = new SolidBrush(White(alpha, p == page ? 255 : 90)))
                    g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
            }

        Rectangle sr = SettingsRect(open);
        using (SolidBrush b = new SolidBrush(White(alpha, 45))) g.FillEllipse(b, sr);
        using (SolidBrush b = new SolidBrush(White(alpha, 230)))
            for (int k = -1; k <= 1; k++)
            {
                float r = I(2), cx = sr.X + sr.Width / 2f + k * I(6), cy = sr.Y + sr.Height / 2f;
                g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
            }
    }

    public void Render()
    {
        double e = 1 - Math.Pow(1 - t, 3);
        Rectangle a = TileRect(), b = OpenRect();
        Rectangle P = new Rectangle(Lerp(a.X, b.X, e), Lerp(a.Y, b.Y, e), Lerp(a.Width, b.Width, e), Lerp(a.Height, b.Height, e));
        Rectangle win = Rectangle.FromLTRB(P.Left - sidePad, P.Top - topExtra, P.Right + sidePad, P.Bottom + botExtra);
        curP = P;
        float a0 = (float)(1 - e), a1 = (float)e;

        using (Bitmap bmp = new Bitmap(win.Width, win.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.TranslateTransform(-win.X, -win.Y);

                using (GraphicsPath path = Round(P, Lerp((int)(tile * 0.22f), I(30), e)))
                {
                    Program.PaintBack(g, path, P, D, wp, (float)(D.Opacity / 100.0 * Math.Min(1, e * 2.5)));
                    if (a0 > 0.01f) DrawIcon(g, face, P, a0);
                    using (Pen pen = new Pen(Color.FromArgb((int)(70 * a1), 255, 255, 255), 1f)) g.DrawPath(pen, path);
                }

                if (a1 > 0.01f)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(P.X, P.Y);
                    g.ScaleTransform(P.Width / (float)b.Width, P.Height / (float)b.Height);
                    DrawOpen(g, a1, b.Size);
                    g.Restore(st);
                    DrawText(g, D.Name, 28, FontStyle.Bold, new RectangleF(win.X, P.Top - topExtra + I(6), win.Width, topExtra - I(8)), a1, true, 1);
                }
            }
            Push(bmp, win);
        }
    }

    void Push(Bitmap bmp, Rectangle win)
    {
        IntPtr screenDc = W.GetDC(IntPtr.Zero);
        IntPtr memDc = W.CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = W.SelectObject(memDc, hBmp);
        W.SIZE size; size.CX = win.Width; size.CY = win.Height;
        W.POINT src; src.X = 0; src.Y = 0;
        W.POINT dst; dst.X = win.X; dst.Y = win.Y;
        W.BLENDFUNCTION bf = new W.BLENDFUNCTION();
        bf.SourceConstantAlpha = 255;
        bf.AlphaFormat = 1;
        W.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);
        W.SelectObject(memDc, old);
        W.DeleteObject(hBmp);
        W.DeleteDC(memDc);
        W.ReleaseDC(IntPtr.Zero, screenDc);
    }

    void OpenFolder()
    {
        if (dir == 1 || t >= 1) return;
        page = 0;
        dim.Bounds = Screen.FromPoint(origin).Bounds;
        dim.Opacity = 0.01;
        dim.Show();
        BringToFront();
        Activate();
        Render();
        dir = 1;
        sw.Restart();
        anim.Start();
    }

    public void CloseFolder()
    {
        if (dir == -1) return;
        dir = -1;
        sw.Restart();
        anim.Start();
    }

    void AnimTick()
    {
        t += dir * sw.Elapsed.TotalMilliseconds / AnimMs;
        sw.Restart();
        if (t > 1) t = 1;
        if (t < 0) t = 0;
        Render();
        dim.Opacity = Math.Max(0.01, 0.45 * (1 - Math.Pow(1 - t, 3)));
        if (dir == 1 && t >= 1) { dir = 0; anim.Stop(); }
        else if (dir == -1 && t <= 0)
        {
            dir = 0; anim.Stop();
            dim.Hide();
            Application.Exit();
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (t > 0 && dir >= 0 && !menuOpen && !dialogOpen)
            BeginInvoke(new MethodInvoker(delegate
            {
                if (Form.ActiveForm == null && !menuOpen && !dialogOpen) CloseFolder();
            }));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (dir != 0 || t < 1) return;
        OpenClick(e.Button, Cursor.Position);
    }

    bool Ctrl { get { return (ModifierKeys & Keys.Control) != 0; } }

    Rectangle BandRect()
    {
        return Rectangle.FromLTRB(Math.Min(bandA.X, bandB.X), Math.Min(bandA.Y, bandB.Y), Math.Max(bandA.X, bandB.X), Math.Max(bandA.Y, bandB.Y));
    }

    void StartBand(Point p)
    {
        banding = true;
        bandA = bandB = p;
        if (!Ctrl) sel.Clear();
        bandBase = new List<Item>(sel);
        Render();
    }

    void UpdateBand()
    {
        Rectangle br = BandRect();
        sel.Clear();
        sel.AddRange(bandBase);
        int start = page * PerPage;
        for (int i = 0; i < PerPage && start + i < D.Items.Count; i++)
        {
            Rectangle cell = new Rectangle(panelPad + (i % D.Cols) * cellW + I(10), topPad + (i / D.Cols) * cellH + I(4), cellW - I(20), cellH - I(10));
            Item it = D.Items[start + i];
            if (cell.IntersectsWith(br) && !sel.Contains(it)) sel.Add(it);
        }
        Render();
    }

    List<Item> Moving()
    {
        List<Item> l = new List<Item>();
        if (pressItem == null) return l;
        if (!sel.Contains(pressItem)) { l.Add(pressItem); return l; }
        foreach (Item it in D.Items) if (sel.Contains(it)) l.Add(it);
        return l;
    }

    void OpenClick(MouseButtons button, Point sp)
    {
        Size open = curP.Size;
        Point p = new Point(sp.X - curP.X, sp.Y - curP.Y);
        if (SettingsRect(open).Contains(p)) { ShowMenu(); return; }

        if (Pages > 1 && p.Y >= open.Height - dotsH)
        {
            float first = open.Width / 2f - (Pages - 1) / 2f * I(16);
            int pg = (int)Math.Round((p.X - first) / I(16));
            if (pg >= 0 && pg < Pages) { page = pg; Render(); }
            return;
        }

        int gx = p.X - panelPad, gy = p.Y - topPad;
        int idx = -1;
        if (gx >= 0 && gy >= 0 && gx < D.Cols * cellW && gy < D.Rows * cellH)
            idx = page * PerPage + (gy / cellH) * D.Cols + gx / cellW;
        if (idx < 0 || idx >= D.Items.Count)
        {
            if (button == MouseButtons.Right) ShowMenu();
            else if (button == MouseButtons.Left) StartBand(p);
            return;
        }

        Item it = D.Items[idx];
        if (button == MouseButtons.Left)
        {
            if (Ctrl)
            {
                if (sel.Contains(it)) sel.Remove(it); else sel.Add(it);
                Render();
                return;
            }
            if (!sel.Contains(it) && sel.Count > 0) { sel.Clear(); Render(); }
            pressItem = it; pressAt = sp; dragOut = false;
        }
        else if (button == MouseButtons.Right)
        {
            ContextMenuStrip m = NewMenu();
            if (sel.Contains(it) && sel.Count > 1)
            {
                List<Item> group = new List<Item>();
                foreach (Item x in D.Items) if (sel.Contains(x)) group.Add(x);
                m.Items.Add("Open " + group.Count + " items", null, delegate { OpenAll(group); });
                m.Items.Add("Remove " + group.Count + " items from folder", null, delegate { RemoveItems(group); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Delete " + group.Count + " items (Recycle Bin)", null, delegate { DeleteItems(group); });
            }
            else
            {
                m.Items.Add("Open", null, delegate { Launch(it); });
                m.Items.Add("Remove from folder", null, delegate { RemoveItem(it); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Delete (Recycle Bin)", null, delegate { DeleteItem(it); });
            }
            m.Show(Cursor.Position);
        }
    }

    List<Item> Order()
    {
        if (dragOut && pressItem != null && dropSlot >= 0)
        {
            List<Item> mv = Moving();
            List<Item> l = new List<Item>(D.Items);
            foreach (Item it in mv) l.Remove(it);
            l.InsertRange(Math.Min(dropSlot, l.Count), mv);
            return l;
        }
        return D.Items;
    }

    void DragUpdate()
    {
        if (!dragOut || pressItem == null) return;
        Point sp = Cursor.Position;
        Size open = curP.Size;
        Point p = new Point(sp.X - curP.X, sp.Y - curP.Y);
        DateTime now = DateTime.Now;

        int dot = -1;
        if (Pages > 1 && p.Y >= open.Height - dotsH && p.Y < open.Height + I(8))
        {
            float first = open.Width / 2f - (Pages - 1) / 2f * I(16);
            int pg = (int)Math.Round((p.X - first) / I(16));
            if (pg >= 0 && pg < Pages && Math.Abs(p.X - (first + pg * I(16))) <= I(8)) dot = pg;
        }
        if (dot != hoverDot) { hoverDot = dot; dotSince = now; }
        if (dot >= 0 && dot != page && (now - dotSince).TotalMilliseconds >= 350)
        {
            page = dot;
            hoverSlot = -1;
            dropSlot = -1;
            Render();
        }

        int slot = -1;
        int gx = p.X - panelPad, gy = p.Y - topPad;
        if (dot < 0 && gx >= 0 && gy >= 0 && gx < D.Cols * cellW && gy < D.Rows * cellH)
        {
            slot = page * PerPage + (gy / cellH) * D.Cols + gx / cellW;
            if (slot > D.Items.Count - 1) slot = D.Items.Count - 1;
        }
        if (slot != hoverSlot)
        {
            hoverSlot = slot;
            hoverSince = now;
            if (dropSlot != -1) { dropSlot = -1; Render(); }
        }
        else if (slot >= 0 && dropSlot != slot && (now - hoverSince).TotalMilliseconds >= Program.HoverMs)
        {
            dropSlot = slot;
            Render();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (banding)
        {
            Point c = Cursor.Position;
            bandB = new Point(Math.Max(0, Math.Min(curP.Width, c.X - curP.X)), Math.Max(0, Math.Min(curP.Height, c.Y - curP.Y)));
            UpdateBand();
            return;
        }
        if (pressItem == null) return;
        Point sp = Cursor.Position;
        if (!dragOut)
        {
            if (Math.Abs(sp.X - pressAt.X) + Math.Abs(sp.Y - pressAt.Y) < I(8)) return;
            dragOut = true;
            hoverSlot = -1; dropSlot = -1; hoverDot = -1;
            ghost.BackgroundImage = pressItem.Icon;
            ghost.Size = new Size(iconSz, iconSz);
            ghost.Location = new Point(sp.X - iconSz / 2, sp.Y - iconSz / 2);
            ghost.Show();
            dragTimer.Start();
            Render();
        }
        ghost.Location = new Point(sp.X - iconSz / 2, sp.Y - iconSz / 2);
        DragUpdate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (banding)
        {
            banding = false;
            Render();
            return;
        }
        if (pressItem == null) return;
        Item it = pressItem;
        List<Item> moving = Moving();
        bool dragged = dragOut;
        int slot = dropSlot;
        pressItem = null;
        dragOut = false;
        hoverSlot = -1; dropSlot = -1; hoverDot = -1;
        dragTimer.Stop();
        ghost.Hide();
        if (!dragged) { sel.Clear(); Launch(it); return; }
        if (!curP.Contains(Cursor.Position)) RemoveItems(moving);
        else if (slot >= 0)
        {
            foreach (Item m in moving) D.Items.Remove(m);
            D.Items.InsertRange(Math.Min(slot, D.Items.Count), moving);
            ChangedIcon();
        }
        else Render();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (t >= 1) SetPage(page + (e.Delta < 0 ? 1 : -1));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (t < 1) return;
        if (e.KeyCode == Keys.Escape)
        {
            if (sel.Count > 0) { sel.Clear(); Render(); }
            else CloseFolder();
        }
        else if (e.KeyCode == Keys.A && e.Control)
        {
            sel.Clear();
            sel.AddRange(D.Items);
            Render();
        }
        else if (e.KeyCode == Keys.Delete && sel.Count > 0)
        {
            List<Item> group = new List<Item>();
            foreach (Item x in D.Items) if (sel.Contains(x)) group.Add(x);
            DeleteItems(group);
        }
        else if (e.KeyCode == Keys.Right) SetPage(page + 1);
        else if (e.KeyCode == Keys.Left) SetPage(page - 1);
    }

    void SetPage(int p)
    {
        p = Math.Max(0, Math.Min(Pages - 1, p));
        if (p != page) { page = p; Render(); }
    }

    void Launch(Item it)
    {
        try { Process.Start(it.Path); }
        catch (Exception ex)
        {
            dialogOpen = true;
            MessageBox.Show(this, "Could not open \"" + it.Label + "\".\n" + ex.Message, "DeskFolders");
            dialogOpen = false;
            return;
        }
        CloseFolder();
    }

    void DeleteItem(Item it)
    {
        dialogOpen = true;
        DialogResult r = MessageBox.Show(this, "Move \"" + it.Label + "\" to the Recycle Bin?\n\n" + it.Path,
            "DeskFolders", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        dialogOpen = false;
        Activate();
        if (r != DialogResult.Yes) return;
        if (File.Exists(it.Path) || Directory.Exists(it.Path))
        {
            Program.Recycle(it.Path);
            if (File.Exists(it.Path) || Directory.Exists(it.Path))
            {
                dialogOpen = true;
                MessageBox.Show(this, "Could not delete \"" + it.Label + "\". It may be open or protected.", "DeskFolders");
                dialogOpen = false;
                Activate();
                return;
            }
        }
        D.Items.Remove(it);
        sel.Remove(it);
        ChangedIcon();
    }

    void RemoveItem(Item it)
    {
        D.Items.Remove(it);
        sel.Remove(it);
        Program.Restore(D, it.Path);
        ChangedIcon();
    }

    void RemoveItems(List<Item> items)
    {
        foreach (Item it in items)
        {
            D.Items.Remove(it);
            sel.Remove(it);
            Program.Restore(D, it.Path);
        }
        ChangedIcon();
    }

    void OpenAll(List<Item> items)
    {
        foreach (Item it in items)
            try { Process.Start(it.Path); } catch { }
        sel.Clear();
        CloseFolder();
    }

    void DeleteItems(List<Item> items)
    {
        if (items.Count == 0) return;
        if (items.Count == 1) { DeleteItem(items[0]); return; }
        string names = "";
        for (int i = 0; i < items.Count && i < 8; i++) names += "\n" + items[i].Label;
        if (items.Count > 8) names += "\n...";
        dialogOpen = true;
        DialogResult r = MessageBox.Show(this, "Move " + items.Count + " items to the Recycle Bin?\n" + names,
            "DeskFolders", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        dialogOpen = false;
        Activate();
        if (r != DialogResult.Yes) return;
        int failed = 0;
        foreach (Item it in items)
        {
            if (File.Exists(it.Path) || Directory.Exists(it.Path)) Program.Recycle(it.Path);
            if (File.Exists(it.Path) || Directory.Exists(it.Path)) { failed++; continue; }
            D.Items.Remove(it);
            sel.Remove(it);
        }
        ChangedIcon();
        if (failed > 0)
        {
            dialogOpen = true;
            MessageBox.Show(this, failed + " item(s) could not be deleted. They may be open or protected.", "DeskFolders");
            dialogOpen = false;
            Activate();
        }
    }

    public void RebuildFace(bool write)
    {
        if (face != null) face.Dispose();
        face = Program.MakeFace(D, wp, 256);
        if (write) Program.UpdateIcon(D, face);
    }

    void ChangedIcon()
    {
        RebuildFace(true);
        Changed();
    }

    void SetWallpaper(string src)
    {
        try
        {
            Program.DeleteIfOwned(D.Wallpaper);
            D.Wallpaper = "";
            if (wp != null) { wp.Dispose(); wp = null; }
            if (src != null)
            {
                string dest = Path.Combine(Path.Combine(Program.Dir, D.Id), "wallpaper_" + DateTime.Now.Ticks + Path.GetExtension(src));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(src, dest);
                D.Wallpaper = dest;
                wp = Program.LoadWallpaper(D);
            }
        }
        catch { }
        ChangedIcon();
    }

    void ChooseWallpaper()
    {
        dialogOpen = true;
        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "Wallpaper for " + D.Name;
            dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
            if (dlg.ShowDialog(this) == DialogResult.OK) SetWallpaper(dlg.FileName);
        }
        dialogOpen = false;
        Activate();
    }

    void SetColor(int rgb)
    {
        D.Rgb = rgb & 0xFFFFFF;
        SetWallpaper(null);
    }

    void CustomColor()
    {
        dialogOpen = true;
        using (ColorDialog dlg = new ColorDialog())
        {
            dlg.FullOpen = true;
            dlg.Color = Color.FromArgb(255, Color.FromArgb(D.Rgb));
            if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(dlg.Color.ToArgb());
        }
        dialogOpen = false;
        Activate();
    }

    void Changed()
    {
        page = Math.Max(0, Math.Min(Pages - 1, page));
        Program.Save(D);
        Render();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null) { Program.AddPaths(D, files); ChangedIcon(); }
    }

    ContextMenuStrip NewMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        menuOpen = true;
        m.Closed += delegate { menuOpen = false; };
        return m;
    }

    void ShowMenu()
    {
        ContextMenuStrip m = NewMenu();
        ToolStripMenuItem rows = new ToolStripMenuItem("Rows");
        for (int n = 1; n <= MaxRows; n++)
        {
            int v = n;
            ToolStripMenuItem mi = new ToolStripMenuItem(v.ToString());
            mi.Checked = v == D.Rows;
            mi.Click += delegate { D.Rows = v; Changed(); };
            rows.DropDownItems.Add(mi);
        }
        ToolStripMenuItem cols = new ToolStripMenuItem("Columns");
        for (int n = 1; n <= MaxCols; n++)
        {
            int v = n;
            ToolStripMenuItem mi = new ToolStripMenuItem(v.ToString());
            mi.Checked = v == D.Cols;
            mi.Click += delegate { D.Cols = v; Changed(); };
            cols.DropDownItems.Add(mi);
        }
        ToolStripMenuItem wall = new ToolStripMenuItem("Wallpaper");
        wall.DropDownItems.Add("Choose image...", null, delegate { ChooseWallpaper(); });
        wall.DropDownItems.Add("Remove", null, delegate { SetWallpaper(null); });

        ToolStripMenuItem color = new ToolStripMenuItem("Color");
        string[] names = { "Graphite", "Black", "Blue", "Teal", "Green", "Orange", "Red", "Pink", "Purple" };
        int[] values = { 0x32343A, 0x101012, 0x285AAA, 0x1E828C, 0x28825A, 0xC86E1E, 0xAA3237, 0xC85082, 0x6E3CA0 };
        for (int n = 0; n < names.Length; n++)
        {
            int v = values[n];
            Bitmap sw16 = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(sw16)) g.Clear(Color.FromArgb(255, Color.FromArgb(v)));
            ToolStripMenuItem mi = new ToolStripMenuItem(names[n], sw16);
            mi.Click += delegate { SetColor(v); };
            color.DropDownItems.Add(mi);
        }
        color.DropDownItems.Add("Custom...", null, delegate { CustomColor(); });

        ToolStripMenuItem opacity = new ToolStripMenuItem("Opacity");
        TrackBar tb = new TrackBar();
        tb.AutoSize = false;
        tb.Size = new Size(I(170), I(30));
        tb.Minimum = 15; tb.Maximum = 100;
        tb.TickStyle = TickStyle.None;
        tb.Value = Math.Max(15, Math.Min(100, D.Opacity));
        tb.ValueChanged += delegate { D.Opacity = tb.Value; Render(); };
        opacity.DropDownItems.Add(new ToolStripControlHost(tb));

        ToolStripMenuItem look = new ToolStripMenuItem("Appearance");
        look.DropDownItems.Add(wall);
        look.DropDownItems.Add(color);
        look.DropDownItems.Add(opacity);

        m.Closed += delegate { Program.Save(D); };
        m.Items.Add(rows);
        m.Items.Add(cols);
        m.Items.Add(look);
        ToolStripMenuItem delay = new ToolStripMenuItem("Move delay");
        int[] delays = { 0, 200, 300, 500, 750, 1000, 1500, 2000 };
        foreach (int ms in delays)
        {
            int v = ms;
            ToolStripMenuItem mi = new ToolStripMenuItem(v == 0 ? "Instant" : (v / 1000.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s");
            mi.Checked = v == Program.HoverMs;
            mi.Click += delegate { Program.SetHoverMs(v); };
            delay.DropDownItems.Add(mi);
        }
        m.Items.Add(delay);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Rename...", null, delegate { Rename(); });
        m.Items.Add("Add apps or files...", null, delegate { AddDialog(); });
        ToolStripMenuItem store = new ToolStripMenuItem("Storage location");
        ToolStripMenuItem current = new ToolStripMenuItem(Program.Root);
        current.Enabled = false;
        store.DropDownItems.Add(current);
        store.DropDownItems.Add("Choose folder...", null, delegate { ChooseRoot(); });
        store.DropDownItems.Add("Use default (Documents)", null, delegate { ApplyRoot(null); });
        m.Items.Add(store);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("New folder...", null, delegate { NewFolder(); });
        m.Items.Add("Delete this folder", null, delegate { DeleteMe(); });
        m.Items.Add("Uninstall DeskFolders...", null, delegate { Uninstall(); });
        m.Show(Cursor.Position);
    }

    void Rename()
    {
        dialogOpen = true;
        string name = Program.Prompt("Folder name", D.Name);
        if (name != null) { Program.Rename(D, name); Changed(); }
        dialogOpen = false;
        Activate();
    }

    void NewFolder()
    {
        dialogOpen = true;
        string name = Program.Prompt("New folder name", "Folder");
        if (name != null) Program.Create(name);
        dialogOpen = false;
        Activate();
    }

    void ChooseRoot()
    {
        dialogOpen = true;
        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Where DeskFolders keeps the items you put in folders";
            dlg.SelectedPath = Program.Root;
            if (dlg.ShowDialog(this) == DialogResult.OK) ApplyRoot(dlg.SelectedPath);
        }
        dialogOpen = false;
        Activate();
    }

    void ApplyRoot(string path)
    {
        Program.SetRoot(path);
        Program.SyncStorage(D);
        Changed();
    }

    void AddDialog()
    {
        dialogOpen = true;
        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "Add to " + D.Name;
            dlg.Multiselect = true;
            dlg.DereferenceLinks = false;
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (dlg.ShowDialog(this) == DialogResult.OK) { Program.AddPaths(D, dlg.FileNames); ChangedIcon(); }
        }
        dialogOpen = false;
        Activate();
    }

    void Uninstall()
    {
        try { Process.Start(Application.ExecutablePath, "--uninstall"); } catch { return; }
        anim.Stop();
        dim.Hide();
        Application.Exit();
    }

    void DeleteMe()
    {
        dialogOpen = true;
        DialogResult r = MessageBox.Show(this, "Delete the folder \"" + D.Name + "\"?\nEverything in it goes back to the desktop.",
            "DeskFolders", MessageBoxButtons.YesNo);
        if (r != DialogResult.Yes) { dialogOpen = false; Activate(); return; }
        anim.Stop();
        dim.Hide();
        Program.Delete(D);
        Application.Exit();
    }
}

static class Program
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFolders");
    static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    static readonly string PublicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    public static readonly string DefaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeskFolders");
    public static string Root = LoadRoot();

    static string LoadRoot()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\DeskFolders"))
            {
                string v = k == null ? null : k.GetValue("Root") as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return DefaultRoot;
    }

    public static int HoverMs = LoadHoverMs();

    static int LoadHoverMs()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\DeskFolders"))
            {
                object v = k == null ? null : k.GetValue("HoverMs");
                if (v is int) return Math.Max(0, Math.Min(5000, (int)v));
            }
        }
        catch { }
        return 500;
    }

    public static void SetHoverMs(int ms)
    {
        HoverMs = ms;
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\DeskFolders"))
                k.SetValue("HoverMs", ms);
        }
        catch { }
    }

    public static void SetRoot(string path)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\DeskFolders"))
            {
                if (path == null) k.DeleteValue("Root", false);
                else k.SetValue("Root", path);
            }
            Root = path ?? DefaultRoot;
        }
        catch { }
    }

    public static void Recycle(string path)
    {
        try
        {
            W.SHFILEOPSTRUCT op = new W.SHFILEOPSTRUCT();
            op.wFunc = 3;
            op.pFrom = path + "\0\0";
            op.fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400;
            W.SHFileOperation(ref op);
        }
        catch { }
    }

    static void ElevatedDelete(List<string> files)
    {
        if (files.Count == 0) return;
        try
        {
            StringBuilder sb = new StringBuilder("/c del /f /q");
            foreach (string f in files) sb.Append(" \"" + f + "\"");
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", sb.ToString());
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process p = Process.Start(psi);
            if (p != null) p.WaitForExit(15000);
        }
        catch { }
        foreach (string f in files)
            if (File.Exists(f)) LastError = "Could not remove \"" + Path.GetFileName(f) + "\" from the desktop. It is in the folder; delete the desktop one by hand.";
    }
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskFolders";

    public static readonly Icon AppIcon = LoadAppIcon();

    static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return SystemIcons.Application; }
    }

    public static Bitmap LoadWallpaper(FolderData d)
    {
        try
        {
            if (d.Wallpaper.Length == 0 || !File.Exists(d.Wallpaper)) return null;
            using (Image im = Image.FromFile(d.Wallpaper))
            {
                double k = Math.Min(1.0, 1000.0 / Math.Max(im.Width, im.Height));
                return new Bitmap(im, new Size(Math.Max(1, (int)(im.Width * k)), Math.Max(1, (int)(im.Height * k))));
            }
        }
        catch { return null; }
    }

    public static void PaintBack(Graphics g, GraphicsPath path, Rectangle r, FolderData d, Bitmap wp, float alpha)
    {
        alpha = Math.Max(0f, Math.Min(1f, alpha));
        if (wp != null)
        {
            Region old = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            float ar = r.Width / (float)Math.Max(1, r.Height), sw = wp.Width, sh = wp.Height;
            if (sw / sh > ar) sw = sh * ar; else sh = sw / ar;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = alpha;
                ia.SetColorMatrix(cm);
                g.DrawImage(wp, r, (wp.Width - sw) / 2, (wp.Height - sh) / 2, sw, sh, GraphicsUnit.Pixel, ia);
            }
            g.Clip = old;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(70 * alpha), 0, 0, 0))) g.FillPath(sb, path);
        }
        else
            using (SolidBrush br = new SolidBrush(Color.FromArgb((int)(255 * alpha), Color.FromArgb(d.Rgb)))) g.FillPath(br, path);
    }

    public static Bitmap MakeFace(FolderData d, Bitmap wp, int size)
    {
        Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle r = new Rectangle(0, 0, size, size);
            using (GraphicsPath path = FolderForm.Round(r, size * 0.22f)) PaintBack(g, path, r, d, wp, 1f);
            int m = (int)(size * 0.14f), gap = (int)(size * 0.05f), sz = (size - 2 * m - 2 * gap) / 3;
            for (int i = 0; i < d.Items.Count && i < 9; i++)
                if (d.Items[i].Icon != null)
                    g.DrawImage(d.Items[i].Icon, new Rectangle(m + (i % 3) * (sz + gap), m + (i / 3) * (sz + gap), sz, sz));
        }
        return b;
    }

    static void WriteIco(Bitmap face, string path)
    {
        int[] sizes = { 256, 64, 48, 32, 16 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int sz in sizes)
            using (Bitmap b = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(face, new Rectangle(0, 0, sz, sz));
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
    }

    public static void UpdateIcon(FolderData d, Bitmap face)
    {
        try
        {
            string dir = FolderDir(d.Id);
            Directory.CreateDirectory(dir);
            foreach (string old in Directory.GetFiles(dir, "icon_*.ico"))
                try { File.Delete(old); } catch { }
            string ico = Path.Combine(dir, "icon_" + DateTime.Now.Ticks + ".ico");
            WriteIco(face, ico);
            if (d.Lnk.Length > 0)
            {
                WriteShortcut(d.Lnk, d.Id, ico);
                W.SHChangeNotify(0x2000, 0x0005, d.Lnk, IntPtr.Zero);
            }
        }
        catch { }
    }

    public static bool HasIcon(FolderData d)
    {
        try { return Directory.GetFiles(FolderDir(d.Id), "icon_*.ico").Length > 0; }
        catch { return false; }
    }

    public static Item MakeItem(string path)
    {
        Item it = new Item();
        it.Path = path;
        string name = Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(path);
        it.Label = string.IsNullOrEmpty(name) ? path : name;
        it.Icon = LoadIcon(path);
        return it;
    }

    static Bitmap Square(Image src, int size)
    {
        Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float k = Math.Min(size / (float)src.Width, size / (float)src.Height);
            float w = src.Width * k, h = src.Height * k;
            g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
        }
        return b;
    }

    static Bitmap ImageThumb(string path)
    {
        try
        {
            using (Image im = Image.FromFile(path))
            {
                try
                {
                    if (Array.IndexOf(im.PropertyIdList, 0x0112) >= 0)
                    {
                        int o = im.GetPropertyItem(0x0112).Value[0];
                        if (o == 3) im.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        else if (o == 6) im.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        else if (o == 8) im.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    }
                }
                catch { }
                return Square(im, 128);
            }
        }
        catch { return null; }
    }

    static Bitmap ShellThumb(string path)
    {
        try
        {
            W.IShellItemImageFactory f;
            W.SHCreateItemFromParsingName(path, IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
            W.SIZE sz; sz.CX = 128; sz.CY = 128;
            IntPtr hbm;
            int hr = f.GetImage(sz, 0x8, out hbm);
            Marshal.ReleaseComObject(f);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try { using (Bitmap raw = Image.FromHbitmap(hbm)) return Square(raw, 128); }
            finally { W.DeleteObject(hbm); }
        }
        catch { return null; }
    }

    static readonly string[] ImageExts = { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff" };
    static readonly string[] MediaExts = { ".webp", ".heic", ".heif", ".avif", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg" };

    static Bitmap LoadIcon(string path)
    {
        string ext = "";
        try { ext = Path.GetExtension(path).ToLowerInvariant(); } catch { }
        bool image = Array.IndexOf(ImageExts, ext) >= 0;
        if (image)
        {
            Bitmap t = ImageThumb(path);
            if (t != null) return t;
        }
        if (image || Array.IndexOf(MediaExts, ext) >= 0)
        {
            Bitmap t = ShellThumb(path);
            if (t != null) return t;
        }
        try
        {
            W.SHFILEINFO fi = new W.SHFILEINFO();
            IntPtr r = W.SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(typeof(W.SHFILEINFO)), 0x4000);
            if (r != IntPtr.Zero)
            {
                Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IntPtr list;
                if (W.SHGetImageList(2, ref iid, out list) == 0 && list != IntPtr.Zero)
                {
                    IntPtr hIcon = W.ImageList_GetIcon(list, fi.iIcon, 1);
                    if (hIcon != IntPtr.Zero)
                    {
                        Bitmap b;
                        using (Icon ic = Icon.FromHandle(hIcon)) b = ic.ToBitmap();
                        W.DestroyIcon(hIcon);
                        return b;
                    }
                }
            }
        }
        catch { }
        Bitmap si = ShellIcon(path);
        if (si != null) return si;
        try { using (Icon ic = Icon.ExtractAssociatedIcon(path)) return ic.ToBitmap(); } catch { }
        try
        {
            W.SHFILEINFO fi = new W.SHFILEINFO();
            uint attr = Directory.Exists(path) ? 0x10u : 0x80u;
            if (W.SHGetFileInfo(path, attr, ref fi, (uint)Marshal.SizeOf(typeof(W.SHFILEINFO)), 0x100 | 0x10) != IntPtr.Zero && fi.hIcon != IntPtr.Zero)
            {
                Bitmap b;
                using (Icon ic = Icon.FromHandle(fi.hIcon)) b = ic.ToBitmap();
                W.DestroyIcon(fi.hIcon);
                return b;
            }
        }
        catch { }
        return SystemIcons.Application.ToBitmap();
    }

    static Bitmap ShellIcon(string path)
    {
        try
        {
            W.IShellItemImageFactory f;
            W.SHCreateItemFromParsingName(path, IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
            W.SIZE sz; sz.CX = 96; sz.CY = 96;
            IntPtr hbm;
            int hr = f.GetImage(sz, 0x4, out hbm);
            Marshal.ReleaseComObject(f);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try { return AlphaBitmap(hbm); }
            finally { W.DeleteObject(hbm); }
        }
        catch { return null; }
    }

    static Bitmap AlphaBitmap(IntPtr hbm)
    {
        using (Bitmap src = Image.FromHbitmap(hbm))
        {
            Rectangle r = new Rectangle(0, 0, src.Width, src.Height);
            BitmapData bd = src.LockBits(r, ImageLockMode.ReadOnly, src.PixelFormat);
            try
            {
                if (Image.GetPixelFormatSize(src.PixelFormat) != 32) return new Bitmap(src);
                byte[] bytes = new byte[Math.Abs(bd.Stride) * bd.Height];
                Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length);
                bool alpha = false;
                for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { alpha = true; break; }
                if (!alpha) return new Bitmap(src);
                Bitmap result = new Bitmap(bd.Width, bd.Height, PixelFormat.Format32bppArgb);
                using (Bitmap wrap = new Bitmap(bd.Width, bd.Height, bd.Stride, PixelFormat.Format32bppPArgb, bd.Scan0))
                using (Graphics g = Graphics.FromImage(result))
                    g.DrawImage(wrap, 0, 0, bd.Width, bd.Height);
                return result;
            }
            finally { src.UnlockBits(bd); }
        }
    }

    static string FolderDir(string id) { return Path.Combine(Dir, id); }

    public static void DeleteIfOwned(string path)
    {
        try
        {
            if (path.StartsWith(Dir + "\\", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    static bool Under(string path, string dir)
    {
        return dir.Length > 0 && path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    static string Free(string dir, string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        string p = Path.Combine(dir, fileName);
        for (int n = 2; File.Exists(p) || Directory.Exists(p); n++) p = Path.Combine(dir, name + " (" + n + ")" + ext);
        return p;
    }

    static void MoveAny(string src, string dest)
    {
        if (Directory.Exists(src)) Directory.Move(src, dest);
        else File.Move(src, dest);
    }

    static bool Owned(FolderData d, string path)
    {
        return Under(path, d.Store) || Under(path, FolderDir(d.Id));
    }

    public static void Restore(FolderData d, string path)
    {
        try
        {
            if (!Owned(d, path)) return;
            if (Directory.Exists(path))
            {
                Directory.Move(path, Free(Desktop, Path.GetFileName(path)));
                return;
            }
            if (!File.Exists(path)) return;
            string same = Path.Combine(Desktop, Path.GetFileName(path));
            if (File.Exists(same) && new FileInfo(same).Length == new FileInfo(path).Length) File.Delete(path);
            else File.Move(path, Free(Desktop, Path.GetFileName(path)));
        }
        catch { }
    }

    public static void SyncStorage(FolderData d)
    {
        try
        {
            string want = Path.Combine(Root, Clean(d.Name));
            if (d.Store.Length > 0 && Directory.Exists(d.Store) && !string.Equals(d.Store, want, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(want)) want = d.Store;
                else
                {
                    Directory.CreateDirectory(Root);
                    Directory.Move(d.Store, want);
                    foreach (Item it in d.Items)
                        if (Under(it.Path, d.Store)) it.Path = want + it.Path.Substring(d.Store.TrimEnd('\\').Length);
                }
            }
            Directory.CreateDirectory(want);
            d.Store = want;
        }
        catch { }
    }

    static string Long(string p)
    {
        try
        {
            StringBuilder sb = new StringBuilder(2048);
            int n = W.GetLongPathName(p, sb, sb.Capacity);
            if (n > 0 && n < sb.Capacity) return sb.ToString();
        }
        catch { }
        return p;
    }

    public static string LastError;

    public static void AddPaths(FolderData d, string[] paths)
    {
        List<string> locked = new List<string>();
        string desk = Long(Desktop), pub = Long(PublicDesktop);
        SyncStorage(d);
        foreach (string raw in paths)
        {
            try
            {
                string p = Long(Path.GetFullPath(raw).TrimEnd('\\'));
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                if (string.Equals(p, d.Lnk, StringComparison.OrdinalIgnoreCase)) continue;
                if (Owned(d, p)) continue;
                string stored = p;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                string parent = Path.GetDirectoryName(p) ?? "";
                bool onDesktop = string.Equals(parent, desk, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parent, pub, StringComparison.OrdinalIgnoreCase);
                bool link = ext == ".lnk" || ext == ".url";
                if ((onDesktop || link) && File.Exists(p))
                {
                    try
                    {
                        Directory.CreateDirectory(d.Store);
                        string dest = Free(d.Store, Path.GetFileName(p));
                        File.Copy(p, dest);
                        stored = dest;
                        if (onDesktop && new FileInfo(dest).Length == new FileInfo(p).Length)
                        {
                            Recycle(p);
                            if (File.Exists(p))
                                try { File.Delete(p); } catch { }
                            if (File.Exists(p)) locked.Add(p);
                        }
                    }
                    catch (Exception ex) { LastError = ex.Message; }
                }
                else if (onDesktop && Directory.Exists(p))
                {
                    try
                    {
                        Directory.CreateDirectory(d.Store);
                        string dest = Free(d.Store, Path.GetFileName(p));
                        Directory.Move(p, dest);
                        stored = dest;
                    }
                    catch (Exception ex) { LastError = ex.Message; }
                }
                d.Items.Add(MakeItem(stored));
            }
            catch (Exception ex) { LastError = ex.Message; }
        }
        ElevatedDelete(locked);
    }

    public static void Save(FolderData d)
    {
        try
        {
            Directory.CreateDirectory(FolderDir(d.Id));
            List<string> lines = new List<string>();
            lines.Add("S|" + d.Rows + "|" + d.Cols);
            lines.Add("N|" + d.Name);
            lines.Add("L|" + d.Lnk);
            lines.Add("D|" + d.Store);
            lines.Add("C|" + d.Rgb + "|" + d.Opacity);
            lines.Add("P|" + d.Wallpaper);
            foreach (Item it in d.Items) lines.Add("I|" + it.Path);
            File.WriteAllLines(Path.Combine(FolderDir(d.Id), "folder.txt"), lines.ToArray());
        }
        catch { }
    }

    static FolderData Load(string id)
    {
        string file = Path.Combine(FolderDir(id), "folder.txt");
        if (!File.Exists(file)) return null;
        FolderData d = new FolderData();
        d.Id = id;
        foreach (string line in File.ReadAllLines(file))
        {
            try
            {
                if (line.StartsWith("S|"))
                {
                    string[] p = line.Split('|');
                    d.Rows = Math.Max(1, int.Parse(p[1]));
                    d.Cols = Math.Max(1, int.Parse(p[2]));
                }
                else if (line.StartsWith("N|")) d.Name = line.Substring(2);
                else if (line.StartsWith("L|")) d.Lnk = line.Substring(2);
                else if (line.StartsWith("P|")) d.Wallpaper = line.Substring(2);
                else if (line.StartsWith("D|")) d.Store = line.Substring(2);
                else if (line.StartsWith("C|"))
                {
                    string[] p = line.Split('|');
                    d.Rgb = int.Parse(p[1]);
                    d.Opacity = Math.Max(15, Math.Min(100, int.Parse(p[2])));
                }
                else if (line.StartsWith("I|")) d.Items.Add(MakeItem(line.Substring(2)));
            }
            catch { }
        }
        return d;
    }

    static object Shortcut(string lnk, out Type type)
    {
        Type st = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(st);
        object sc = st.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        type = sc.GetType();
        return sc;
    }

    static void WriteShortcut(string lnk, string id, string icon)
    {
        Type t;
        object sc = Shortcut(lnk, out t);
        string exe = Application.ExecutablePath;
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
        t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { "--open " + id });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icon + ",0" });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    static string ShortcutArgs(string lnk)
    {
        try
        {
            Type t;
            object sc = Shortcut(lnk, out t);
            return (string)t.InvokeMember("Arguments", BindingFlags.GetProperty, null, sc, null);
        }
        catch { return ""; }
    }

    static void Resolve(FolderData d)
    {
        if (d.Lnk.Length > 0 && File.Exists(d.Lnk)) return;
        try
        {
            foreach (string f in Directory.GetFiles(Desktop, "*.lnk"))
                if (ShortcutArgs(f).IndexOf(d.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    d.Lnk = f;
                    d.Name = Path.GetFileNameWithoutExtension(f);
                    Save(d);
                    return;
                }
        }
        catch { }
    }

    static string Clean(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        name = name.Trim();
        return name.Length == 0 ? "Folder" : name;
    }

    static string FreeLnk(string name)
    {
        string p = Path.Combine(Desktop, name + ".lnk");
        for (int n = 2; File.Exists(p); n++) p = Path.Combine(Desktop, name + " (" + n + ").lnk");
        return p;
    }

    public static void Create(string name)
    {
        try
        {
            FolderData d = new FolderData();
            d.Id = Guid.NewGuid().ToString("N");
            d.Lnk = FreeLnk(Clean(name));
            d.Name = Path.GetFileNameWithoutExtension(d.Lnk);
            SyncStorage(d);
            using (Bitmap face = MakeFace(d, null, 256)) UpdateIcon(d, face);
            Save(d);
            W.SHChangeNotify(0x2, 0x0005 | 0x1000, d.Lnk, IntPtr.Zero);
        }
        catch (Exception ex) { MessageBox.Show("Could not create the folder.\n" + ex.Message, "DeskFolders"); }
    }

    public static void Rename(FolderData d, string name)
    {
        try
        {
            name = Clean(name);
            if (name == d.Name && File.Exists(d.Lnk)) return;
            string target = FreeLnk(name);
            bool existed = d.Lnk.Length > 0 && File.Exists(d.Lnk);
            if (existed) File.Move(d.Lnk, target);
            d.Lnk = target;
            d.Name = Path.GetFileNameWithoutExtension(target);
            SyncStorage(d);
            if (!existed)
                using (Bitmap w2 = LoadWallpaper(d))
                using (Bitmap face = MakeFace(d, w2, 256)) UpdateIcon(d, face);
            d.Name = Path.GetFileNameWithoutExtension(target);
        }
        catch (Exception ex) { MessageBox.Show("Could not rename the folder.\n" + ex.Message, "DeskFolders"); }
    }

    public static void Delete(FolderData d)
    {
        foreach (Item it in d.Items) Restore(d, it.Path);
        try { if (d.Lnk.Length > 0 && File.Exists(d.Lnk)) { File.Delete(d.Lnk); W.SHChangeNotify(0x4, 0x0005, d.Lnk, IntPtr.Zero); } } catch { }
        try
        {
            foreach (string f in Directory.GetFiles(Desktop, "*.lnk"))
                if (ShortcutArgs(f).IndexOf(d.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    File.Delete(f);
                    W.SHChangeNotify(0x4, 0x0005, f, IntPtr.Zero);
                }
        }
        catch { }
        try { if (Directory.Exists(FolderDir(d.Id))) Directory.Delete(FolderDir(d.Id), true); } catch { }
        try { if (d.Store.Length > 0 && Directory.Exists(d.Store) && Directory.GetFileSystemEntries(d.Store).Length == 0) Directory.Delete(d.Store); } catch { }
    }

    static bool DriveOk(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && Directory.Exists(root);
        }
        catch { return false; }
    }

    static bool Gone(string path)
    {
        return !File.Exists(path) && !Directory.Exists(path) && DriveOk(path);
    }

    static List<string> StoreEntries(string store)
    {
        List<string> list = new List<string>();
        if (store.Length == 0 || !Directory.Exists(store)) return list;
        foreach (string e in Directory.GetFileSystemEntries(store))
        {
            try
            {
                FileAttributes a = File.GetAttributes(e);
                if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                list.Add(e);
            }
            catch { }
        }
        return list;
    }

    static bool NeedsSync(string store, List<string> items)
    {
        Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in items)
        {
            if (Gone(p)) return true;
            known[p] = true;
        }
        foreach (string e in StoreEntries(store))
            if (!known.ContainsKey(e)) return true;
        return false;
    }

    public static bool SyncItems(FolderData d)
    {
        bool changed = false;
        for (int i = d.Items.Count - 1; i >= 0; i--)
            if (Gone(d.Items[i].Path)) { d.Items.RemoveAt(i); changed = true; }
        Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (Item it in d.Items) known[it.Path] = true;
        foreach (string e in StoreEntries(d.Store))
            if (!known.ContainsKey(e))
            {
                d.Items.Add(MakeItem(e));
                changed = true;
            }
        return changed;
    }

    static void Resync(string id)
    {
        using (System.Threading.Mutex m = new System.Threading.Mutex(false, "DeskFolders_" + id))
        {
            bool got;
            try { got = m.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { got = true; }
            if (!got) return;
            try
            {
                FolderData f = Load(id);
                if (f != null && SyncItems(f))
                {
                    Save(f);
                    using (Bitmap w2 = LoadWallpaper(f))
                    using (Bitmap face = MakeFace(f, w2, 256)) UpdateIcon(f, face);
                }
            }
            catch { }
            finally { m.ReleaseMutex(); }
        }
    }

    static List<string> Sweep()
    {
        List<string> gone = new List<string>();
        try
        {
            if (!Directory.Exists(Dir)) return gone;
            foreach (string sub in Directory.GetDirectories(Dir))
            {
                try
                {
                    string file = Path.Combine(sub, "folder.txt");
                    if (!File.Exists(file)) continue;
                    FolderData d = new FolderData();
                    d.Id = Path.GetFileName(sub);
                    string store = "";
                    List<string> items = new List<string>();
                    foreach (string line in File.ReadAllLines(file))
                    {
                        if (line.StartsWith("D|")) store = line.Substring(2);
                        else if (line.StartsWith("L|")) d.Lnk = line.Substring(2);
                        else if (line.StartsWith("I|")) items.Add(line.Substring(2));
                    }
                    if (store.Length == 0 || Directory.Exists(store))
                    {
                        if (NeedsSync(store, items)) Resync(d.Id);
                        continue;
                    }
                    string drive = Path.GetPathRoot(store);
                    if (string.IsNullOrEmpty(drive) || !Directory.Exists(drive)) continue;
                    using (System.Threading.Mutex m = new System.Threading.Mutex(false, "DeskFolders_" + d.Id))
                    {
                        bool got;
                        try { got = m.WaitOne(0); }
                        catch (System.Threading.AbandonedMutexException) { got = true; }
                        if (!got) continue;
                        try
                        {
                            if (!Directory.Exists(store))
                            {
                                Delete(d);
                                gone.Add(d.Id);
                            }
                        }
                        finally { m.ReleaseMutex(); }
                    }
                }
                catch { }
            }
        }
        catch { }
        return gone;
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool StartupEnabled()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("DeskFoldersWatch") != null;
        }
        catch { return false; }
    }

    static void SetStartup(bool on)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("DeskFoldersWatch", "\"" + Application.ExecutablePath + "\" --watch");
                else k.DeleteValue("DeskFoldersWatch", false);
            }
        }
        catch { }
    }

    static IntPtr DesktopList()
    {
        IntPtr view = W.FindWindowEx(W.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr w = IntPtr.Zero;
        while (view == IntPtr.Zero)
        {
            w = W.FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
            if (w == IntPtr.Zero) break;
            view = W.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return view == IntPtr.Zero ? IntPtr.Zero : W.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    static bool Renaming()
    {
        IntPtr list = DesktopList();
        return list != IntPtr.Zero && W.FindWindowEx(list, IntPtr.Zero, "Edit", null) != IntPtr.Zero;
    }

    static void ConvertNew()
    {
        string[] files = Directory.GetFiles(Desktop, "*.deskfolder");
        if (files.Length == 0 || Renaming()) return;
        foreach (string f in files)
        {
            if ((DateTime.Now - File.GetCreationTime(f)).TotalSeconds < 1.5) continue;
            Convert(f);
        }
    }

    static void Convert(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try { File.Delete(file); } catch { return; }
        W.SHChangeNotify(0x4, 0x0005, file, IntPtr.Zero);
        Create(name);
        try
        {
            string dir = Path.GetDirectoryName(file);
            W.SHChangeNotify(0x1000, 0x0005 | 0x1000, dir, IntPtr.Zero);
            if (!string.Equals(dir, Desktop, StringComparison.OrdinalIgnoreCase))
                W.SHChangeNotify(0x1000, 0x0005 | 0x1000, Desktop, IntPtr.Zero);
        }
        catch { }
    }

    static void Watch()
    {
        bool isNew;
        using (System.Threading.Mutex m = new System.Threading.Mutex(true, "DeskFolders_Watcher", out isNew))
        {
            if (!isNew) return;
            using (System.Threading.EventWaitHandle stop = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, "DeskFolders_WatcherStop"))
            using (NotifyIcon tray = new NotifyIcon())
            {
                tray.Icon = AppIcon;
                tray.Text = "DeskFolders";
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Opening += delegate
                {
                    menu.Items.Clear();
                    menu.Items.Add("New folder...", null, delegate
                    {
                        string name = Prompt("New folder name", "Folder");
                        if (name != null) Create(name);
                    });
                    ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows");
                    startup.Checked = StartupEnabled();
                    startup.Click += delegate { SetStartup(!StartupEnabled()); };
                    menu.Items.Add(startup);
                    menu.Items.Add("Exit", null, delegate { Application.Exit(); });
                };
                menu.Items.Add("Exit");
                tray.ContextMenuStrip = menu;
                tray.Visible = true;
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 1500;
                timer.Tick += delegate
                {
                    if (stop.WaitOne(0)) { Application.Exit(); return; }
                    try { Sweep(); } catch { }
                    try { ConvertNew(); } catch { }
                };
                timer.Start();
                Application.Run();
                tray.Visible = false;
            }
        }
    }

    static void EnsureWatcher()
    {
        try
        {
            System.Threading.Mutex existing;
            if (System.Threading.Mutex.TryOpenExisting("DeskFolders_Watcher", out existing))
            {
                existing.Dispose();
                return;
            }
            Process.Start(Application.ExecutablePath, "--watch");
        }
        catch { }
    }

    static void StopWatcher()
    {
        try
        {
            using (System.Threading.EventWaitHandle stop = System.Threading.EventWaitHandle.OpenExisting("DeskFolders_WatcherStop"))
                stop.Set();
            System.Threading.Thread.Sleep(500);
        }
        catch { }
    }

    static void Register()
    {
        try
        {
            string exe = Application.ExecutablePath;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\DeskFolders"))
            {
                if (k.GetValue("StartupSet") == null)
                {
                    SetStartup(true);
                    k.SetValue("StartupSet", 1);
                }
                else if (StartupEnabled()) SetStartup(true);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "DeskFolders");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("Publisher", "DeskFolders");
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1);
                k.SetValue("NoRepair", 1);
            }
            bool changed;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.deskfolder\ShellNew"))
            {
                changed = k.GetValue("NullFile") == null || k.GetValue("Command") != null;
                k.DeleteValue("Command", false);
                k.SetValue("NullFile", "");
                k.SetValue("IconPath", exe + ",0");
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.deskfolder"))
                k.SetValue("", "DeskFolders.Folder");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DeskFolders.Folder"))
                k.SetValue("", "DeskFolder");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DeskFolders.Folder\DefaultIcon"))
                k.SetValue("", exe + ",0");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\DeskFolders.Folder\shell\open\command"))
                k.SetValue("", "\"" + exe + "\" --convert \"%1\"");
            if (changed) W.SHChangeNotify(0x08000000, 0, null, IntPtr.Zero);
        }
        catch { }
    }

    static void UninstallAll()
    {
        DialogResult r = MessageBox.Show("Uninstall DeskFolders?\nEverything inside your folders goes back to the desktop.",
            "DeskFolders", MessageBoxButtons.YesNo);
        if (r != DialogResult.Yes) return;
        StopWatcher();
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                k.DeleteValue("DeskFoldersWatch", false);
        }
        catch { }
        try
        {
            if (Directory.Exists(Dir))
                foreach (string sub in Directory.GetDirectories(Dir))
                {
                    FolderData d = Load(Path.GetFileName(sub));
                    if (d == null) continue;
                    Resolve(d);
                    SyncStorage(d);
                    Delete(d);
                }
        }
        catch { }
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
        try { if (Directory.Exists(Root) && Directory.GetFileSystemEntries(Root).Length == 0) Directory.Delete(Root); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DeskFolders", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.deskfolder", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DeskFolders.Folder", false); } catch { }
        try { W.SHChangeNotify(0x08000000, 0, null, IntPtr.Zero); } catch { }
        MessageBox.Show("DeskFolders was uninstalled.", "DeskFolders");
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del \"" + Application.ExecutablePath + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch { }
    }

    public static string Prompt(string title, string value)
    {
        using (Form f = new Form())
        {
            f.Text = title;
            f.Icon = AppIcon;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.TopMost = true;
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.ClientSize = new Size(300, 84);
            TextBox tb = new TextBox();
            tb.Text = value;
            tb.SetBounds(12, 12, 276, 24);
            Button ok = new Button();
            ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
            ok.SetBounds(122, 46, 80, 28);
            Button cancel = new Button();
            cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(208, 46, 80, 28);
            f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            if (f.ShowDialog() == DialogResult.OK && tb.Text.Trim().Length > 0) return tb.Text.Trim();
            return null;
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        W.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Point from = Cursor.Position;

        if (args.Length >= 1 && args[0] == "--uninstall")
        {
            UninstallAll();
            return;
        }
        if (args.Length >= 2 && args[0] == "--convert")
        {
            Register();
            EnsureWatcher();
            if (File.Exists(args[1])) Convert(args[1]);
            return;
        }
        if (args.Length >= 1 && args[0] == "--watch")
        {
            Watch();
            return;
        }
        Register();
        List<string> gone = Sweep();
        EnsureWatcher();

        if (args.Length >= 2 && args[0] == "--open")
        {
            if (gone.Contains(args[1])) return;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(false, "DeskFolders_" + args[1]))
            {
                bool got;
                try { got = mutex.WaitOne(args.Length > 2 ? 8000 : 0); }
                catch (System.Threading.AbandonedMutexException) { got = true; }
                if (!got) return;
                FolderData d = Load(args[1]);
                if (d == null)
                {
                    MessageBox.Show("This folder no longer exists.", "DeskFolders");
                    return;
                }
                Resolve(d);
                SyncStorage(d);
                Save(d);
                bool write = !HasIcon(d);
                if (SyncItems(d)) { Save(d); write = true; }
                if (args.Length > 2)
                {
                    write = true;
                    string[] extra = new string[args.Length - 2];
                    Array.Copy(args, 2, extra, 0, extra.Length);
                    int before = d.Items.Count;
                    AddPaths(d, extra);
                    Save(d);
                    if (d.Items.Count != before && LastError != null) MessageBox.Show(LastError, "DeskFolders");
                    if (d.Items.Count == before)
                        MessageBox.Show("Nothing was added.\n" + (LastError ?? "The dropped items were not found or are already in this folder."), "DeskFolders");
                    using (Bitmap w2 = LoadWallpaper(d))
                    using (Bitmap face = MakeFace(d, w2, 256)) UpdateIcon(d, face);
                    return;
                }
                FolderForm form = new FolderForm(d, from);
                if (write) form.RebuildFace(true);
                Application.Run(form);
            }
            return;
        }

        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                k.DeleteValue("DeskFolders", false);
        }
        catch { }
        string name = Prompt("New folder name", "Folder");
        if (name != null) Create(name);
    }
}
