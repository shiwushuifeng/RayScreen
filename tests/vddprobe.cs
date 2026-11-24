using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class VddProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetINFClassW(string inf, ref Guid classGuid, StringBuilder className, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr parent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name, ref Guid classGuid, string desc, IntPtr parent, uint flags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    static string logPath = @"C:\DSH NEXT\develop\RayScreen\tests\vdd-probe.log";

    static void L(string s)
    {
        try { File.AppendAllText(logPath, s + Environment.NewLine); } catch { }
    }

    static void Main()
    {
        string inf = @"C:\DSH NEXT\develop\RayScreen\vdd\VirtualDisplayDriver\MttVDD.inf";
        L("inf exists = " + File.Exists(inf));
        string[] names = new string[] { @"Root\MttVDD", @"ROOT\MttVDD", @"Root\MttVDD\0000", "MttVDD", @"Root\VirtualDisplay" };
        uint[] flags = new uint[] { 1, 0 };
        foreach (string nm in names)
        {
            foreach (uint fl in flags)
            {
                Guid cg = Guid.Empty;
                StringBuilder cn = new StringBuilder(64);
                uint req;
                SetupDiGetINFClassW(inf, ref cg, cn, 64, out req);
                IntPtr s2 = SetupDiCreateDeviceInfoList(ref cg, IntPtr.Zero);
                SP_DEVINFO_DATA d2 = new SP_DEVINFO_DATA();
                d2.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                bool ok = SetupDiCreateDeviceInfoW(s2, nm, ref cg, null, IntPtr.Zero, fl, ref d2);
                int err = Marshal.GetLastWin32Error();
                L(string.Format("name=[{0}] flags=0x{1:X} -> {2} err=0x{3:X8}", nm, fl, ok ? "OK" : "FAIL", err));
                if (s2 != IntPtr.Zero && s2 != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(s2);
            }
        }
        L("done");
    }
}
