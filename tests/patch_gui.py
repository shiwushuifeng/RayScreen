import io, re, os, sys

p = r"C:\DSH NEXT\develop\RayScreen\src\RayScreen.cs"
s = io.open(p, encoding="utf-8").read()

# 1) append topology + wake-hook classes at end of file
extra = r'''

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
'''

if "static class VScreen" not in s:
    s = s.rstrip() + "\n" + extra

# 2) GUI: field + Start/Stop/WakeScreen/RefreshStatus
s = s.replace("    bool screenIsOff;", "    bool screenIsOff;\n    bool vScreenMode;", 1)

old_start = """        if (chkOff.Checked)
        {
            screenIsOff = true;
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
        }"""
new_start = """        if (chkOff.Checked)
        {
            screenIsOff = true;
            string err = null;
            // 首选：把桌面切到虚拟屏，内屏失去信号 → 背光真灭，系统照常全速运行
            if (VScreen.Available && VScreen.SwitchOff(out err))
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
        }"""
assert old_start in s
s = s.replace(old_start, new_start, 1)

old_stop = """        if (screenIsOff)
        {
            if (monitorOffMode) Power.MonitorOn();
            else Blanker.Unblank();
            screenIsOff = false;
            monitorOffMode = false;
        }"""
new_stop = """        if (screenIsOff)
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
        }"""
assert old_stop in s
s = s.replace(old_stop, new_stop, 1)

old_wake = """    void WakeScreen()
    {
        if (monitorOffMode) Power.MonitorOn();
        Blanker.Unblank();
        if (screenIsOff)
        {
            screenIsOff = false;
            monitorOffMode = false;
            RefreshStatus();
        }
    }"""
new_wake = """    void WakeScreen()
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
    }"""
assert old_wake in s
s = s.replace(old_wake, new_wake, 1)

old_status = """            lblSub.Text = screenIsOff
                ? (monitorOffMode ? "系统全速运行 · 显示器已关闭" : "系统全速运行 · 屏幕已变黑")
                : "系统全速运行 · 屏幕保持常亮";"""
new_status = """            lblSub.Text = screenOffText();"""
assert old_status in s
s = s.replace(old_status, new_status, 1)

# add screenOffText helper before OnShown
anchor = "    protected override void OnShown(EventArgs e)"
helper = """    string screenOffText()
    {
        if (!screenIsOff) return "系统全速运行 · 屏幕保持常亮";
        if (vScreenMode) return "系统全速运行 · 内屏已断电（按任意键点亮）";
        if (monitorOffMode) return "系统全速运行 · 显示器已关闭";
        return "系统全速运行 · 屏幕已变黑";
    }

"""
assert anchor in s
s = s.replace(anchor, helper + anchor, 1)

# hook wiring in constructor: after FormClosing block
old_close = """        FormClosing += delegate
        {
            if (guarding) Stop(true);
        };"""
new_close = """        FormClosing += delegate
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
        };"""
assert old_close in s
s = s.replace(old_close, new_close, 1)

io.open(p, "w", encoding="utf-8").write(s)
print("patched GUI")
