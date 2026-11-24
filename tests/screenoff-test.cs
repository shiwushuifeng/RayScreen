using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class ScreenOffTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern void SetProcessDPIAware();

    delegate bool EnumProc(IntPtr h, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    const int BM_CLICK = 0x00F5;

    static List<IntPtr> Kids(IntPtr root)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr lp) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    static string TextOf(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static IntPtr Find(IntPtr root, string text)
    {
        foreach (IntPtr h in Kids(root))
            if (TextOf(h) == text) return h;
        return IntPtr.Zero;
    }

    static void Shot(IntPtr win, string path)
    {
        RECT wr;
        GetWindowRect(win, out wr);
        Rectangle r = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
        using (Bitmap bmp = new Bitmap(r.Width, r.Height))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
            bmp.Save(path, ImageFormat.Png);
        }
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
        Thread.Sleep(500);

        IntPtr chk = Find(win, "开始后立即关闭显示器（移动鼠标或按任意键唤醒）");
        if (chk == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到复选框"); p.Kill(); return 2; }
        SendMessage(chk, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(300);

        IntPtr start = Find(win, "开始守护");
        if (start == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到开始按钮"); p.Kill(); return 3; }
        SendMessage(start, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(3000);

        Shot(win, System.IO.Path.Combine(dir, "screenoff-dark.png"));
        Console.WriteLine("状态(关屏后): " + string.Join(" | ", Kids(win).ConvertAll(delegate(IntPtr h) { return TextOf(h); }).FindAll(delegate(string s) { return s.Length > 0; }).ToArray()));

        IntPtr stop = Find(win, "停止守护");
        if (stop == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到停止按钮"); p.Kill(); return 4; }
        SendMessage(stop, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(2000);

        Shot(win, System.IO.Path.Combine(dir, "screenoff-after.png"));
        Console.WriteLine("状态(停止后): " + string.Join(" | ", Kids(win).ConvertAll(delegate(IntPtr h) { return TextOf(h); }).FindAll(delegate(string s) { return s.Length > 0; }).ToArray()));

        p.Kill();
        Console.WriteLine("RESULT: DONE");
        return 0;
    }
}
