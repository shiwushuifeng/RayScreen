// RayScreen — 跑任务时只关屏、不休眠
// 原理：现代待机（S0）机器上，只要 Windows 认为「显示器已关」，就会连带进入连接待机
//       （表现为风扇停转、指示灯灭、任务被降速）。所以这里反过来做：
//       一直持有 ES_DISPLAY_REQUIRED（系统认为屏幕必须亮着 → 永不进连接待机），
//       再把屏幕「变黑」——亮度压到 0 + 盖一层全黑置顶窗口，用户看到的是一块黑屏，
//       但整机仍在全速运行（风扇照转）。
// 编译：csc /target:winexe（GUI）与 /define:CLI /target:exe（命令行）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("RayScreen")]
[assembly: System.Reflection.AssemblyProduct("RayScreen")]
[assembly: System.Reflection.AssemblyDescription("任务运行期间阻止系统休眠，显示器照常关闭")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

static class Native
{
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    public const uint WM_SYSCOMMAND = 0x0112;
    public const int SC_MONITORPOWER = 0xF170;
    public const int MONITOR_OFF = 2;
    public const int MONITOR_ON = -1;

    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    public const int SystemExecutionState = 16;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint flags);

    [DllImport("kernel32.dll")]
    public static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    // ---- 现代待机（S0 低电量待机）下更可靠的保活方式：power request ----
    [StructLayout(LayoutKind.Sequential)]
    public struct POWER_REQUEST_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    public const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    public const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    public const int PowerRequestDisplayRequired = 0;
    public const int PowerRequestSystemRequired = 1;
    public const int PowerRequestAwayModeRequired = 2;
    public const int PowerRequestExecutionRequired = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr PowerCreateRequest(ref POWER_REQUEST_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("powrprof.dll")]
    public static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, out uint output, uint outputSize);
}

static class Power
{
    static bool active;

    public static uint LastPreviousState;
    public static bool PowerRequestActive;
    public static bool DisplayRequiredActive;

    public static bool Active
    {
        get { return active; }
    }

    /// 同时阻止系统睡眠与「关屏引发的连接待机」：始终带着 ES_DISPLAY_REQUIRED。
    public static bool KeepAwake()
    {
        const uint flags = Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED;

        uint prev = Native.SetThreadExecutionState(flags);
        LastPreviousState = prev;
        bool execOk = prev != 0;

        // 现代待机（S0）机器上，仅靠 SetThreadExecutionState 常在关屏后失效，
        // 系统会自己滑进连接待机（表现为风扇停转、整机像睡了）。再挂一个 power request。
        bool reqOk = UsePowerRequest();

        DisplayRequiredActive = execOk;
        active = execOk || reqOk;
        return active;
    }

    /// 周期性重申保活请求（防止关屏/待机切换后状态被系统清掉）。
    public static void Reassert()
    {
        if (!active) return;
        Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED);
        if (requestHandle != IntPtr.Zero)
        {
            Native.PowerSetRequest(requestHandle, Native.PowerRequestSystemRequired);
            Native.PowerSetRequest(requestHandle, Native.PowerRequestExecutionRequired);
        }
    }

    /// 释放请求，恢复系统默认电源行为。
    public static void Release()
    {
        Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
        ClearPowerRequest();
        DisplayRequiredActive = false;
        PowerRequestActive = false;
        active = false;
    }

    static IntPtr requestHandle = IntPtr.Zero;
    static IntPtr reasonPtr = IntPtr.Zero;

    static bool UsePowerRequest()
    {
        if (requestHandle != IntPtr.Zero) return true;
        if (reasonPtr == IntPtr.Zero)
            reasonPtr = Marshal.StringToHGlobalUni("RayScreen：任务运行中，保持系统唤醒");

        Native.POWER_REQUEST_CONTEXT ctx = new Native.POWER_REQUEST_CONTEXT();
        ctx.Version = Native.POWER_REQUEST_CONTEXT_VERSION;
        ctx.Flags = Native.POWER_REQUEST_CONTEXT_SIMPLE_STRING;
        ctx.SimpleReasonString = reasonPtr;

        IntPtr h = Native.PowerCreateRequest(ref ctx);
        if (h == IntPtr.Zero) return false;

        bool ok = Native.PowerSetRequest(h, Native.PowerRequestSystemRequired);
        bool ok2 = Native.PowerSetRequest(h, Native.PowerRequestExecutionRequired);
        requestHandle = h;
        PowerRequestActive = ok && ok2;
        return PowerRequestActive;
    }

    static void ClearPowerRequest()
    {
        if (requestHandle == IntPtr.Zero) return;
        Native.PowerClearRequest(requestHandle, Native.PowerRequestSystemRequired);
        Native.PowerClearRequest(requestHandle, Native.PowerRequestExecutionRequired);
        Native.CloseHandle(requestHandle);
        requestHandle = IntPtr.Zero;
    }

    /// 发送 SC_MONITORPOWER。
    /// 必须广播给所有顶层窗口（实际由 explorer 的 shell 窗口处理）；
    /// 只发给桌面窗口在 Win10/11 上是空操作，显示器不会关。
    public static bool SendMonitorCommand(int param)
    {
        const uint SMTO_ABORTIFHUNG = 0x0002;
        IntPtr result;

        bool posted = Native.PostMessage(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND,
            (IntPtr)Native.SC_MONITORPOWER, (IntPtr)param);

        // 少数环境只认同步消息，再补一次（带超时，避免被卡死）
        Native.SendMessageTimeout(Native.GetDesktopWindow(), Native.WM_SYSCOMMAND,
            (IntPtr)Native.SC_MONITORPOWER, (IntPtr)param, SMTO_ABORTIFHUNG, 1000, out result);

        return posted;
    }

    public static void MonitorOff()
    {
        SendMonitorCommand(Native.MONITOR_OFF);
    }

    public static void MonitorOn()
    {
        SendMonitorCommand(Native.MONITOR_ON);
    }

    /// 连接待机（Modern Standby）是否已被注册表禁用：PlatformAoAcOverride=0 时，系统关屏不会再进待机。
    public static bool ConnectedStandbyDisabled()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\\CurrentControlSet\\Control\\Power"))
            {
                if (k == null) return false;
                object v = k.GetValue("PlatformAoAcOverride");
                return v != null && Convert.ToInt32(v) == 0;
            }
        }
        catch { return false; }
    }

    /// 查询系统当前生效的执行状态标志（自检用）。
    public static uint QueryState()
    {
        uint st;
        Native.CallNtPowerInformation(Native.SystemExecutionState, IntPtr.Zero, 0, out st, 4);
        return st;
    }
}

