using System;
using System.Runtime.InteropServices;
using System.Threading;

static class MonitorProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();
    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint flags);
    [DllImport("powrprof.dll")]
    static extern uint CallNtPowerInformation(int info, IntPtr inBuf, uint inSize, out uint outBuf, uint outSize);

    const uint ES_CONTINUOUS = 0x80000000;
    const uint ES_SYSTEM_REQUIRED = 0x00000001;
    const uint ES_DISPLAY_REQUIRED = 0x00000002;
    static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
    const uint WM_SYSCOMMAND = 0x0112;
    static readonly IntPtr SC_MONITORPOWER = new IntPtr(0xF170);

    static uint State()
    {
        uint v;
        CallNtPowerInformation(16, IntPtr.Zero, 0, out v, 4);
        return v;
    }

    static void Main()
    {
        Console.WriteLine("准备 1 秒后关屏，保持 6 秒，然后恢复。");
        uint prev = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        Console.WriteLine("SetThreadExecutionState(含 DISPLAY_REQUIRED) -> 0x" + prev.ToString("X8"));
        Thread.Sleep(1000);
        Console.WriteLine("系统执行状态 = 0x" + State().ToString("X8") + "（期望含 0x2 标志）");

        PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, new IntPtr(2));
        Console.WriteLine("已发送关屏命令，时间 = " + DateTime.Now.ToString("HH:mm:ss"));
        Thread.Sleep(6000);

        PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, new IntPtr(-1));
        Console.WriteLine("已发送亮屏命令，时间 = " + DateTime.Now.ToString("HH:mm:ss"));
        Thread.Sleep(800);
        SetThreadExecutionState(ES_CONTINUOUS);
        Console.WriteLine("已释放，结束。");
    }
}
