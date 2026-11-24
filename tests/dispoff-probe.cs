using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

static class DispOffProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    struct SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_INFO { public SOURCE_INFO sourceInfo; public TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] payload;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths,
        ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topologyId);
    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint numPath, [In] PATH_INFO[] paths, uint numMode,
        [In] MODE_INFO[] modes, uint flags);

    const uint QDC_ALL_PATHS = 0x1;
    const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    const uint SDC_TOPOLOGY_INTERNAL = 0x00000010;
    const uint SDC_APPLY = 0x00000080;
    const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    const uint SDC_ALLOW_CHANGES = 0x00000400;
    const uint SDC_VALIDATE = 0x00000040;
    const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;

    static void Main()
    {
        Console.WriteLine("PATH_INFO=" + Marshal.SizeOf(typeof(PATH_INFO)) + " MODE_INFO=" + Marshal.SizeOf(typeof(MODE_INFO)));

        uint np, nm;
        int rc = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out np, out nm);
        Console.WriteLine("GetDisplayConfigBufferSizes rc=" + rc + " paths=" + np + " modes=" + nm);
        PATH_INFO[] paths = new PATH_INFO[np];
        MODE_INFO[] modes = new MODE_INFO[nm];
        for (int i = 0; i < modes.Length; i++) modes[i].payload = new byte[48];
        rc = QueryDisplayConfig(QDC_ALL_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
        Console.WriteLine("QueryDisplayConfig rc=" + rc + " paths=" + np + " modes=" + nm);
        int active = 0;
        for (int i = 0; i < np; i++)
        {
            bool isActive = (paths[i].flags & 0x1) != 0;
            if (isActive) active++;
            Console.WriteLine("  path[" + i + "] active=" + isActive + " srcId=" + paths[i].sourceInfo.id +
                " targetId=" + paths[i].targetInfo.id + " tech=" + paths[i].targetInfo.outputTechnology +
                " avail=" + paths[i].targetInfo.targetAvailable);
        }
        Console.WriteLine("active paths=" + active);

        int v = SetDisplayConfig(0, null, 0, null, SDC_VALIDATE | SDC_ALLOW_CHANGES | SDC_USE_SUPPLIED_DISPLAY_CONFIG);
        Console.WriteLine("validate-all-off rc=" + v);

        // 安全网：25 秒后无论如何都切回内置屏
        Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 25 >nul & DisplaySwitch.exe /internal")
        { UseShellExecute = false, CreateNoWindow = true });

        if (v == 0)
        {
            int a = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_ALLOW_CHANGES | SDC_USE_SUPPLIED_DISPLAY_CONFIG);
            Console.WriteLine("apply-all-off rc=" + a);
            Thread.Sleep(6000);
            int r = SetDisplayConfig(0, null, 0, null, SDC_TOPOLOGY_INTERNAL | SDC_APPLY);
            Console.WriteLine("restore-internal rc=" + r);
            if (r != 0)
            {
                Process.Start(new ProcessStartInfo("DisplaySwitch.exe", "/internal") { UseShellExecute = false, CreateNoWindow = true });
                Console.WriteLine("fallback DisplaySwitch /internal");
            }
            Thread.Sleep(1500);
            rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm);
            QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
            Console.WriteLine("after restore active paths=" + np);
        }
        Console.WriteLine("DONE");
    }
}