/// 屏幕亮度（笔记本内置屏走 WMI，可在系统认为「屏幕亮着」的前提下把背光压到 0）。
static class Brightness
{
    public static bool Supported { get; private set; }
    public static int Current = -1;

    public static int Get()
    {
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("root\\WMI",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
            {
                foreach (ManagementObject o in s.Get())
                {
                    int v = Convert.ToInt32(o["CurrentBrightness"]);
                    Supported = true;
                    return v;
                }
            }
        }
        catch { }
        return -1;
    }

    public static bool Set(int value)
    {
        if (value < 0) value = 0;
        if (value > 100) value = 100;
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("root\\WMI",
                "SELECT * FROM WmiMonitorBrightnessMethods"))
            {
                foreach (ManagementObject o in s.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)value });
                    Supported = true;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}

/// 盖满全部显示器的全黑置顶窗口：系统层面屏幕仍是「亮着」的，用户看到的却是黑屏。
class OverlayForm : Form
{
    public OverlayForm(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW，不进 Alt+Tab
            return cp;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        Blanker.NotifyUserInput();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Blanker.NotifyUserInput();
    }
}

/// 黑屏控制器：亮度 0 + 全黑遮罩，任何键盘/鼠标输入都会点亮。
static class Blanker
{
    static readonly List<Form> forms = new List<Form>();
    static Thread thread;
    static int savedBrightness = -1;

    public static bool On { get; private set; }
    public static event EventHandler Woke;

    public static void Blank()
    {
        if (On) return;

        savedBrightness = Brightness.Get();
        Brightness.Current = 0;
        Brightness.Set(0);

        thread = new Thread(RunOverlays);
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        On = true;
    }

    public static void Unblank()
    {
        if (!On) return;
        On = false;

        Form first = null;
        lock (forms)
        {
            if (forms.Count > 0) first = forms[0];
        }
        try
        {
            if (first != null && first.IsHandleCreated)
                first.BeginInvoke(new MethodInvoker(CloseOverlays));
        }
        catch { }

        if (savedBrightness >= 0) Brightness.Set(savedBrightness > 5 ? savedBrightness : 50);
        savedBrightness = -1;
    }

    static void CloseOverlays()
    {
        lock (forms)
        {
            for (int i = 0; i < forms.Count; i++)
            {
                try { forms[i].Close(); } catch { }
            }
            forms.Clear();
        }
        Application.ExitThread();
    }

