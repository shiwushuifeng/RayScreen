using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class GuiVTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern void SetProcessDPIAware();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    delegate bool EnumProc(IntPtr h, IntPtr lp);
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

    static string Screens()
    {
        StringBuilder sb = new StringBuilder();
        foreach (Screen s in Screen.AllScreens)
            sb.Append(s.DeviceName + " " + s.Bounds.Width + "x" + s.Bounds.Height + "  ");
        return sb.ToString().Trim();
    }

    static string State(IntPtr win)
    {
        List<string> parts = new List<string>();
        foreach (IntPtr h in Kids(win))
        {
            string t = TextOf(h);
            if (t.Length > 0) parts.Add(t);
        }
        return string.Join(" | ", parts.ToArray());
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
        Console.WriteLine("初始屏幕: " + Screens());

        IntPtr chk = Find(win, "开始后立即变黑屏（整机照常运行，按任意键点亮）");
        if (chk == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到复选框"); p.Kill(); return 2; }
        SendMessage(chk, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(300);

        IntPtr start = Find(win, "开始守护");
        if (start == IntPtr.Zero) { Console.WriteLine("FAIL: 找不到开始按钮"); p.Kill(); return 3; }
        SendMessage(start, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(3000);
        Console.WriteLine("守护后屏幕: " + Screens());

        // 模拟按一次 A 键，验证「按任意键点亮」
        keybd_event(0x41, 0, 0, IntPtr.Zero);
        keybd_event(0x41, 0, 2, IntPtr.Zero);
        Thread.Sleep(2500);
        Console.WriteLine("按键后屏幕: " + Screens());
        Console.WriteLine("状态: " + State(win));

        IntPtr stop = Find(win, "停止守护");
        if (stop == IntPtr.Zero) { Console.WriteLine("FAIL: 按钮没有切换成停止"); p.Kill(); return 4; }
        SendMessage(stop, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(2500);
        Console.WriteLine("停止后屏幕: " + Screens());
        Console.WriteLine("状态: " + State(win));

        p.Kill();
        bool ok = true;
        if (Screen.AllScreens.Length != 1) ok = false;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: CHECK");
        return ok ? 0 : 5;
    }
}
