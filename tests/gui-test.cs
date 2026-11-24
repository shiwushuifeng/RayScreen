// 自动化验收：启动 GUI、点“开始守护”、截图、点“停止守护”、校验文案
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class GuiTest
{
    const uint BM_CLICK = 0x00F5;
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string name);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static string TextOf(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static List<IntPtr> Children(IntPtr root)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    static IntPtr Find(IntPtr root, string text)
    {
        foreach (IntPtr h in Children(root))
            if (TextOf(h) == text) return h;
        return IntPtr.Zero;
    }

    static string AllTexts(IntPtr root)
    {
        List<string> parts = new List<string>();
        foreach (IntPtr h in Children(root))
        {
            string t = TextOf(h);
            if (t.Length > 0) parts.Add(t);
        }
        return string.Join(" | ", parts.ToArray());
    }

    static void Shot(IntPtr win, string path)
    {
        IntPtr topmost = new IntPtr(-1);
        IntPtr notop = new IntPtr(-2);
        SetWindowPos(win, topmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Thread.Sleep(500);

        RECT wr;
        GetWindowRect(win, out wr);
        int pad = 14;
        Rectangle vs = new Rectangle(wr.Left - pad, wr.Top - pad, wr.Right - wr.Left + pad * 2, wr.Bottom - wr.Top + pad * 2);
        using (Bitmap bmp = new Bitmap(vs.Width, vs.Height))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, bmp.Size);
            bmp.Save(path, ImageFormat.Png);
        }
        SetWindowPos(win, notop, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Console.WriteLine("shot: " + path);
    }

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDPIAware();
        string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string exe = System.IO.Path.Combine(dir, "..", "bin", "RayScreen.exe");

        Process p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        IntPtr win = IntPtr.Zero;
        for (int i = 0; i < 50 && win == IntPtr.Zero; i++)
        {
            Thread.Sleep(200);
            win = FindWindow(null, "RayScreen — 关屏不休眠");
        }
        if (win == IntPtr.Zero) { Console.WriteLine("FAIL: 窗口没出现"); return 1; }

        SetForegroundWindow(win);
        Thread.Sleep(600);
        RECT wr;
        GetWindowRect(win, out wr);
        Console.WriteLine("窗口物理尺寸: " + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top) + "（逻辑 472x348）");
        Console.WriteLine("初始: " + AllTexts(win));
        Shot(win, System.IO.Path.Combine(dir, "gui-idle.png"));

        IntPtr start = Find(win, "开始守护");
        if (start == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到开始按钮"); return 2; }
        SendMessage(start, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(1800);
        string guarding = AllTexts(win);
        Console.WriteLine("守护中: " + guarding);
        Shot(win, System.IO.Path.Combine(dir, "gui-guarding.png"));

        IntPtr stop = Find(win, "停止守护");
        if (stop == IntPtr.Zero) { Console.WriteLine("FAIL: 按钮没有切换成停止"); return 3; }
        SendMessage(stop, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(800);
        string idle = AllTexts(win);
        Console.WriteLine("已停止: " + idle);

        bool ok = guarding.Contains("守护中") && guarding.Contains("停止守护") &&
                  idle.Contains("未启用") && idle.Contains("开始守护");
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");

        SendMessage(win, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(800);
        if (!p.HasExited) p.Kill();
        return ok ? 0 : 4;
    }
}