    static void RunOverlays()
    {
        try
        {
            List<Form> made = new List<Form>();
            foreach (Screen s in Screen.AllScreens)
            {
                made.Add(new OverlayForm(s.Bounds));
            }
            if (made.Count == 0) return;

            made[0].Shown += delegate
            {
                for (int i = 1; i < made.Count; i++)
                {
                    try { made[i].Show(); } catch { }
                }
                try { made[0].Activate(); } catch { }
            };

            lock (forms)
            {
                forms.Clear();
                forms.AddRange(made);
            }

            if (!On) { CloseOverlays(); return; }
            Application.Run(made[0]);
        }
        catch { }
    }

    internal static void NotifyUserInput()
    {
        EventHandler h = Woke;
        if (h != null) h(null, EventArgs.Empty);
    }
}

#if CLI
static class Cli
{
    const string Version = "RayScreen 1.0.0";

    static void Line(string s)
    {
        Console.WriteLine(s);
    }

    static void PrintUsage()
    {
        Line(Version + " — 跑任务时只关屏、不休眠");
        Line("");
        Line("用法：");
        Line("  RayScreen-cli.exe -- <命令>               守护 + 执行命令，命令结束后自动恢复");
        Line("  RayScreen-cli.exe --screen-off -- <命令>  同上，并在开始时立即让屏幕变黑");
        Line("  RayScreen-cli.exe                         只守护，按 Ctrl+C 结束");
        Line("  RayScreen-cli.exe --selftest              自检：打印电源请求状态");
        Line("");
        Line("示例：");
        Line("  RayScreen-cli.exe --screen-off -- ffmpeg -i in.mp4 out.mkv");
        Line("  RayScreen-cli.exe -- cmd /c build.cmd");
        Line("");
        Line("返回码 = 被包裹命令的返回码；命令不存在时为 9009。");
    }

    public static int Main(string[] args)
    {
        try { Native.SetConsoleOutputCP(65001); } catch { }
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        if (args.Length >= 1 && args[0] == "--task-host")
            return TaskHost();

        return RunMain(args, Environment.CommandLine);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr h);

    static string[] SplitArgs(string line)
    {
        List<string> list = new List<string>();
        if (line == null || line.Trim().Length == 0) return list.ToArray();
        int n;
        IntPtr p = CommandLineToArgvW("x.exe " + line, out n);
        if (p == IntPtr.Zero) { list.Add(line); return list.ToArray(); }
        try
        {
            for (int i = 1; i < n; i++)
            {
                IntPtr sp = Marshal.ReadIntPtr(p, i * IntPtr.Size);
                list.Add(Marshal.PtrToStringUni(sp));
            }
        }
        finally { LocalFree(p); }
        return list.ToArray();
    }

    // 由计划任务以管理员身份静默启动：参数从文件读，输出写文件，避免每次弹 UAC
    static int TaskHost()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RayScreen");
        try { Directory.CreateDirectory(dir); } catch { }
        string argFile = Path.Combine(dir, "args.txt");
        string outFile = Path.Combine(dir, "out.txt");
        string doneFile = Path.Combine(dir, "done.txt");
        try { if (File.Exists(doneFile)) File.Delete(doneFile); } catch { }

        string rawLine = "";
        try { if (File.Exists(argFile)) rawLine = File.ReadAllText(argFile, Encoding.UTF8).Trim(); } catch { }

        string[] argv = SplitArgs(rawLine);

        TextWriter old = Console.Out;
        StreamWriter sw = new StreamWriter(outFile, false, new UTF8Encoding(false));
        sw.AutoFlush = true;
        Console.SetOut(sw);
        int rc;
        try { rc = RunMain(argv, "RayScreen-cli.exe " + rawLine); }
        finally { try { sw.Flush(); } catch { } Console.SetOut(old); try { sw.Dispose(); } catch { } }
        try { File.WriteAllText(doneFile, rc.ToString()); } catch { }
        return rc;
    }

    static int RunMain(string[] args, string raw)
    {
        bool screenOff = false;
        bool screenOn = false;
        bool selfTest = false;
        bool help = false;
        string command = null;

        // 命令部分按原始命令行切分，避免参数里的引号被拆坏。
        int sep = raw.IndexOf(" -- ", StringComparison.Ordinal);
        if (sep >= 0)
        {
            command = raw.Substring(sep + 4).Trim();
        }
        else if (raw.EndsWith(" --", StringComparison.Ordinal))
        {
            command = "";
        }

        bool beforeSep = true;
        foreach (string a in args)
        {
            if (!beforeSep) break;
            if (a == "--") { beforeSep = false; break; }
            if (a == "--screen-off" || a == "-s") screenOff = true;
            else if (a == "--screen-on" || a == "-o") screenOn = true;
            else if (a == "--selftest") selfTest = true;
            else if (a == "--help" || a == "-h" || a == "/?") help = true;
        }

        if (help)
        {
            PrintUsage();
            return 0;
        }

        if (screenOn)
        {
            VScreen.Probe();
            bool ok = VScreen.RecoverInternal();
            Line(ok ? "  已重新点亮内屏。" : "  [!] 恢复内屏失败（需要管理员权限或已无可用内屏）。");
            return ok ? 0 : 5;
        }

        if (selfTest)
            return RunSelfTest();

        if (command != null)
            return RunCommand(command, screenOff);

        return RunInteractive();
    }

    static int RunSelfTest()
    {
        Line(Version + " 自检");
        uint before = Power.QueryState();
        Line("  当前系统执行状态: 0x" + before.ToString("X8"));

        bool accepted = Power.KeepAwake();
        if (accepted)
            Line("  SetThreadExecutionState: 成功（返回前一状态 0x" + Power.LastPreviousState.ToString("X8") + "）");
        else
            Line("  SetThreadExecutionState: 失败，错误码 " + Marshal.GetLastWin32Error());

        Thread.Sleep(1200);
        uint mid = Power.QueryState();
        Line("  1.2 秒后状态: 0x" + mid.ToString("X8"));

        bool systemRequired = (mid & Native.ES_SYSTEM_REQUIRED) != 0;
        bool displayRequired = (mid & Native.ES_DISPLAY_REQUIRED) != 0;

        Line("    PowerRequest（防连接待机）      : " + (Power.PowerRequestActive ? "已启用 ✓" : "启用失败 ✗"));
        Line("    系统保活 ES_SYSTEM_REQUIRED  : " + (systemRequired ? "已生效 ✓" : "未生效 ✗"));
        Line("    屏幕常亮 ES_DISPLAY_REQUIRED : " + (displayRequired ? "已生效 ✓ 系统不会进连接待机" : "未生效 ✗"));
        Line("    亮度控制（WMI）              : " + (Brightness.Get() >= 0 ? "可用 ✓ 可把背光压到 0" : "不可用 · 只靠全黑遮罩"));
        Line("    连接待机（S0）               : " + (Power.ConnectedStandbyDisabled()
            ? "已在注册表禁用（重启后生效）· 关屏只灭背光" : "仍启用 · 关屏会连带进待机"));
        Line("  说明: ES_CONTINUOUS 是调用控制位，不出现在系统状态里；线程存活期间请求一直有效。");

        Power.Release();
        Thread.Sleep(500);
        uint after = Power.QueryState();
        Line("  释放后状态: 0x" + after.ToString("X8") +
             (after == mid ? "（仍有其他程序持有保活请求，属正常）" : ""));

        bool ok = accepted && systemRequired && displayRequired;
        Line(ok ? "结论: 通过 — 系统保持全速运行，屏幕由程序自己变黑。" : "结论: 未通过。");
        return ok ? 0 : 2;
    }

    static int RunCommand(string command, bool screenOff)
    {
        if (command.Length == 0)
        {
            Line("[!] “--” 后面没有命令。");
            PrintUsage();
            return 2;
        }

        if (!Power.KeepAwake())
        {
            Line("[!] 无法设置电源请求，错误码 " + Marshal.GetLastWin32Error());
            return 3;
        }

        Line(Version);
        Line("  守护中：系统保持全速运行（不会睡眠、不会进连接待机）。");
        bool realOff = false;
        bool vScreen = false;
        if (screenOff)
        {
            string verr = null;
            if (VScreen.SwitchOff(out verr))
            {
                vScreen = true;
                Line("  屏幕：已切到虚拟屏（内屏断电），按任意键或用 --screen-on 恢复");
            }
            else
            {
                realOff = Power.ConnectedStandbyDisabled();
                if (realOff) Power.MonitorOff();
                else Blanker.Blank();
            }
        }
        Line("  执行: " + command);
        Line("");

        int exitCode = -1;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"");
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Environment.CurrentDirectory;

            using (Process p = Process.Start(psi))
            {
                if (p == null)
                {
                    Line("[!] 无法启动命令。");
                    return 4;
                }
                p.WaitForExit();
                exitCode = p.ExitCode;
            }
        }
        finally
        {
            if (screenOff)
            {
                if (vScreen) VScreen.Restore();
                else if (realOff) Power.MonitorOn();
                else Blanker.Unblank();
            }
            Power.Release();
        }

        Line("");
        Line("  完成，返回码 " + exitCode + "；已恢复正常电源行为。");
        return exitCode;
    }

    static int RunInteractive()
    {
        if (!Power.KeepAwake())
        {
            Line("[!] 无法设置电源请求，错误码 " + Marshal.GetLastWin32Error());
            return 3;
        }

        ManualResetEvent done = new ManualResetEvent(false);
        ConsoleCancelEventHandler handler = delegate(object s, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            done.Set();
        };
        Console.CancelKeyPress += handler;

        Line(Version);
        Line("  守护中：系统保持全速运行（不会睡眠、不会进连接待机）。");
        Line("  按 Ctrl+C 结束并恢复。");

        done.WaitOne();
        Console.CancelKeyPress -= handler;

        Blanker.Unblank();
        Power.Release();
        Line("");
        Line("  已恢复正常电源行为。");
        return 0;
    }
}
#endif

#if !CLI
static class Program
{
    [STAThread]
    static void Main()
    {
        try { Native.SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

sealed class RoundedButton : Button
{
    public int Radius = 8;
    Color baseColor = Color.FromArgb(47, 111, 222);

    public Color BaseColor
    {
        get { return baseColor; }
        set { baseColor = value; Invalidate(); }
    }

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 10F);
        ForeColor = Color.White;
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        GraphicsPath p = new GraphicsPath();
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        using (GraphicsPath p = Round(new Rectangle(0, 0, Width, Height), Radius))
        {
            Region old = Region;
            Region = new Region(p);
            if (old != null) old.Dispose();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill = baseColor;
        if (!Enabled) fill = Color.FromArgb(205, 210, 219);
        else if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
            fill = Color.FromArgb(Math.Min(255, fill.R + 16), Math.Min(255, fill.G + 16), Math.Min(255, fill.B + 16));

        using (GraphicsPath p = Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (SolidBrush b = new SolidBrush(fill))
        {
            g.FillPath(b, p);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle,
            Enabled ? ForeColor : Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

sealed class MainForm : Form
{
    readonly Label lblState;
    readonly Label lblSub;
    readonly Label lblTime;
    readonly Label lblDot;
    readonly CheckBox chkOff;
    readonly RoundedButton btnToggle;
    readonly RoundedButton btnWake;
    readonly System.Windows.Forms.Timer tick;
    readonly Panel card;

    bool guarding;
    bool screenIsOff;
    bool vScreenMode;
    bool monitorOffMode;      // true = 用系统指令真关屏；false = 黑屏遮罩
    DateTime since;
    int ticks;
    DateTime watchStart;
    int watchTicks;
    bool watchPending;

    static readonly Color Accent = Color.FromArgb(47, 111, 222);
    static readonly Color Danger = Color.FromArgb(214, 69, 69);
    static readonly Color Ink = Color.FromArgb(32, 37, 46);
    static readonly Color Muted = Color.FromArgb(122, 130, 145);
    static readonly Color Idle = Color.FromArgb(178, 185, 196);
    static readonly Color Good = Color.FromArgb(30, 172, 108);

    static void ScaleTree(Control c, float s)
    {
        for (int i = 0; i < c.Controls.Count; i++) ScaleTree(c.Controls[i], s);
        if (c is Form) return;
        c.Location = new Point((int)Math.Round(c.Left * s), (int)Math.Round(c.Top * s));
        if (!c.AutoSize) c.Size = new Size((int)Math.Round(c.Width * s), (int)Math.Round(c.Height * s));
    }

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "RayScreen — 关屏不休眠";
        ClientSize = new Size(472, 348);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(246, 247, 250);
        Font = new Font("Microsoft YaHei UI", 9F);

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Label title = new Label();
        title.Text = "RayScreen";
        title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        title.ForeColor = Ink;
        title.AutoSize = true;
        title.Location = new Point(26, 20);
        Controls.Add(title);

        Label sub = new Label();
        sub.Text = "任务运行期间阻止系统休眠，显示器照常关闭";
        sub.ForeColor = Muted;
        sub.AutoSize = true;
        sub.Location = new Point(28, 56);
        Controls.Add(sub);

        card = new Panel();
        card.BackColor = Color.White;
        card.Location = new Point(26, 88);
        card.Size = new Size(420, 108);
        card.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(228, 231, 237)))
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        Controls.Add(card);

        lblDot = new Label();
        lblDot.Text = "●";
        lblDot.Font = new Font("Segoe UI", 15F);
        lblDot.ForeColor = Idle;
        lblDot.AutoSize = true;
        lblDot.Location = new Point(18, 22);
        card.Controls.Add(lblDot);

        lblState = new Label();
        lblState.Text = "未启用";
        lblState.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        lblState.ForeColor = Ink;
        lblState.AutoSize = true;
        lblState.Location = new Point(44, 16);
        card.Controls.Add(lblState);

        lblSub = new Label();
        lblSub.Text = "系统可能照常睡眠/休眠";
        lblSub.ForeColor = Muted;
        lblSub.AutoSize = true;
        lblSub.Location = new Point(46, 50);
        card.Controls.Add(lblSub);

        lblTime = new Label();
        lblTime.Text = "00:00:00";
        lblTime.Font = new Font("Consolas", 12F);
        lblTime.ForeColor = Color.FromArgb(150, 157, 170);
        lblTime.AutoSize = false;
        lblTime.TextAlign = ContentAlignment.MiddleRight;
        lblTime.Size = new Size(96, 24);
        lblTime.Location = new Point(302, 18);
        card.Controls.Add(lblTime);

        chkOff = new CheckBox();
        chkOff.Text = "开始后立即变黑屏（整机照常运行，按任意键点亮）";
        chkOff.AutoSize = true;
        chkOff.ForeColor = Color.FromArgb(70, 77, 90);
        chkOff.Location = new Point(28, 214);
        Controls.Add(chkOff);

        btnToggle = new RoundedButton();
        btnToggle.Text = "开始守护";
        btnToggle.BaseColor = Accent;
        btnToggle.Location = new Point(26, 250);
        btnToggle.Size = new Size(200, 44);
        btnToggle.Click += delegate { Toggle(); };
        Controls.Add(btnToggle);

        btnWake = new RoundedButton();
        btnWake.Text = "点亮屏幕";
        btnWake.BaseColor = Color.FromArgb(108, 117, 133);
        btnWake.Location = new Point(246, 250);
        btnWake.Size = new Size(200, 44);
        btnWake.Click += delegate { WakeScreen(); };
        Controls.Add(btnWake);

        Label foot = new Label();
        foot.Text = "关闭窗口即恢复正常电源行为；不修改任何电源计划。";
        foot.ForeColor = Color.FromArgb(150, 157, 170);
        foot.AutoSize = true;
        foot.Location = new Point(28, 312);
        Controls.Add(foot);

        // 按系统 DPI 放大布局几何（字体本身已由 GDI 按 DPI 渲染，无需再缩放）
        float ui = 1f;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) ui = g.DpiX / 96f;
        if (ui > 1.01f || ui < 0.99f)
        {
            ClientSize = new Size((int)Math.Round(472 * ui), (int)Math.Round(348 * ui));
            ScaleTree(this, ui);
            lblTime.Left = card.Width - lblTime.Width - (int)Math.Round(22 * ui);
            lblTime.Top = (int)Math.Round(18 * ui);
        }

        Blanker.Woke += delegate
        {
            if (IsHandleCreated) BeginInvoke(new MethodInvoker(OnUserWoke));
        };

        tick = new System.Windows.Forms.Timer();
        tick.Interval = 500;
        tick.Tick += delegate
        {
            if (!guarding) return;
            TimeSpan d = DateTime.Now - since;
            lblTime.Text = string.Format("{0:00}:{1:00}:{2:00}", (int)d.TotalHours, d.Minutes, d.Seconds);

            // 每 15 秒重申一次保活，避免关屏/待机切换后被系统清掉
            if (++ticks % 30 == 0) Power.Reassert();

            // 看门狗：真关屏后如果进程被冻结过（tick 明显缺拍），说明系统还是进了待机，
            // 立刻点亮并改用黑屏遮罩，保证任务继续跑。
            if (watchPending && (DateTime.Now - watchStart).TotalSeconds >= 8)
            {
                watchPending = false;
                if (ticks - watchTicks < 6)
                {
                    monitorOffMode = false;
                    Power.MonitorOn();
                    Blanker.Blank();
                    screenIsOff = true;
                    RefreshStatus();
                }
            }
        };

        FormClosing += delegate
        {
            if (guarding) Stop(true);
            VScreen.Restore();
            WakeHook.Stop();
        };

        WakeHook.OnWake = delegate
        {
            if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate
            {
                if (vScreenMode) WakeScreen();
            }));
        };
    }

    void Toggle()
    {
        if (guarding) Stop(false);
        else Start();
    }

    void Start()
    {
        if (!Power.KeepAwake())
        {
            MessageBox.Show(this, "无法设置电源请求，可能被安全策略拦截。", "RayScreen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        guarding = true;
        since = DateTime.Now;
        lblTime.Text = "00:00:00";
        tick.Start();

        if (chkOff.Checked)
        {
            screenIsOff = true;
            string err = null;
            // 首选：把桌面切到虚拟屏，内屏失去信号 → 背光真灭，系统照常全速运行
            if (VScreen.SwitchOff(out err))
            {
                vScreenMode = true;
                WakeHook.Start();
            }
            else
            {
                monitorOffMode = Power.ConnectedStandbyDisabled();
                if (monitorOffMode)
                {
                    Power.MonitorOff();
                    watchPending = true;
                    watchStart = DateTime.Now;
                    watchTicks = ticks;
                }
                else
                {
                    Blanker.Blank();
                }
            }
        }

        btnToggle.Text = "停止守护";
        btnToggle.BaseColor = Danger;
        chkOff.Enabled = false;
        RefreshStatus();
    }

    void Stop(bool silent)
    {
        if (screenIsOff)
        {
            if (vScreenMode)
            {
                VScreen.Restore();
                WakeHook.Stop();
                vScreenMode = false;
            }
            else if (monitorOffMode) Power.MonitorOn();
            else Blanker.Unblank();
            screenIsOff = false;
            monitorOffMode = false;
        }
        Power.Release();
        guarding = false;
        tick.Stop();

        btnToggle.Text = "开始守护";
        btnToggle.BaseColor = Accent;
        chkOff.Enabled = true;
        RefreshStatus();

        if (!silent)
        {
            lblTime.Text = "00:00:00";
        }
    }

    void WakeScreen()
    {
        if (vScreenMode)
        {
            VScreen.Restore();
            WakeHook.Stop();
            vScreenMode = false;
        }
        else
        {
            if (monitorOffMode) Power.MonitorOn();
            Blanker.Unblank();
        }
        if (screenIsOff)
        {
            screenIsOff = false;
            monitorOffMode = false;
            RefreshStatus();
        }
    }

    void OnUserWoke()
    {
        if (screenIsOff)
        {
            screenIsOff = false;
            RefreshStatus();
        }
    }

    void RefreshStatus()
    {
        if (guarding)
        {
            lblDot.ForeColor = Good;
            lblState.Text = "守护中";
            lblSub.Text = screenOffText();
        }
        else
        {
            lblDot.ForeColor = Idle;
            lblState.Text = "未启用";
            lblSub.Text = "系统可能照常睡眠/休眠";
        }
    }

    string screenOffText()
    {
        if (!screenIsOff) return "系统全速运行 · 屏幕保持常亮";
        if (vScreenMode) return "系统全速运行 · 内屏已断电（按任意键点亮）";
        if (monitorOffMode) return "系统全速运行 · 显示器已关闭";
        return "系统全速运行 · 屏幕已变黑";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshStatus();
    }
}
#endif


static class VScreen
{
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] struct SRC_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    struct TGT_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public SRC_INFO sourceInfo; public TGT_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] payload; }
    [StructLayout(LayoutKind.Sequential)] struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags; public uint outputTechnology;
        public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr currentTopologyId);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME info);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPath, [In] PATH_INFO[] paths, uint numMode, [In] MODE_INFO[] modes, uint flags);

    const uint QDC_ALL_PATHS = 0x1;
    const uint SDC_APPLY = 0x80, SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20, SDC_ALLOW_CHANGES = 0x400;
    const uint PATH_ACTIVE = 0x1;

    static PATH_INFO[] savedPaths;
    static MODE_INFO[] savedModes;
    static uint savedPathCount, savedModeCount;

    /// <summary>虚拟显示器（VDD）是否已安装并可用。</summary>
    public static bool Available { get; private set; }
    /// <summary>当前是否已经把桌面切到虚拟屏（内屏断电）。</summary>
    public static bool Switched { get; private set; }
    public static string VirtualName = "";

    static string NameOf(TGT_INFO t)
    {
        TARGET_DEVICE_NAME n = new TARGET_DEVICE_NAME();
        n.header.type = 2;
        n.header.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
        n.header.adapterId = t.adapterId;
        n.header.id = t.id;
        if (DisplayConfigGetDeviceInfo(ref n) != 0) return "";
        return n.monitorFriendlyDeviceName ?? "";
    }

    static bool Query(out PATH_INFO[] paths, out MODE_INFO[] modes, out uint np, out uint nm)
    {
        paths = null; modes = null; np = 0; nm = 0;
        uint np0, nm0;
        if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out np0, out nm0) != 0) return false;
        PATH_INFO[] pa = new PATH_INFO[np0];
        MODE_INFO[] ma = new MODE_INFO[nm0];
        if (QueryDisplayConfig(QDC_ALL_PATHS, ref np0, pa, ref nm0, ma, IntPtr.Zero) != 0) return false;
        paths = pa; modes = ma; np = np0; nm = nm0;
        return true;
    }

    /// <summary>探测是否存在名为 VDD 的虚拟显示器。</summary>
    public static bool Probe()
    {
        PATH_INFO[] pa; MODE_INFO[] ma; uint np, nm;
        Available = false;
        if (!Query(out pa, out ma, out np, out nm)) return false;
        for (int i = 0; i < (int)np; i++)
        {
            if (!pa[i].targetInfo.targetAvailable) continue;
            string nm2 = NameOf(pa[i].targetInfo);
            if (nm2.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                VirtualName = nm2;
                Available = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>保存当前显示配置并把桌面切到虚拟屏（内屏失去信号 → 背光熄灭，系统照跑）。</summary>
    public static bool SwitchOff(out string error)
    {
        error = null;
        PATH_INFO[] pa; MODE_INFO[] ma; uint np, nm;
        if (!Query(out pa, out ma, out np, out nm)) { error = "读取显示配置失败"; return false; }

        List<PATH_INFO> act = new List<PATH_INFO>();
        for (int i = 0; i < (int)np; i++)
            if ((pa[i].flags & PATH_ACTIVE) != 0) act.Add(pa[i]);
        savedPaths = act.ToArray();
        savedPathCount = (uint)savedPaths.Length;
        savedModes = ma;
        savedModeCount = nm;
        if (savedPathCount == 0) { error = "没有活动的显示输出"; return false; }

        PATH_INFO only = new PATH_INFO();
        bool found = false;
        for (int i = 0; i < (int)np; i++)
        {
            if (!pa[i].targetInfo.targetAvailable) continue;
            string nm2 = NameOf(pa[i].targetInfo);
            if (nm2.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) < 0) continue;
            only = pa[i];
            only.flags |= PATH_ACTIVE;
            only.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
            only.targetInfo.modeInfoIdx = 0xFFFFFFFF;
            VirtualName = nm2;
            Available = true;
            found = true;
            break;
        }
        if (!found) { error = "没有找到虚拟显示器"; return false; }

        PATH_INFO[] arr = new PATH_INFO[] { only };
        int rc = SetDisplayConfig(1, arr, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
        if (rc != 0) { error = "切换显示输出失败 rc=" + rc + "（需要管理员权限）"; return false; }
        Switched = true;
        return true;
    }

    /// <summary>兜底恢复：在没有保存配置时，直接重新点亮内屏（用于程序异常退出后的抢救）。</summary>
    public static bool RecoverInternal()
    {
        PATH_INFO[] pa; MODE_INFO[] ma; uint np, nm;
        if (!Query(out pa, out ma, out np, out nm)) return false;
        PATH_INFO only = new PATH_INFO();
        bool found = false;
        for (int i = 0; i < (int)np; i++)
        {
            if (!pa[i].targetInfo.targetAvailable) continue;
            string nm2 = NameOf(pa[i].targetInfo);
            if (nm2.Length == 0) continue;
            if (nm2.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            only = pa[i];
            only.flags |= PATH_ACTIVE;
            only.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
            only.targetInfo.modeInfoIdx = 0xFFFFFFFF;
            found = true;
            break;
        }
        if (!found) return false;
        PATH_INFO[] arr = new PATH_INFO[] { only };
        int rc = SetDisplayConfig(1, arr, 0, null, SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
        if (rc == 0) { Switched = false; return true; }
        return false;
    }

    /// <summary>恢复切换前的显示配置（内屏重新点亮）。</summary>
    public static bool Restore()
    {
        if (!Switched) return true;
        if (savedPaths == null || savedPathCount == 0) { Switched = false; return false; }
        int rc = SetDisplayConfig(savedPathCount, savedPaths, savedModeCount, savedModes,
            SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_APPLY);
        if (rc == 0) { Switched = false; return true; }
        return false;
    }
}

static class WakeHook
{
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);

    static HookProc kproc, mproc;
    static IntPtr khook, mhook;

    /// <summary>屏幕切到虚拟屏后，按下任意键 / 点击鼠标即回调（用来点亮内屏）。</summary>
    public static Action OnWake;

    public static void Start()
    {
        if (khook != IntPtr.Zero || mhook != IntPtr.Zero) return;
        kproc = delegate(int c, IntPtr w, IntPtr l)
        {
            if (c >= 0 && OnWake != null) OnWake();
            return CallNextHookEx(khook, c, w, l);
        };
        mproc = delegate(int c, IntPtr w, IntPtr l)
        {
            // 只认鼠标左/右键按下，避免移动鼠标就点亮
            int msg = w.ToInt32();
            if (c >= 0 && (msg == 0x0201 || msg == 0x0204) && OnWake != null) OnWake();
            return CallNextHookEx(mhook, c, w, l);
        };
        IntPtr mod = GetModuleHandle(null);
        khook = SetWindowsHookEx(13, kproc, mod, 0); // WH_KEYBOARD_LL
        mhook = SetWindowsHookEx(14, mproc, mod, 0); // WH_MOUSE_LL
    }

    public static void Stop()
    {
        if (khook != IntPtr.Zero) { UnhookWindowsHookEx(khook); khook = IntPtr.Zero; }
        if (mhook != IntPtr.Zero) { UnhookWindowsHookEx(mhook); mhook = IntPtr.Zero; }
        kproc = null; mproc = null;
    }
}
